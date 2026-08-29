using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class SetAssignedAmountHandlerTests
{
    // The app's one real time zone (menunest-189) — every user is in Thailand.
    private const string Bkk = "Asia/Bangkok";

    [Fact]
    public async Task Creates_new_assignment_row_on_first_call()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, Year: 2026, Month: 4, Amount: 15000m, TimeZoneId: Bkk, BatchId: null),
            CancellationToken.None);

        var persisted = fx.Db.MonthlyAssignments.Single();
        persisted.FamilyId.Should().Be(fx.Family.Id);
        persisted.CategoryId.Should().Be(cat.Id);
        persisted.Year.Should().Be(2026);
        persisted.Month.Should().Be(4);
        persisted.AssignedAmount.Should().Be(15000m);
    }

    [Fact]
    public async Task Updates_existing_assignment_row_on_second_call()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 4, 15000m, Bkk, null),
            CancellationToken.None);
        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 4, 20000m, Bkk, null),
            CancellationToken.None);

        fx.Db.MonthlyAssignments.Should().HaveCount(1);
        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(20000m);
    }

    [Fact]
    public async Task Throws_DomainException_when_category_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var otherGroup = BudgetCategoryGroup.Create(otherFamily.Id, "Foreign", 0);
        fx.Db.BudgetCategoryGroups.Add(otherGroup);
        var foreignCat = BudgetCategory.Create(otherFamily.Id, otherGroup.Id, "Foreign Cat", null, 0);
        fx.Db.BudgetCategories.Add(foreignCat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        var act = async () => await sut.Handle(
            new SetAssignedAmountCommand(foreignCat.Id, 2026, 4, 100m, Bkk, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Category not found*");
    }

    // ── menunest-181: assigning into an everyday envelope is a Budgeting event ──

    [Fact]
    public async Task Assigning_into_an_everyday_envelope_refreezes_the_daily_allowance()
    {
        using var fx = new HandlerTestFixture();
        // The freeze's pot is cumulative "as of today's month" — the fixed
        // clock must be on or after the assigned month, or the assignment
        // below would (correctly) be excluded as not-yet-current.
        fx.Clock.UtcNow = new DateTime(2026, 4, 15, 3, 0, 0, DateTimeKind.Utc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, Year: 2026, Month: 4, Amount: 6000m, TimeZoneId: Bkk, BatchId: null),
            CancellationToken.None);

        fx.Db.DailyAllowances.Should().ContainSingle();
        fx.Db.DailyAllowances.Single().FrozenPot.Should().Be(6000m);
    }

    [Fact]
    public async Task Assigning_into_a_non_everyday_envelope_never_touches_the_daily_allowance()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0); // never marked everyday
        // A DIFFERENT envelope IS marked everyday, so HasMarksAsync is true for the
        // family — this is what forces the assertion to actually exercise the
        // per-category guard rather than piggyback on AllowanceFreezer's own
        // family-wide "nothing marked anywhere" no-op.
        var other = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 1);
        other.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(cat, other);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, Year: 2026, Month: 4, Amount: 20000m, TimeZoneId: Bkk, BatchId: null),
            CancellationToken.None);

        fx.Db.DailyAllowances.Should().BeEmpty("assigning into a non-everyday envelope is not a Budgeting event, even though another envelope in the family is marked");
    }

    // ── menunest-189: the viewer's local day, not the server's UTC day ──

    /// <summary>
    /// 2026-08-31T20:00Z is 2026-09-01T03:00 in Bangkok (UTC+7) — the exact
    /// 00:00–07:00 ICT window the ADR exists for: the server's UTC day (Aug 31)
    /// and the viewer's local day (Sep 1) disagree. If the handler still read
    /// <c>DateTime.UtcNow</c> directly (or converted with the wrong sign), the
    /// freeze would land on Aug 31 / ForMonth=8 and divide by the wrong day
    /// count instead.
    /// </summary>
    [Fact]
    public async Task Assigning_into_an_everyday_envelope_during_the_UTC_lag_window_freezes_on_the_Bangkok_date()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        // Assigns for SEPTEMBER — the viewer's real "this month" at this instant.
        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, Year: 2026, Month: 9, Amount: 3000m, TimeZoneId: Bkk, BatchId: null),
            CancellationToken.None);

        var row = fx.Db.DailyAllowances.Single();
        row.FrozenOn.Should().Be(new DateOnly(2026, 9, 1),
            "the freeze must land on the viewer's Bangkok day, not the server's UTC day (still Aug 31)");
        row.IsForMonth(2026, 9).Should().BeTrue(
            "freezing on the wrong UTC day would freeze for August instead of September");
        // September has 30 days; frozen on the 1st, none are gone yet. A UTC-day
        // freeze (Aug 31, days remaining = 1) would compute a wildly different figure.
        row.Amount.Should().Be(3000m / 30m);
    }

    [Fact]
    public async Task Assigning_into_an_everyday_envelope_with_an_unknown_time_zone_throws_and_freezes_nothing()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        var act = async () => await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 4, 6000m, "Not/A/Real/Zone", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        fx.Db.DailyAllowances.Should().BeEmpty(
            "an unknown zone must reject before any freeze is written, not silently fall back to UTC and freeze anyway");
    }

    [Fact]
    public async Task Assigning_into_a_non_everyday_envelope_ignores_an_invalid_time_zone_because_the_freeze_never_fires()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0); // never marked everyday
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

        // No freeze is needed for a non-everyday envelope, so an invalid zone
        // must not block the write — the zone is resolved only where it's used.
        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 4, 6000m, "Not/A/Real/Zone", null),
            CancellationToken.None);

        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(6000m);
    }
}
