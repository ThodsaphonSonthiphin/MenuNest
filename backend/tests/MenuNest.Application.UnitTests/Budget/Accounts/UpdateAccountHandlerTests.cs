using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.UpdateAccount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class UpdateAccountHandlerTests
{
    [Fact]
    public async Task Updates_name_and_sort_order_of_existing_account()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Old Name", BudgetAccountType.Cash, 100m, 1);
        fx.Db.BudgetAccounts.Add(acc);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateAccountHandler(fx.Db, fx.UserProvisioner.Object, new UpdateAccountValidator());

        var result = await sut.Handle(
            new UpdateAccountCommand(acc.Id, "New Name", 7, IsClosed: false),
            CancellationToken.None);

        result.Name.Should().Be("New Name");
        result.SortOrder.Should().Be(7);
        result.Balance.Should().Be(100m); // untouched

        var reloaded = fx.Db.BudgetAccounts.Single(a => a.Id == acc.Id);
        reloaded.Name.Should().Be("New Name");
        reloaded.SortOrder.Should().Be(7);
    }

    [Fact]
    public async Task IsClosed_toggles_close_and_reopen()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Wallet", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateAccountHandler(fx.Db, fx.UserProvisioner.Object, new UpdateAccountValidator());

        // Close an open account.
        var closed = await sut.Handle(
            new UpdateAccountCommand(acc.Id, "Wallet", 0, IsClosed: true),
            CancellationToken.None);
        closed.IsClosed.Should().BeTrue();

        // Reopen a closed account.
        var reopened = await sut.Handle(
            new UpdateAccountCommand(acc.Id, "Wallet", 0, IsClosed: false),
            CancellationToken.None);
        reopened.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task Throws_DomainException_when_account_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);

        var foreignAcc = BudgetAccount.Create(otherFamily.Id, "Foreign", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(foreignAcc);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateAccountHandler(fx.Db, fx.UserProvisioner.Object, new UpdateAccountValidator());

        var act = async () => await sut.Handle(
            new UpdateAccountCommand(foreignAcc.Id, "Hacked", 0, IsClosed: false),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Account not found*");
    }
}
