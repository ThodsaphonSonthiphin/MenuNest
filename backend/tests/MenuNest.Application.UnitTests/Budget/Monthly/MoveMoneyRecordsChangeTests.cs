using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;
using MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class MoveMoneyRecordsChangeTests
{
    private const string Bkk = "Asia/Bangkok";

    private static (BudgetCategory from, BudgetCategory to) Seed(
        HandlerTestFixture fx, decimal fromAmount, decimal toAmount)
    {
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "Dining", null, 1);
        fx.Db.BudgetCategories.AddRange(from, to);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 8, fromAmount));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, to.Id, 2026, 8, toAmount));
        fx.Db.SaveChanges();
        return (from, to);
    }

    [Fact]
    public async Task Move_records_one_row_holding_the_source_as_a_negative_delta()
    {
        using var fx = new HandlerTestFixture();
        var (from, to) = Seed(fx, 1000m, 500m);

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(),
            new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        await sut.Handle(new MoveMoneyCommand(from.Id, to.Id, 2026, 8, 300m, Bkk), CancellationToken.None);

        var change = fx.Db.BudgetChanges.Single();
        change.Kind.Should().Be(BudgetChangeKind.Move);
        change.CategoryId.Should().Be(from.Id);
        change.SecondCategoryId.Should().Be(to.Id);
        change.Delta.Should().Be(-300m);
        change.UserId.Should().Be(fx.User.Id);
    }

    [Fact]
    public async Task Cover_records_the_same_shape_but_marked_as_Cover()
    {
        using var fx = new HandlerTestFixture();
        var (from, overspent) = Seed(fx, 1000m, -200m);

        var sut = new CoverOverspendingHandler(
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(),
            new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 8, 200m, Bkk),
            CancellationToken.None);

        var change = fx.Db.BudgetChanges.Single();
        change.Kind.Should().Be(BudgetChangeKind.Cover);
        change.CategoryId.Should().Be(from.Id);
        change.SecondCategoryId.Should().Be(overspent.Id);
        change.Delta.Should().Be(-200m);
    }
}
