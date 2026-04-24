using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Transactions.UpdateTransaction;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Transactions;

public class UpdateTransactionHandlerTests
{
    [Fact]
    public async Task Updating_amount_on_same_account_applies_net_delta_to_balance()
    {
        using var fx = new HandlerTestFixture();

        // Start the account at 1000, log an existing -100 tx so the live balance is 900.
        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 1000m, 0);
        acc.AdjustBalance(-100m);
        fx.Db.BudgetAccounts.Add(acc);

        var tx = BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, -100m,
            new DateOnly(2026, 4, 10), null, fx.User.Id);
        fx.Db.BudgetTransactions.Add(tx);
        await fx.Db.SaveChangesAsync();

        acc.Balance.Should().Be(900m); // sanity

        var sut = new UpdateTransactionHandler(fx.Db, fx.UserProvisioner.Object, new UpdateTransactionValidator());

        // Change -100 -> -250. Net delta = -150 from 900 => 750 (not 650).
        await sut.Handle(
            new UpdateTransactionCommand(tx.Id, acc.Id, CategoryId: null,
                Amount: -250m, Date: new DateOnly(2026, 4, 10), Notes: null),
            CancellationToken.None);

        fx.Db.BudgetAccounts.Single(a => a.Id == acc.Id).Balance.Should().Be(750m);
        fx.Db.BudgetTransactions.Single(t => t.Id == tx.Id).Amount.Should().Be(-250m);
    }

    [Fact]
    public async Task Moving_transaction_to_different_account_removes_from_old_and_applies_to_new()
    {
        using var fx = new HandlerTestFixture();

        // Old account started at 1000, had a -100 tx logged => balance 900.
        var oldAcc = BudgetAccount.Create(fx.Family.Id, "Old", BudgetAccountType.Cash, 1000m, 0);
        oldAcc.AdjustBalance(-100m);
        // New account started at 500, no activity yet.
        var newAcc = BudgetAccount.Create(fx.Family.Id, "New", BudgetAccountType.Cash, 500m, 1);
        fx.Db.BudgetAccounts.AddRange(oldAcc, newAcc);

        var tx = BudgetTransaction.Create(
            fx.Family.Id, oldAcc.Id, null, -100m,
            new DateOnly(2026, 4, 10), null, fx.User.Id);
        fx.Db.BudgetTransactions.Add(tx);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateTransactionHandler(fx.Db, fx.UserProvisioner.Object, new UpdateTransactionValidator());

        // Re-assign the same -100 tx from oldAcc to newAcc.
        await sut.Handle(
            new UpdateTransactionCommand(tx.Id, newAcc.Id, CategoryId: null,
                Amount: -100m, Date: new DateOnly(2026, 4, 10), Notes: null),
            CancellationToken.None);

        // Old account: reverse the -100 delta => back to 1000.
        // New account: apply -100 delta => 400.
        fx.Db.BudgetAccounts.Single(a => a.Id == oldAcc.Id).Balance.Should().Be(1000m);
        fx.Db.BudgetAccounts.Single(a => a.Id == newAcc.Id).Balance.Should().Be(400m);
        fx.Db.BudgetTransactions.Single(t => t.Id == tx.Id).AccountId.Should().Be(newAcc.Id);
    }

    [Fact]
    public async Task Changing_category_only_leaves_account_balance_unchanged()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 1000m, 0);
        acc.AdjustBalance(-100m); // balance 900
        fx.Db.BudgetAccounts.Add(acc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", emoji: null, sortOrder: 0);
        fx.Db.BudgetCategories.Add(cat);

        var tx = BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, -100m,
            new DateOnly(2026, 4, 10), null, fx.User.Id);
        fx.Db.BudgetTransactions.Add(tx);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateTransactionHandler(fx.Db, fx.UserProvisioner.Object, new UpdateTransactionValidator());

        await sut.Handle(
            new UpdateTransactionCommand(tx.Id, acc.Id, CategoryId: cat.Id,
                Amount: -100m, Date: new DateOnly(2026, 4, 10), Notes: null),
            CancellationToken.None);

        fx.Db.BudgetAccounts.Single(a => a.Id == acc.Id).Balance.Should().Be(900m);
        fx.Db.BudgetTransactions.Single(t => t.Id == tx.Id).CategoryId.Should().Be(cat.Id);
    }

    [Fact]
    public async Task Throws_DomainException_when_transaction_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var foreignAcc = BudgetAccount.Create(otherFamily.Id, "Foreign", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(foreignAcc);
        var foreignTx = BudgetTransaction.Create(
            otherFamily.Id, foreignAcc.Id, null, -10m,
            new DateOnly(2026, 4, 10), null, fx.User.Id);
        fx.Db.BudgetTransactions.Add(foreignTx);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateTransactionHandler(fx.Db, fx.UserProvisioner.Object, new UpdateTransactionValidator());

        var act = async () => await sut.Handle(
            new UpdateTransactionCommand(foreignTx.Id, foreignAcc.Id, CategoryId: null,
                Amount: -20m, Date: new DateOnly(2026, 4, 10), Notes: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Transaction not found*");
    }
}
