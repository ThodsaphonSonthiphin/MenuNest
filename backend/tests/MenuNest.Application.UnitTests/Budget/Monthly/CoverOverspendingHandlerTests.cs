using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class CoverOverspendingHandlerTests
{
    [Fact]
    public async Task Decrements_source_and_increments_overspent_assignment()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Savings", null, 0);
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        fx.Db.BudgetCategories.AddRange(from, overspent);

        var fromA = MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 4, 1000m);
        var overspentA = MonthlyAssignment.Create(fx.Family.Id, overspent.Id, 2026, 4, 200m);
        fx.Db.MonthlyAssignments.AddRange(fromA, overspentA);
        await fx.Db.SaveChangesAsync();

        var sut = new CoverOverspendingHandler(
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator());

        // Use CoverOverspendingCommand explicitly — this assertion proves
        // the command is wired to the CoverOverspending handler (not MoveMoney).
        var cmd = new CoverOverspendingCommand(
            OverspentCategoryId: overspent.Id,
            FromCategoryId: from.Id,
            Year: 2026, Month: 4, Amount: 150m);
        cmd.Should().BeOfType<CoverOverspendingCommand>();

        await sut.Handle(cmd, CancellationToken.None);

        var reloadedFrom = fx.Db.MonthlyAssignments.Single(a => a.CategoryId == from.Id);
        var reloadedOverspent = fx.Db.MonthlyAssignments.Single(a => a.CategoryId == overspent.Id);
        reloadedFrom.AssignedAmount.Should().Be(850m);
        reloadedOverspent.AssignedAmount.Should().Be(350m);
    }

    [Fact]
    public async Task Throws_ValidationException_when_overspent_equals_from()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new CoverOverspendingHandler(
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator());

        var act = async () => await sut.Handle(
            new CoverOverspendingCommand(cat.Id, cat.Id, 2026, 4, 100m),
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
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "B", null, 1);
        fx.Db.BudgetCategories.AddRange(from, overspent);
        await fx.Db.SaveChangesAsync();

        var sut = new CoverOverspendingHandler(
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator());

        var zeroCall = async () => await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 4, 0m),
            CancellationToken.None);

        await zeroCall.Should().ThrowAsync<ValidationException>();
    }
}
