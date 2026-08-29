using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.History;

public class BudgetChangeApplierTests
{
    private static BudgetCategory SeedCategory(HandlerTestFixture fx, string name, int sort = 0)
    {
        var group = fx.Db.BudgetCategoryGroups.FirstOrDefault()
                    ?? BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        if (!fx.Db.BudgetCategoryGroups.Any()) fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, name, null, sort);
        fx.Db.BudgetCategories.Add(cat);
        return cat;
    }

    [Fact]
    public async Task Undoing_an_assign_subtracts_the_delta_and_leaves_a_concurrent_change_standing()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");

        // The user assigned 300; then somebody else added 100 on top.
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 400m));
        var change = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        await new BudgetChangeApplier(fx.Db).ApplyAsync(change, -1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        // 400 - 300 = 100. A rollback to "0" would have destroyed the other 100.
        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(100m);
    }

    [Fact]
    public async Task Undoing_a_move_returns_the_money_to_the_source()
    {
        using var fx = new HandlerTestFixture();
        var from = SeedCategory(fx, "Groceries", 0);
        var to = SeedCategory(fx, "Dining", 1);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 8, 700m));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, to.Id, 2026, 8, 800m));
        var change = BudgetChange.RecordMove(fx.Family.Id, fx.User.Id, 2026, 8, from.Id, to.Id, 300m, false);
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        await new BudgetChangeApplier(fx.Db).ApplyAsync(change, -1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == from.Id).AssignedAmount.Should().Be(1000m);
        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == to.Id).AssignedAmount.Should().Be(500m);
    }

    [Fact]
    public async Task Redoing_re_applies_the_same_delta_forward()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 100m));
        var change = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        await new BudgetChangeApplier(fx.Db).ApplyAsync(change, +1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(400m);
    }

    [Fact]
    public async Task Undoing_an_everyday_mark_flips_it_back()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");
        cat.MarkEveryday(true);
        var change = BudgetChange.RecordEverydayMark(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, true);
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        await new BudgetChangeApplier(fx.Db).ApplyAsync(change, -1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        fx.Db.BudgetCategories.Single().IsEveryday.Should().BeFalse();
    }

    [Fact]
    public async Task Creates_the_assignment_row_when_it_no_longer_exists()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");
        var change = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        await new BudgetChangeApplier(fx.Db).ApplyAsync(change, -1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(-300m);
    }
}
