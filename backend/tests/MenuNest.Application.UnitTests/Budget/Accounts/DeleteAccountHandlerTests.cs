using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.DeleteAccount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class DeleteAccountHandlerTests
{
    [Fact]
    public async Task Deletes_account_with_no_transactions()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Wallet", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteAccountHandler(fx.Db, fx.UserProvisioner.Object);

        await sut.Handle(new DeleteAccountCommand(acc.Id), CancellationToken.None);

        fx.Db.BudgetAccounts.Any(a => a.Id == acc.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Throws_DomainException_when_account_has_transactions()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Wallet", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);

        var tx = BudgetTransaction.Create(
            fx.Family.Id, acc.Id, categoryId: null,
            amount: -10m, date: new DateOnly(2026, 4, 24), notes: null, createdByUserId: fx.User.Id);
        fx.Db.BudgetTransactions.Add(tx);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteAccountHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new DeleteAccountCommand(acc.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        fx.Db.BudgetAccounts.Any(a => a.Id == acc.Id).Should().BeTrue();
    }
}
