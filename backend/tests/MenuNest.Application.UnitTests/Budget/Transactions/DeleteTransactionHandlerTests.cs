using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Transactions.DeleteTransaction;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Transactions;

public class DeleteTransactionHandlerTests
{
    [Fact]
    public async Task Deleting_transaction_reverses_its_delta_on_the_account()
    {
        using var fx = new HandlerTestFixture();

        // Account started at 1000, had a -100 tx => balance 900.
        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 1000m, 0);
        acc.AdjustBalance(-100m);
        fx.Db.BudgetAccounts.Add(acc);

        var tx = BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, -100m,
            new DateOnly(2026, 4, 10), null, fx.User.Id);
        fx.Db.BudgetTransactions.Add(tx);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteTransactionHandler(fx.Db, fx.UserProvisioner.Object);

        await sut.Handle(new DeleteTransactionCommand(tx.Id), CancellationToken.None);

        fx.Db.BudgetTransactions.Any(t => t.Id == tx.Id).Should().BeFalse();
        fx.Db.BudgetAccounts.Single(a => a.Id == acc.Id).Balance.Should().Be(1000m);
    }

    [Fact]
    public async Task Throws_DomainException_when_transaction_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var foreignAcc = BudgetAccount.Create(otherFamily.Id, "Foreign", BudgetAccountType.Cash, 100m, 0);
        fx.Db.BudgetAccounts.Add(foreignAcc);
        var foreignTx = BudgetTransaction.Create(
            otherFamily.Id, foreignAcc.Id, null, -10m,
            new DateOnly(2026, 4, 10), null, fx.User.Id);
        fx.Db.BudgetTransactions.Add(foreignTx);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteTransactionHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new DeleteTransactionCommand(foreignTx.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Transaction not found*");
        fx.Db.BudgetTransactions.Any(t => t.Id == foreignTx.Id).Should().BeTrue();
    }
}
