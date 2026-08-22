using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class GetMonthlySummaryDerivedBalanceTests
{
    private static GetMonthlySummaryHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    /// <summary>
    /// July holds 30,000; August adds 22,480. Viewing July must show 30,000 —
    /// what the account held THEN, not the 52,480 it holds today (menunest-182).
    /// </summary>
    private static Guid SeedAccountWithTwoMonths(HandlerTestFixture fx)
    {
        var acc = BudgetAccount.Create(fx.Family.Id, "SCB", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 30_000m, new DateOnly(2026, 7, 15), "Opening balance", fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 22_480m, new DateOnly(2026, 8, 3), "Salary", fx.User.Id));
        return acc.Id;
    }

    [Fact]
    public async Task A_past_month_shows_the_balance_held_at_the_end_of_that_month()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7), CancellationToken.None);

        july.Accounts.Single().Balance.Should().Be(30_000m);
    }

    [Fact]
    public async Task The_current_month_shows_every_transaction_to_date()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var august = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 8), CancellationToken.None);

        august.Accounts.Single().Balance.Should().Be(52_480m);
    }

    [Fact]
    public async Task A_month_before_the_first_transaction_shows_zero()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var june = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 6), CancellationToken.None);

        june.Accounts.Single().Balance.Should().Be(0m);
    }

    [Fact]
    public async Task A_transaction_on_the_last_day_of_the_month_is_included()
    {
        // Guards the boundary: the filter is Date < firstOfNextMonth, not Date < lastDay.
        using var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "Cash", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 500m, new DateOnly(2026, 7, 31), "Late", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7), CancellationToken.None);

        july.Accounts.Single().Balance.Should().Be(500m);
    }

    [Fact]
    public async Task Ready_to_assign_uses_the_derived_balance_not_the_stored_one()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7), CancellationToken.None);

        // No envelopes, so Ready to Assign is the whole derived account total.
        july.ReadyToAssign.Should().Be(30_000m);
    }

    [Fact]
    public async Task Another_familys_transactions_never_leak_in()
    {
        using var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "Mine", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 100m, new DateOnly(2026, 8, 1), null, fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, 9_999m, new DateOnly(2026, 8, 1), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var august = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 8), CancellationToken.None);

        august.Accounts.Single().Balance.Should().Be(100m);
    }
}
