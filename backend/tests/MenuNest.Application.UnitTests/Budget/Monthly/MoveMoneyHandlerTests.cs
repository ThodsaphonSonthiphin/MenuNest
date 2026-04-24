using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class MoveMoneyHandlerTests
{
    [Fact]
    public async Task Decrements_source_and_increments_destination_when_both_assignments_exist()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "Dining", null, 1);
        fx.Db.BudgetCategories.AddRange(from, to);

        var fromA = MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 4, 1000m);
        var toA = MonthlyAssignment.Create(fx.Family.Id, to.Id, 2026, 4, 500m);
        fx.Db.MonthlyAssignments.AddRange(fromA, toA);
        await fx.Db.SaveChangesAsync();

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator());

        await sut.Handle(
            new MoveMoneyCommand(from.Id, to.Id, 2026, 4, 300m),
            CancellationToken.None);

        var reloadedFrom = fx.Db.MonthlyAssignments.Single(a => a.CategoryId == from.Id);
        var reloadedTo = fx.Db.MonthlyAssignments.Single(a => a.CategoryId == to.Id);
        reloadedFrom.AssignedAmount.Should().Be(700m);
        reloadedTo.AssignedAmount.Should().Be(800m);
    }

    [Fact]
    public async Task Creates_missing_assignments_and_balances_the_move()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "Dining", null, 1);
        fx.Db.BudgetCategories.AddRange(from, to);
        await fx.Db.SaveChangesAsync();

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator());

        await sut.Handle(
            new MoveMoneyCommand(from.Id, to.Id, 2026, 4, 200m),
            CancellationToken.None);

        fx.Db.MonthlyAssignments.Should().HaveCount(2);
        var reloadedFrom = fx.Db.MonthlyAssignments.Single(a => a.CategoryId == from.Id);
        var reloadedTo = fx.Db.MonthlyAssignments.Single(a => a.CategoryId == to.Id);
        reloadedFrom.AssignedAmount.Should().Be(-200m);
        reloadedTo.AssignedAmount.Should().Be(200m);
    }

    [Fact]
    public async Task Throws_ValidationException_when_source_equals_destination()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator());

        var act = async () => await sut.Handle(
            new MoveMoneyCommand(cat.Id, cat.Id, 2026, 4, 100m),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Throws_ValidationException_when_amount_is_not_positive()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "A", null, 0);
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "B", null, 1);
        fx.Db.BudgetCategories.AddRange(from, to);
        await fx.Db.SaveChangesAsync();

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator());

        var zeroCall = async () => await sut.Handle(
            new MoveMoneyCommand(from.Id, to.Id, 2026, 4, 0m),
            CancellationToken.None);
        var negativeCall = async () => await sut.Handle(
            new MoveMoneyCommand(from.Id, to.Id, 2026, 4, -5m),
            CancellationToken.None);

        await zeroCall.Should().ThrowAsync<ValidationException>();
        await negativeCall.Should().ThrowAsync<ValidationException>();
    }
}
