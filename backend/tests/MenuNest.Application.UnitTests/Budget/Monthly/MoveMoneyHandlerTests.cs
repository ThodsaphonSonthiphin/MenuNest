using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class MoveMoneyHandlerTests
{
    // The app's one real time zone (menunest-189) — every user is in Thailand.
    private const string Bkk = "Asia/Bangkok";

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
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        await sut.Handle(
            new MoveMoneyCommand(from.Id, to.Id, 2026, 4, 300m, Bkk),
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
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        await sut.Handle(
            new MoveMoneyCommand(from.Id, to.Id, 2026, 4, 200m, Bkk),
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
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        var act = async () => await sut.Handle(
            new MoveMoneyCommand(cat.Id, cat.Id, 2026, 4, 100m, Bkk),
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
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        var zeroCall = async () => await sut.Handle(
            new MoveMoneyCommand(from.Id, to.Id, 2026, 4, 0m, Bkk),
            CancellationToken.None);
        var negativeCall = async () => await sut.Handle(
            new MoveMoneyCommand(from.Id, to.Id, 2026, 4, -5m, Bkk),
            CancellationToken.None);

        await zeroCall.Should().ThrowAsync<ValidationException>();
        await negativeCall.Should().ThrowAsync<ValidationException>();
    }

    // ── menunest-181: only re-freeze when an everyday envelope is involved ──

    [Fact]
    public async Task Moving_money_into_an_everyday_envelope_refreezes_the_daily_allowance()
    {
        using var fx = new HandlerTestFixture();
        // The freeze's pot is cumulative "as of today's month" — the fixed
        // clock must be on or after the assigned month, or the assignment
        // below would (correctly) be excluded as not-yet-current.
        fx.Clock.UtcNow = new DateTime(2026, 4, 15, 3, 0, 0, DateTimeKind.Utc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Savings", null, 0); // not everyday
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        to.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(from, to);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 4, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        await sut.Handle(new MoveMoneyCommand(from.Id, to.Id, 2026, 4, 300m, Bkk), CancellationToken.None);

        fx.Db.DailyAllowances.Should().ContainSingle();
        fx.Db.DailyAllowances.Single().FrozenPot.Should().Be(300m);
    }

    [Fact]
    public async Task Moving_money_between_two_non_everyday_envelopes_never_touches_the_daily_allowance()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "Utilities", null, 1);
        // A DIFFERENT envelope IS marked everyday, so HasMarksAsync is true for the
        // family — this forces the assertion to exercise the per-move guard rather
        // than piggyback on AllowanceFreezer's own family-wide no-op.
        var other = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 2);
        other.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(from, to, other);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 4, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        await sut.Handle(new MoveMoneyCommand(from.Id, to.Id, 2026, 4, 300m, Bkk), CancellationToken.None);

        fx.Db.DailyAllowances.Should().BeEmpty("neither envelope in the move is marked everyday, even though another envelope in the family is");
    }

    // ── menunest-189: the viewer's local day, not the server's UTC day ──

    /// <summary>
    /// Same UTC-lag boundary as SetAssignedAmountHandlerTests — pinned here too
    /// because each of the three Budgeting-event handlers re-freezes on its own
    /// copy of "today"; a fix applied to one and forgotten on another would only
    /// be caught by a test at that specific site.
    /// </summary>
    [Fact]
    public async Task Moving_money_into_an_everyday_envelope_during_the_UTC_lag_window_freezes_on_the_Bangkok_date()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Savings", null, 0);
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        to.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(from, to);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 9, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        await sut.Handle(new MoveMoneyCommand(from.Id, to.Id, 2026, 9, 300m, Bkk), CancellationToken.None);

        var row = fx.Db.DailyAllowances.Single();
        row.FrozenOn.Should().Be(new DateOnly(2026, 9, 1),
            "the freeze must land on the viewer's Bangkok day, not the server's UTC day (still Aug 31)");
        row.IsForMonth(2026, 9).Should().BeTrue();
    }

    [Fact]
    public async Task Moving_money_into_an_everyday_envelope_with_an_unknown_time_zone_throws_and_freezes_nothing()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Savings", null, 0);
        var to = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        to.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(from, to);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 4, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new MoveMoneyHandler(
            fx.Db, fx.UserProvisioner.Object, new MoveMoneyValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        var act = async () => await sut.Handle(
            new MoveMoneyCommand(from.Id, to.Id, 2026, 4, 300m, "Not/A/Real/Zone"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        fx.Db.DailyAllowances.Should().BeEmpty();
    }
}
