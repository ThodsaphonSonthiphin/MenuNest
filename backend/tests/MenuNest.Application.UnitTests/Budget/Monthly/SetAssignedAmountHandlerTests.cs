using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class SetAssignedAmountHandlerTests
{
    [Fact]
    public async Task Creates_new_assignment_row_on_first_call()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator());

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, Year: 2026, Month: 4, Amount: 15000m),
            CancellationToken.None);

        var persisted = fx.Db.MonthlyAssignments.Single();
        persisted.FamilyId.Should().Be(fx.Family.Id);
        persisted.CategoryId.Should().Be(cat.Id);
        persisted.Year.Should().Be(2026);
        persisted.Month.Should().Be(4);
        persisted.AssignedAmount.Should().Be(15000m);
    }

    [Fact]
    public async Task Updates_existing_assignment_row_on_second_call()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator());

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 4, 15000m),
            CancellationToken.None);
        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 4, 20000m),
            CancellationToken.None);

        fx.Db.MonthlyAssignments.Should().HaveCount(1);
        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(20000m);
    }

    [Fact]
    public async Task Throws_DomainException_when_category_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var otherGroup = BudgetCategoryGroup.Create(otherFamily.Id, "Foreign", 0);
        fx.Db.BudgetCategoryGroups.Add(otherGroup);
        var foreignCat = BudgetCategory.Create(otherFamily.Id, otherGroup.Id, "Foreign Cat", null, 0);
        fx.Db.BudgetCategories.Add(foreignCat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator());

        var act = async () => await sut.Handle(
            new SetAssignedAmountCommand(foreignCat.Id, 2026, 4, 100m),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Category not found*");
    }
}
