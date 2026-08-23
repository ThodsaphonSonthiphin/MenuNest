using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class CreateAccountHandlerTests
{
    // The app's one real time zone (menunest-189) — every user is in Thailand.
    private const string Bkk = "Asia/Bangkok";

    private static CreateAccountHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new CreateAccountValidator(), fx.Clock);

    [Fact]
    public async Task First_account_in_family_gets_sort_order_zero()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("SCB Savings", BudgetAccountType.Cash, OpeningBalance: 0m, TimeZoneId: null),
            CancellationToken.None);

        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Subsequent_account_gets_max_plus_one()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.BudgetAccounts.Add(BudgetAccount.Create(fx.Family.Id, "Cash", BudgetAccountType.Cash, 0m, 3));
        fx.Db.BudgetAccounts.Add(BudgetAccount.Create(fx.Family.Id, "KBank Credit", BudgetAccountType.Credit, 0m, 11));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("Wise", BudgetAccountType.Cash, 0m, null),
            CancellationToken.None);

        result.SortOrder.Should().Be(12);
    }

    [Fact]
    public async Task Max_is_scoped_to_calling_family_only()
    {
        using var fx = new HandlerTestFixture();
        var otherFamilyId = Guid.NewGuid();
        fx.Db.BudgetAccounts.Add(BudgetAccount.Create(otherFamilyId, "Other", BudgetAccountType.Cash, 0m, 99));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("Mine", BudgetAccountType.Cash, 0m, null),
            CancellationToken.None);

        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Rejects_blank_name()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new CreateAccountCommand("  ", BudgetAccountType.Cash, 0m, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Rejects_name_longer_than_120_characters()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new CreateAccountCommand(new string('a', 121), BudgetAccountType.Cash, 0m, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Opening_balance_is_written_as_an_uncategorised_transaction()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("SCB Savings", BudgetAccountType.Cash, 40_000m, Bkk),
            CancellationToken.None);

        var tx = fx.Db.BudgetTransactions.Single(t => t.AccountId == result.Id);
        tx.Amount.Should().Be(40_000m);
        tx.CategoryId.Should().BeNull();          // lands in Ready to Assign
        tx.Notes.Should().Be("Opening balance");
        result.Balance.Should().Be(40_000m);      // the cache still agrees
    }

    [Fact]
    public async Task A_zero_opening_balance_writes_no_transaction()
    {
        // BudgetTransaction.Create throws on a zero amount (BudgetTransaction.cs:27).
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("Empty", BudgetAccountType.Cash, 0m, null),
            CancellationToken.None);

        fx.Db.BudgetTransactions.Any(t => t.AccountId == result.Id).Should().BeFalse();
        result.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task A_negative_opening_balance_is_written_for_a_liability()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("KBank Credit", BudgetAccountType.Credit, -12_000m, Bkk),
            CancellationToken.None);

        fx.Db.BudgetTransactions.Single(t => t.AccountId == result.Id).Amount.Should().Be(-12_000m);
        result.Balance.Should().Be(-12_000m);
    }

    // ── menunest-189: the viewer's local day, not the server's UTC day ──

    [Fact]
    public async Task A_zero_opening_balance_ignores_a_missing_time_zone_because_no_date_is_needed()
    {
        // No transaction is written for a zero opening balance, so "today"
        // is never read — the zone must not be required in this case.
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("Empty", BudgetAccountType.Cash, 0m, TimeZoneId: null),
            CancellationToken.None);

        result.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task A_non_zero_opening_balance_with_a_missing_time_zone_throws_and_writes_nothing()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new CreateAccountCommand("SCB Savings", BudgetAccountType.Cash, 40_000m, TimeZoneId: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        fx.Db.BudgetAccounts.Should().BeEmpty(
            "the account row must not be persisted when the opening-balance transaction can't be dated");
    }

    [Fact]
    public async Task A_non_zero_opening_balance_with_an_unknown_time_zone_throws_and_writes_nothing()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new CreateAccountCommand("SCB Savings", BudgetAccountType.Cash, 40_000m, "Not/A/Real/Zone"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        fx.Db.BudgetAccounts.Should().BeEmpty();
    }

    /// <summary>
    /// 2026-08-31T20:00Z is 2026-09-01T03:00 in Bangkok (UTC+7) — the exact
    /// 00:00–07:00 ICT window the bug lived in: the server's UTC day (Aug 31)
    /// and the viewer's local day (Sep 1) disagree. If the handler still read
    /// <c>DateTime.UtcNow</c> directly, the opening transaction would be dated
    /// Aug 31 — the wrong month at this boundary — corrupting menunest-183's
    /// derived August balance instead of landing in September where the
    /// viewer actually created it.
    /// </summary>
    [Fact]
    public async Task Opening_balance_is_dated_on_the_Bangkok_day_during_the_UTC_lag_window()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc);
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("SCB Savings", BudgetAccountType.Cash, 40_000m, Bkk),
            CancellationToken.None);

        var tx = fx.Db.BudgetTransactions.Single(t => t.AccountId == result.Id);
        tx.Date.Should().Be(new DateOnly(2026, 9, 1),
            "the opening transaction must be dated on the viewer's Bangkok day, not the server's UTC day (still Aug 31)");
    }
}
