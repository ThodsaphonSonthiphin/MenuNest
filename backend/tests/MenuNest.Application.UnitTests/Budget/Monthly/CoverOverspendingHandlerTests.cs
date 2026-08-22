using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class CoverOverspendingHandlerTests
{
    // The app's one real time zone (menunest-189) — every user is in Thailand.
    private const string Bkk = "Asia/Bangkok";

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
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        // Use CoverOverspendingCommand explicitly — this assertion proves
        // the command is wired to the CoverOverspending handler (not MoveMoney).
        var cmd = new CoverOverspendingCommand(
            OverspentCategoryId: overspent.Id,
            FromCategoryId: from.Id,
            Year: 2026, Month: 4, Amount: 150m, TimeZoneId: Bkk);
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
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        var act = async () => await sut.Handle(
            new CoverOverspendingCommand(cat.Id, cat.Id, 2026, 4, 100m, Bkk),
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
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        var zeroCall = async () => await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 4, 0m, Bkk),
            CancellationToken.None);

        await zeroCall.Should().ThrowAsync<ValidationException>();
    }

    // ── menunest-181: only re-freeze when an everyday envelope is involved ──

    [Fact]
    public async Task Covering_an_overspent_everyday_envelope_refreezes_the_daily_allowance()
    {
        using var fx = new HandlerTestFixture();
        // The freeze's pot is cumulative "as of today's month" — the fixed
        // clock must be on or after the assigned month, or the assignment
        // below would (correctly) be excluded as not-yet-current.
        fx.Clock.UtcNow = new DateTime(2026, 4, 15, 3, 0, 0, DateTimeKind.Utc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Savings", null, 0); // not everyday
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        overspent.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(from, overspent);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 4, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new CoverOverspendingHandler(
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 4, 150m, Bkk), CancellationToken.None);

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
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 4, 150m, Bkk), CancellationToken.None);

        fx.Db.DailyAllowances.Should().BeEmpty("neither envelope involved is marked everyday, even though another envelope in the family is");
    }

    // ── menunest-189: the viewer's local day, not the server's UTC day ──

    /// <summary>
    /// Same UTC-lag boundary as SetAssignedAmountHandlerTests/MoveMoneyHandlerTests
    /// — pinned here too because each of the three Budgeting-event handlers
    /// re-freezes on its own copy of "today"; a fix applied to one and forgotten
    /// on another would only be caught by a test at that specific site.
    /// </summary>
    [Fact]
    public async Task Covering_an_overspent_everyday_envelope_during_the_UTC_lag_window_freezes_on_the_Bangkok_date()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Savings", null, 0);
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        overspent.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(from, overspent);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 9, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new CoverOverspendingHandler(
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 9, 150m, Bkk), CancellationToken.None);

        var row = fx.Db.DailyAllowances.Single();
        row.FrozenOn.Should().Be(new DateOnly(2026, 9, 1),
            "the freeze must land on the viewer's Bangkok day, not the server's UTC day (still Aug 31)");
        row.IsForMonth(2026, 9).Should().BeTrue();
    }

    [Fact]
    public async Task Covering_an_overspent_everyday_envelope_with_an_unknown_time_zone_throws_and_freezes_nothing()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var from = BudgetCategory.Create(fx.Family.Id, group.Id, "Savings", null, 0);
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        overspent.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(from, overspent);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, from.Id, 2026, 4, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new CoverOverspendingHandler(
            fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(), new AllowanceFreezer(fx.Db), fx.Clock);

        var act = async () => await sut.Handle(
            new CoverOverspendingCommand(overspent.Id, from.Id, 2026, 4, 150m, "Not/A/Real/Zone"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        fx.Db.DailyAllowances.Should().BeEmpty();
    }
}
