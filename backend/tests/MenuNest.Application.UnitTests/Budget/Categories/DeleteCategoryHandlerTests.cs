using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Categories.DeleteCategory;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Categories;

public class DeleteCategoryHandlerTests
{
    [Fact]
    public async Task Deletes_category_with_no_transactions()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteCategoryHandler(fx.Db, fx.UserProvisioner.Object);

        await sut.Handle(new DeleteCategoryCommand(cat.Id), CancellationToken.None);

        fx.Db.BudgetCategories.Any(c => c.Id == cat.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Throws_DomainException_when_a_transaction_references_the_category()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);

        var account = BudgetAccount.Create(fx.Family.Id, "Wallet", BudgetAccountType.Cash, 1000m, 0);
        fx.Db.BudgetAccounts.Add(account);
        var tx = BudgetTransaction.Create(
            fx.Family.Id, account.Id, cat.Id,
            amount: -500m, date: new DateOnly(2026, 4, 24),
            notes: null, createdByUserId: fx.User.Id);
        fx.Db.BudgetTransactions.Add(tx);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteCategoryHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new DeleteCategoryCommand(cat.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        fx.Db.BudgetCategories.Any(c => c.Id == cat.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Throws_DomainException_when_category_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var otherGroup = BudgetCategoryGroup.Create(otherFamily.Id, "Foreign", 0);
        fx.Db.BudgetCategoryGroups.Add(otherGroup);
        var foreign = BudgetCategory.Create(otherFamily.Id, otherGroup.Id, "Foreign Cat", null, 0);
        fx.Db.BudgetCategories.Add(foreign);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteCategoryHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new DeleteCategoryCommand(foreign.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Category not found*");
    }
}
