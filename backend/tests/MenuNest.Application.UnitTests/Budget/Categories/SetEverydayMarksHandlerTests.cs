using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Categories.SetEverydayMarks;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Categories;

public class SetEverydayMarksHandlerTests
{
    // The single most important positive case in this task: marking N
    // envelopes in one request is ONE Budgeting event, not N. A buggy
    // per-mark loop (save + freeze inside the loop) would converge on the
    // same final row, so only a call-count assertion catches it.
    [Fact]
    public async Task Marking_six_envelopes_in_one_request_saves_and_refreezes_exactly_once()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cats = Enumerable.Range(0, 6)
            .Select(i => BudgetCategory.Create(fx.Family.Id, group.Id, $"Cat{i}", null, i))
            .ToList();
        fx.Db.BudgetCategories.AddRange(cats);
        foreach (var c in cats)
            fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, c.Id, 2026, 8, 1000m));
        await fx.Db.SaveChangesAsync();

        var counting = new SaveChangesCountingDbContext(fx.Db);
        var freezer = new AllowanceFreezer(counting);
        var sut = new SetEverydayMarksHandler(
            counting, fx.UserProvisioner.Object, new SetEverydayMarksValidator(), freezer);

        var marks = cats.Select(c => new EverydayMark(c.Id, true)).ToList();
        await sut.Handle(new SetEverydayMarksCommand(marks), CancellationToken.None);

        // Exactly one save for the marks + one for the freeze — independent of N.
        counting.SaveChangesCallCount.Should().Be(2);

        fx.Db.DailyAllowances.Should().ContainSingle();
        var row = fx.Db.DailyAllowances.Single();
        // The frozen pot must reflect ALL six envelopes — proves the single
        // freeze happened AFTER every mark was applied, not mid-batch.
        row.FrozenPot.Should().Be(6000m);
        foreach (var c in cats)
            fx.Db.BudgetCategories.Single(x => x.Id == c.Id).IsEveryday.Should().BeTrue();
    }

    [Fact]
    public async Task Unmarking_the_only_everyday_envelope_leaves_the_existing_frozen_row_untouched()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 6000m));
        await fx.Db.SaveChangesAsync();

        var freezer = new AllowanceFreezer(fx.Db);
        // A Budgeting event that already happened while the envelope was
        // marked — this row is what proves "nothing marked now" leaves the
        // STORE untouched, not just that HasMarksAsync flips to false.
        var frozen = await freezer.RefreezeAsync(fx.Family.Id, new DateOnly(2026, 8, 21), CancellationToken.None);
        frozen.Should().NotBeNull();
        await fx.Db.SaveChangesAsync();
        var (amountBefore, frozenOnBefore, frozenPotBefore) = (frozen!.Amount, frozen.FrozenOn, frozen.FrozenPot);

        var sut = new SetEverydayMarksHandler(
            fx.Db, fx.UserProvisioner.Object, new SetEverydayMarksValidator(), freezer);

        await sut.Handle(
            new SetEverydayMarksCommand([new EverydayMark(cat.Id, false)]),
            CancellationToken.None);

        fx.Db.BudgetCategories.Single().IsEveryday.Should().BeFalse();
        // HasMarksAsync is now false, so RefreezeAsync returns null and the
        // handler must not touch the existing row again.
        (await freezer.HasMarksAsync(fx.Family.Id, CancellationToken.None)).Should().BeFalse();

        // The stale row is left behind byte-for-byte, not deleted and not
        // refrozen to garbage — RefreezeAsync's null return means "store
        // nothing", which must translate to "the existing row is untouched".
        fx.Db.DailyAllowances.Should().ContainSingle("the row is left behind, not deleted");
        var row = fx.Db.DailyAllowances.Single();
        row.Amount.Should().Be(amountBefore);
        row.FrozenOn.Should().Be(frozenOnBefore);
        row.FrozenPot.Should().Be(frozenPotBefore);
    }

    [Fact]
    public async Task Unmarking_one_of_two_everyday_envelopes_refreezes_with_the_reduced_pot()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var keep = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        keep.MarkEveryday(true);
        var drop = BudgetCategory.Create(fx.Family.Id, group.Id, "Dining Out", null, 1);
        drop.MarkEveryday(true);
        fx.Db.BudgetCategories.AddRange(keep, drop);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, keep.Id, 2026, 8, 4000m));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, drop.Id, 2026, 8, 2000m));
        await fx.Db.SaveChangesAsync();

        var freezer = new AllowanceFreezer(fx.Db);
        var frozen = await freezer.RefreezeAsync(fx.Family.Id, new DateOnly(2026, 8, 21), CancellationToken.None);
        frozen!.FrozenPot.Should().Be(6000m, "both envelopes count before the drop");
        await fx.Db.SaveChangesAsync();

        var sut = new SetEverydayMarksHandler(
            fx.Db, fx.UserProvisioner.Object, new SetEverydayMarksValidator(), freezer);

        await sut.Handle(
            new SetEverydayMarksCommand([new EverydayMark(drop.Id, false)]),
            CancellationToken.None);

        fx.Db.BudgetCategories.Single(c => c.Id == drop.Id).IsEveryday.Should().BeFalse();
        fx.Db.BudgetCategories.Single(c => c.Id == keep.Id).IsEveryday.Should().BeTrue();
        fx.Db.DailyAllowances.Should().ContainSingle();
        fx.Db.DailyAllowances.Single().FrozenPot.Should().Be(4000m,
            "dropping one envelope's everyday mark removes its money from the pot, leaving only the kept one's");
    }

    // menunest-184's other central negative case: a request that changes NO
    // envelope's mark is not a Budgeting event — it must not reset FrozenOn or
    // re-divide the pot. This is exactly what the SPA does re-posting the
    // whole sheet unchanged (open + close with no edits).
    [Fact]
    public async Task A_request_that_changes_no_marks_leaves_the_frozen_allowance_untouched()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 6000m));
        await fx.Db.SaveChangesAsync();

        var freezer = new AllowanceFreezer(fx.Db);
        var frozen = await freezer.RefreezeAsync(fx.Family.Id, new DateOnly(2026, 8, 21), CancellationToken.None);
        frozen.Should().NotBeNull();
        await fx.Db.SaveChangesAsync();
        var (amountBefore, frozenOnBefore, frozenPotBefore) = (frozen!.Amount, frozen.FrozenOn, frozen.FrozenPot);

        // Assign MORE money after the freeze, so if the handler wrongly
        // re-freezes, the pot (and therefore Amount) visibly changes — a
        // silent "same values" no-op refreeze would otherwise hide the bug.
        fx.Db.MonthlyAssignments.Single().SetAmount(9000m);
        await fx.Db.SaveChangesAsync();

        var sut = new SetEverydayMarksHandler(
            fx.Db, fx.UserProvisioner.Object, new SetEverydayMarksValidator(), freezer);

        // Re-submits the SAME mark the category already has — nothing changes.
        await sut.Handle(
            new SetEverydayMarksCommand([new EverydayMark(cat.Id, true)]),
            CancellationToken.None);

        var row = fx.Db.DailyAllowances.Single();
        row.Amount.Should().Be(amountBefore, "a no-op mark request must not re-divide the pot");
        row.FrozenOn.Should().Be(frozenOnBefore, "a no-op mark request must not reset the freeze date");
        row.FrozenPot.Should().Be(frozenPotBefore, "a no-op mark request must not pick up the later assignment");
    }

    [Fact]
    public async Task Throws_DomainException_when_a_category_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();
        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var otherGroup = BudgetCategoryGroup.Create(otherFamily.Id, "Foreign", 0);
        fx.Db.BudgetCategoryGroups.Add(otherGroup);
        var foreignCat = BudgetCategory.Create(otherFamily.Id, otherGroup.Id, "Foreign Cat", null, 0);
        fx.Db.BudgetCategories.Add(foreignCat);
        await fx.Db.SaveChangesAsync();

        var freezer = new AllowanceFreezer(fx.Db);
        var sut = new SetEverydayMarksHandler(
            fx.Db, fx.UserProvisioner.Object, new SetEverydayMarksValidator(), freezer);

        var act = async () => await sut.Handle(
            new SetEverydayMarksCommand([new EverydayMark(foreignCat.Id, true)]),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Category not found*");
    }

    [Fact]
    public async Task Throws_ValidationException_when_Marks_is_empty()
    {
        using var fx = new HandlerTestFixture();
        var freezer = new AllowanceFreezer(fx.Db);
        var sut = new SetEverydayMarksHandler(
            fx.Db, fx.UserProvisioner.Object, new SetEverydayMarksValidator(), freezer);

        var act = async () => await sut.Handle(
            new SetEverydayMarksCommand([]), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Throws_ValidationException_when_a_mark_has_an_empty_category_id()
    {
        using var fx = new HandlerTestFixture();
        var freezer = new AllowanceFreezer(fx.Db);
        var sut = new SetEverydayMarksHandler(
            fx.Db, fx.UserProvisioner.Object, new SetEverydayMarksValidator(), freezer);

        var act = async () => await sut.Handle(
            new SetEverydayMarksCommand([new EverydayMark(Guid.Empty, true)]), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
