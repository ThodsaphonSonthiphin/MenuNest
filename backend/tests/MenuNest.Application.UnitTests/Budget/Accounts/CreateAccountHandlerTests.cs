using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class CreateAccountHandlerTests
{
    private static CreateAccountHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new CreateAccountValidator());

    [Fact]
    public async Task First_account_in_family_gets_sort_order_zero()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("SCB Savings", BudgetAccountType.Cash, OpeningBalance: 0m),
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
            new CreateAccountCommand("Wise", BudgetAccountType.Cash, 0m),
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
            new CreateAccountCommand("Mine", BudgetAccountType.Cash, 0m),
            CancellationToken.None);

        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Rejects_blank_name()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new CreateAccountCommand("  ", BudgetAccountType.Cash, 0m),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Rejects_name_longer_than_120_characters()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new CreateAccountCommand(new string('a', 121), BudgetAccountType.Cash, 0m),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Opening_balance_is_written_as_an_uncategorised_transaction()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateAccountCommand("SCB Savings", BudgetAccountType.Cash, 40_000m),
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
            new CreateAccountCommand("Empty", BudgetAccountType.Cash, 0m),
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
            new CreateAccountCommand("KBank Credit", BudgetAccountType.Credit, -12_000m),
            CancellationToken.None);

        fx.Db.BudgetTransactions.Single(t => t.AccountId == result.Id).Amount.Should().Be(-12_000m);
        result.Balance.Should().Be(-12_000m);
    }
}
