using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class SetAssignedAmountRecordsChangeTests
{
    private const string Bkk = "Asia/Bangkok";

    private static SetAssignedAmountHandler Sut(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(),
            new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

    private static BudgetCategory SeedCategory(HandlerTestFixture fx, string name, int sort = 0)
    {
        var group = fx.Db.BudgetCategoryGroups.FirstOrDefault();
        if (group is null)
        {
            group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
            fx.Db.BudgetCategoryGroups.Add(group);
        }
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, name, null, sort);
        fx.Db.BudgetCategories.Add(cat);
        return cat;
    }

    [Fact]
    public async Task Records_the_delta_from_zero_when_the_assignment_is_new()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 8, 300m, Bkk, null),
            CancellationToken.None);

        var change = fx.Db.BudgetChanges.Single();
        change.Kind.Should().Be(BudgetChangeKind.Assign);
        change.CategoryId.Should().Be(cat.Id);
        change.Delta.Should().Be(300m);
        change.Year.Should().Be(2026);
        change.Month.Should().Be(8);
        change.UserId.Should().Be(fx.User.Id);
    }

    [Fact]
    public async Task Records_the_difference_when_an_assignment_already_exists()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 200m));
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 8, 500m, Bkk, null),
            CancellationToken.None);

        fx.Db.BudgetChanges.Single().Delta.Should().Be(300m);
    }

    [Fact]
    public async Task Records_nothing_when_the_amount_does_not_change()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 200m));
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 8, 200m, Bkk, null),
            CancellationToken.None);

        fx.Db.BudgetChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task Carries_the_batch_id_so_one_quick_assign_press_is_one_row()
    {
        using var fx = new HandlerTestFixture();
        var a = SeedCategory(fx, "Groceries", 0);
        var b = SeedCategory(fx, "Dining", 1);
        await fx.Db.SaveChangesAsync();
        var batch = Guid.NewGuid();

        await Sut(fx).Handle(new SetAssignedAmountCommand(a.Id, 2026, 8, 100m, Bkk, batch), CancellationToken.None);
        await Sut(fx).Handle(new SetAssignedAmountCommand(b.Id, 2026, 8, 200m, Bkk, batch), CancellationToken.None);

        fx.Db.BudgetChanges.Should().HaveCount(2);
        fx.Db.BudgetChanges.Select(c => c.BatchId).Should().AllBeEquivalentTo(batch);
    }
}
