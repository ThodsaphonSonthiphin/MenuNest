using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Categories.UpdateCategory;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Categories;

public class UpdateCategoryHandlerTests
{
    [Fact]
    public async Task Updates_name_emoji_and_sort_order()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", "🏠", 1);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateCategoryHandler(
            fx.Db, fx.UserProvisioner.Object, new UpdateCategoryValidator());

        var result = await sut.Handle(
            new UpdateCategoryCommand(
                cat.Id, group.Id, "Mortgage", "🏡", 5,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        result.Name.Should().Be("Mortgage");
        result.Emoji.Should().Be("🏡");
        result.SortOrder.Should().Be(5);

        var reloaded = fx.Db.BudgetCategories.Single(c => c.Id == cat.Id);
        reloaded.Name.Should().Be("Mortgage");
        reloaded.Emoji.Should().Be("🏡");
        reloaded.SortOrder.Should().Be(5);
    }

    [Fact]
    public async Task Switches_from_by_date_to_monthly_amount_and_nulls_the_other_fields()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Goals", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Vacation", "🌴", 0);
        cat.SetByDateTarget(30000m, new DateOnly(2026, 12, 31));
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateCategoryHandler(
            fx.Db, fx.UserProvisioner.Object, new UpdateCategoryValidator());

        var result = await sut.Handle(
            new UpdateCategoryCommand(
                cat.Id, group.Id, "Vacation", "🌴", 0,
                BudgetTargetType.MonthlyAmount, TargetAmount: 2500m,
                TargetDueDate: null, TargetDayOfMonth: 15),
            CancellationToken.None);

        result.TargetType.Should().Be(BudgetTargetType.MonthlyAmount);
        result.TargetAmount.Should().Be(2500m);
        result.TargetDayOfMonth.Should().Be(15);
        result.TargetDueDate.Should().BeNull();
    }

    [Fact]
    public async Task Clears_target_when_target_type_is_none()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        cat.SetMonthlyTarget(15000m, 1);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateCategoryHandler(
            fx.Db, fx.UserProvisioner.Object, new UpdateCategoryValidator());

        var result = await sut.Handle(
            new UpdateCategoryCommand(
                cat.Id, group.Id, "Rent", null, 0,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        result.TargetType.Should().Be(BudgetTargetType.None);
        result.TargetAmount.Should().BeNull();
        result.TargetDueDate.Should().BeNull();
        result.TargetDayOfMonth.Should().BeNull();
    }
}
