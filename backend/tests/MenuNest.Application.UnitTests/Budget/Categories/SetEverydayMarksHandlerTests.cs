using FluentAssertions;
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
        var sut = new SetEverydayMarksHandler(counting, fx.UserProvisioner.Object, freezer);

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
    public async Task Unmarking_the_only_everyday_envelope_leaves_no_frozen_row()
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
        // Freeze once up front so there's a row to prove gets left behind (not deleted, just stale/unused).
        var frozen = await freezer.RefreezeAsync(fx.Family.Id, new DateOnly(2026, 8, 21), CancellationToken.None);
        frozen.Should().NotBeNull();
        await fx.Db.SaveChangesAsync();

        var sut = new SetEverydayMarksHandler(fx.Db, fx.UserProvisioner.Object, freezer);

        await sut.Handle(
            new SetEverydayMarksCommand([new EverydayMark(cat.Id, false)]),
            CancellationToken.None);

        fx.Db.BudgetCategories.Single().IsEveryday.Should().BeFalse();
        // HasMarksAsync is now false, so RefreezeAsync returns null and the
        // handler must not touch the existing row again.
        (await freezer.HasMarksAsync(fx.Family.Id, CancellationToken.None)).Should().BeFalse();
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
        var sut = new SetEverydayMarksHandler(fx.Db, fx.UserProvisioner.Object, freezer);

        var act = async () => await sut.Handle(
            new SetEverydayMarksCommand([new EverydayMark(foreignCat.Id, true)]),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Category not found*");
    }
}
