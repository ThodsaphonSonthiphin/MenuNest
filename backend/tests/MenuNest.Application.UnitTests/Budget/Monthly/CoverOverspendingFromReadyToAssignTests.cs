using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

/// <summary>
/// menunest-215 / issue #115 — covering an overspend from Ready to Assign, the
/// money not yet placed in any envelope. Ready to Assign is DERIVED
/// (<c>sum(accounts) − sum(envelope.available)</c>), so it owns no
/// <see cref="MonthlyAssignment"/> row: the cover is a one-sided increment of
/// the overspent envelope, and the derived figure falls by exactly that.
/// </summary>
public class CoverOverspendingFromReadyToAssignTests
{
    private const string Bkk = "Asia/Bangkok";

    private static CoverOverspendingHandler Sut(HandlerTestFixture fx) => new(
        fx.Db, fx.UserProvisioner.Object, new CoverOverspendingValidator(),
        new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

    /// <summary>The state in #115's screenshot: ค่าซักผ้า overspent by ฿110.</summary>
    private static (BudgetCategory overspent, BudgetCategory other) Seed(HandlerTestFixture fx)
    {
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "ค่าซักผ้า", "🧺", 0);
        var other = BudgetCategory.Create(fx.Family.Id, group.Id, "อาหาร", "🍜", 1);
        fx.Db.BudgetCategories.AddRange(overspent, other);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, overspent.Id, 2026, 8, 0m));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, other.Id, 2026, 8, 240m));
        fx.Db.SaveChanges();
        return (overspent, other);
    }

    [Fact]
    public async Task Increments_the_overspent_envelope_and_leaves_every_other_envelope_untouched()
    {
        using var fx = new HandlerTestFixture();
        var (overspent, other) = Seed(fx);

        await Sut(fx).Handle(
            new CoverOverspendingCommand(overspent.Id, FromCategoryId: null, 2026, 8, 110m, Bkk),
            CancellationToken.None);

        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == overspent.Id)
            .AssignedAmount.Should().Be(110m);
        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == other.Id)
            .AssignedAmount.Should().Be(240m,
                "covering from Ready to Assign takes nothing out of any envelope");
    }

    [Fact]
    public async Task Creates_the_assignment_row_when_the_overspent_envelope_has_none_this_month()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        // Overspent purely by activity — never assigned, so no row exists yet.
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "ค่าซักผ้า", "🧺", 0);
        fx.Db.BudgetCategories.Add(overspent);
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new CoverOverspendingCommand(overspent.Id, FromCategoryId: null, 2026, 8, 110m, Bkk),
            CancellationToken.None);

        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == overspent.Id)
            .AssignedAmount.Should().Be(110m);
    }

    [Fact]
    public async Task Records_an_Assign_carrying_the_positive_delta_and_no_second_envelope()
    {
        using var fx = new HandlerTestFixture();
        var (overspent, _) = Seed(fx);

        await Sut(fx).Handle(
            new CoverOverspendingCommand(overspent.Id, FromCategoryId: null, 2026, 8, 110m, Bkk),
            CancellationToken.None);

        var change = fx.Db.BudgetChanges.Single();
        // Not Cover: a Cover row means "this envelope gave, that one received",
        // and BudgetChangeApplier refuses one whose destination is null. There
        // is no giving envelope here — the act IS an assign.
        change.Kind.Should().Be(BudgetChangeKind.Assign);
        change.CategoryId.Should().Be(overspent.Id);
        change.SecondCategoryId.Should().BeNull();
        change.Delta.Should().Be(110m);
        change.BatchId.Should().BeNull();
        change.UserId.Should().Be(fx.User.Id);
    }

    [Fact]
    public async Task The_recorded_change_undoes_and_redoes_through_the_existing_applier()
    {
        using var fx = new HandlerTestFixture();
        var (overspent, _) = Seed(fx);

        await Sut(fx).Handle(
            new CoverOverspendingCommand(overspent.Id, FromCategoryId: null, 2026, 8, 110m, Bkk),
            CancellationToken.None);

        var applier = new BudgetChangeApplier(fx.Db);
        var change = fx.Db.BudgetChanges.Single();

        await applier.ApplyAsync(change, direction: -1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();
        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == overspent.Id)
            .AssignedAmount.Should().Be(0m, "undo applies the opposite delta");

        await applier.ApplyAsync(change, direction: +1, CancellationToken.None);
        await fx.Db.SaveChangesAsync();
        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == overspent.Id)
            .AssignedAmount.Should().Be(110m, "redo is the same arithmetic, sign flipped");
    }

    /// <summary>
    /// menunest-193: the write is a DELTA, so a concurrent assign by another
    /// Family member survives it. The frontend alternative — reusing
    /// SetAssignedAmount with a client-computed <c>assigned + amount</c> — would
    /// write an ABSOLUTE figure from a stale summary and destroy that member's
    /// work. This test is what pins the choice.
    /// </summary>
    [Fact]
    public async Task A_concurrent_assign_by_another_member_survives_the_cover()
    {
        using var fx = new HandlerTestFixture();
        var (overspent, _) = Seed(fx);

        // Another member assigns ฿500 after this viewer's summary was rendered
        // (which still shows assigned = 0).
        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == overspent.Id).AdjustAmount(500m);
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new CoverOverspendingCommand(overspent.Id, FromCategoryId: null, 2026, 8, 110m, Bkk),
            CancellationToken.None);

        fx.Db.MonthlyAssignments.Single(a => a.CategoryId == overspent.Id)
            .AssignedAmount.Should().Be(610m, "both writes land; neither clobbers the other");
    }

    [Fact]
    public async Task Throws_ValidationException_when_the_source_is_an_empty_Guid_rather_than_null()
    {
        using var fx = new HandlerTestFixture();
        var (overspent, _) = Seed(fx);

        // An empty Guid is a caller that meant to name an envelope and sent
        // nothing — never a deliberate "from Ready to Assign", which is null.
        var act = async () => await Sut(fx).Handle(
            new CoverOverspendingCommand(overspent.Id, Guid.Empty, 2026, 8, 110m, Bkk),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        fx.Db.BudgetChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task Throws_ValidationException_when_the_amount_is_not_positive()
    {
        using var fx = new HandlerTestFixture();
        var (overspent, _) = Seed(fx);

        var act = async () => await Sut(fx).Handle(
            new CoverOverspendingCommand(overspent.Id, FromCategoryId: null, 2026, 8, 0m, Bkk),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ── menunest-181/189: the everyday guard, with no source envelope ──

    [Fact]
    public async Task Covering_an_overspent_everyday_envelope_from_Ready_to_Assign_refreezes_the_allowance()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 15, 3, 0, 0, DateTimeKind.Utc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        overspent.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(overspent);
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new CoverOverspendingCommand(overspent.Id, FromCategoryId: null, 2026, 8, 150m, Bkk),
            CancellationToken.None);

        fx.Db.DailyAllowances.Should().ContainSingle();
        fx.Db.DailyAllowances.Single().FrozenPot.Should().Be(150m);
    }

    [Fact]
    public async Task Covering_a_non_everyday_envelope_from_Ready_to_Assign_never_touches_the_allowance()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var overspent = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        // A DIFFERENT envelope is marked everyday, so the family-wide no-op in
        // AllowanceFreezer cannot be what makes this pass — the per-cover guard
        // must, with a null source that matches no row.
        var other = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        other.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(overspent, other);
        await fx.Db.SaveChangesAsync();

        await Sut(fx).Handle(
            new CoverOverspendingCommand(overspent.Id, FromCategoryId: null, 2026, 8, 150m, Bkk),
            CancellationToken.None);

        fx.Db.DailyAllowances.Should().BeEmpty(
            "the covered envelope is not everyday, and a null source can never match one");
    }
}
