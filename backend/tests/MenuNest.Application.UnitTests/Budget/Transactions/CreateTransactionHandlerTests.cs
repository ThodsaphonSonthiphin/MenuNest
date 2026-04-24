using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Transactions;

public class CreateTransactionHandlerTests
{
    [Fact]
    public async Task Creates_transaction_and_adjusts_account_balance_by_signed_amount()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 1000m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateTransactionHandler(fx.Db, fx.UserProvisioner.Object, new CreateTransactionValidator());

        var result = await sut.Handle(
            new CreateTransactionCommand(acc.Id, CategoryId: null, Amount: -250m,
                Date: new DateOnly(2026, 4, 10), Notes: "Groceries"),
            CancellationToken.None);

        result.AccountId.Should().Be(acc.Id);
        result.AccountName.Should().Be("Checking");
        result.Amount.Should().Be(-250m);
        result.CategoryId.Should().BeNull();
        result.Notes.Should().Be("Groceries");
        result.CreatedByUserId.Should().Be(fx.User.Id);
        result.CreatedByDisplayName.Should().Be(fx.User.DisplayName);

        var persisted = fx.Db.BudgetAccounts.Single(a => a.Id == acc.Id);
        persisted.Balance.Should().Be(750m);

        fx.Db.BudgetTransactions.Should().ContainSingle(t => t.Id == result.Id);
    }

    [Fact]
    public async Task Creates_uncategorized_transaction_when_categoryId_is_null()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Savings", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateTransactionHandler(fx.Db, fx.UserProvisioner.Object, new CreateTransactionValidator());

        var result = await sut.Handle(
            new CreateTransactionCommand(acc.Id, CategoryId: null, Amount: 5000m,
                Date: new DateOnly(2026, 4, 1), Notes: null),
            CancellationToken.None);

        result.CategoryId.Should().BeNull();
        result.CategoryName.Should().BeNull();
        result.CategoryEmoji.Should().BeNull();
        result.Amount.Should().Be(5000m);

        fx.Db.BudgetAccounts.Single(a => a.Id == acc.Id).Balance.Should().Be(5000m);
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

        var sut = new CreateTransactionHandler(fx.Db, fx.UserProvisioner.Object, new CreateTransactionValidator());

        var act = async () => await sut.Handle(
            new CreateTransactionCommand(foreignAcc.Id, CategoryId: null, Amount: -10m,
                Date: new DateOnly(2026, 4, 10), Notes: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Account not found*");
    }

    [Fact]
    public async Task Throws_DomainException_when_category_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var otherGroup = BudgetCategoryGroup.Create(otherFamily.Id, "G", 0);
        fx.Db.BudgetCategoryGroups.Add(otherGroup);
        var foreignCat = BudgetCategory.Create(otherFamily.Id, otherGroup.Id, "Foreign Cat", emoji: null, sortOrder: 0);
        fx.Db.BudgetCategories.Add(foreignCat);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateTransactionHandler(fx.Db, fx.UserProvisioner.Object, new CreateTransactionValidator());

        var act = async () => await sut.Handle(
            new CreateTransactionCommand(acc.Id, CategoryId: foreignCat.Id, Amount: -10m,
                Date: new DateOnly(2026, 4, 10), Notes: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Category not found*");
    }
}
