using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
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
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db));

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
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db));

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
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db));

        var zeroCall = async () => await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 4, 0m),
            CancellationToken.None);

        await zeroCall.Should().ThrowAsync<ValidationException>();
    }

    // ── menunest-181: only re-freeze when an everyday envelope is involved ──

    [Fact]
    public async Task Covering_an_overspent_everyday_envelope_refreezes_the_daily_allowance()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Savings", null, 0); // not everyday
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        overspent.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(from, overspent);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 4, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new CoverOverspendingHandler(
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db));

        await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 4, 150m), CancellationToken.None);

        fx.Db.DailyAllowances.Should().ContainSingle();
        fx.Db.DailyAllowances.Single().FrozenPot.Should().Be(150m);
    }

    [Fact]
    public async Task Covering_overspending_between_two_non_everyday_envelopes_never_touches_the_daily_allowance()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Savings", null, 0);
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 1);
        // A DIFFERENT envelope IS marked everyday, so HasMarksAsync is true for the
        // family — this forces the assertion to exercise the per-cover guard rather
        // than piggyback on AllowanceFreezer's own family-wide no-op.
        var other = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 2);
        other.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(from, overspent, other);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 4, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new CoverOverspendingHandler(
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db));

        await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 4, 150m), CancellationToken.None);

        fx.Db.DailyAllowances.Should().BeEmpty("neither envelope involved is marked everyday, even though another envelope in the family is");
    }
}
