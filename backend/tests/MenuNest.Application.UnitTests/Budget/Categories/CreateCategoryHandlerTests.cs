using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Categories.CreateCategory;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Categories;

public class CreateCategoryHandlerTests
{
    [Fact]
    public async Task Creates_category_inside_a_group_with_no_target()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateCategoryHandler(
            fx.Db, fx.UserProvisioner.Object, new CreateCategoryValidator());

        var result = await sut.Handle(
            new CreateCategoryCommand(
                group.Id, "Rent", "🏠", 1,
                BudgetTargetType.None, TargetAmount: null,
                TargetDueDate: null, TargetDayOfMonth: null),
            CancellationToken.None);

        result.Name.Should().Be("Rent");
        result.Emoji.Should().Be("🏠");
        result.SortOrder.Should().Be(1);
        result.TargetType.Should().Be(BudgetTargetType.None);
        result.TargetAmount.Should().BeNull();
        result.TargetDueDate.Should().BeNull();
        result.TargetDayOfMonth.Should().BeNull();

        var persisted = fx.Db.BudgetCategories.Single(c => c.Id == result.Id);
        persisted.FamilyId.Should().Be(fx.Family.Id);
        persisted.GroupId.Should().Be(group.Id);
    }

    [Fact]
    public async Task Creates_category_with_monthly_amount_target()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateCategoryHandler(
            fx.Db, fx.UserProvisioner.Object, new CreateCategoryValidator());

        var result = await sut.Handle(
            new CreateCategoryCommand(
                group.Id, "Rent", null, 0,
                BudgetTargetType.MonthlyAmount, TargetAmount: 15000m,
                TargetDueDate: null, TargetDayOfMonth: 1),
            CancellationToken.None);

        result.TargetType.Should().Be(BudgetTargetType.MonthlyAmount);
        result.TargetAmount.Should().Be(15000m);
        result.TargetDayOfMonth.Should().Be(1);
        result.TargetDueDate.Should().BeNull();
    }

    [Fact]
    public async Task Creates_category_with_by_date_target()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Goals", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateCategoryHandler(
            fx.Db, fx.UserProvisioner.Object, new CreateCategoryValidator());

        var dueDate = new DateOnly(2026, 12, 31);
        var result = await sut.Handle(
            new CreateCategoryCommand(
                group.Id, "Vacation", "🌴", 0,
                BudgetTargetType.ByDate, TargetAmount: 30000m,
                TargetDueDate: dueDate, TargetDayOfMonth: null),
            CancellationToken.None);

        result.TargetType.Should().Be(BudgetTargetType.ByDate);
        result.TargetAmount.Should().Be(30000m);
        result.TargetDueDate.Should().Be(dueDate);
        result.TargetDayOfMonth.Should().BeNull();
    }

    [Fact]
    public async Task Throws_DomainException_when_group_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var foreignGroup = BudgetCategoryGroup.Create(otherFamily.Id, "Foreign", 0);
        fx.Db.BudgetCategoryGroups.Add(foreignGroup);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateCategoryHandler(
            fx.Db, fx.UserProvisioner.Object, new CreateCategoryValidator());

        var act = async () => await sut.Handle(
            new CreateCategoryCommand(
                foreignGroup.Id, "Hacked", null, 0,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Group not found*");
    }
}
