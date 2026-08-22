using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class SetAssignedAmountHandlerTests
{
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
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db));

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, Year: 2026, Month: 4, Amount: 15000m),
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
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db));

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 4, 15000m),
            CancellationToken.None);
        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, 2026, 4, 20000m),
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
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db));

        var act = async () => await sut.Handle(
            new SetAssignedAmountCommand(foreignCat.Id, 2026, 4, 100m),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Category not found*");
    }

    // ── menunest-181: assigning into an everyday envelope is a Budgeting event ──

    [Fact]
    public async Task Assigning_into_an_everyday_envelope_refreezes_the_daily_allowance()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new SetAssignedAmountHandler(
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db));

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, Year: 2026, Month: 4, Amount: 6000m),
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
            fx.Db, fx.UserProvisioner.Object, new SetAssignedAmountValidator(), new AllowanceFreezer(fx.Db));

        await sut.Handle(
            new SetAssignedAmountCommand(cat.Id, Year: 2026, Month: 4, Amount: 20000m),
            CancellationToken.None);

        fx.Db.DailyAllowances.Should().BeEmpty("assigning into a non-everyday envelope is not a Budgeting event, even though another envelope in the family is marked");
    }
}
