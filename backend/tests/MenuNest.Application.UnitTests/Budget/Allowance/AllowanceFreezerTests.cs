using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Allowance;

public class AllowanceFreezerTests
{
    private static BudgetCategory SeedEverydayCategory(HandlerTestFixture fx, string name = "Groceries", int sortOrder = 0)
    {
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, name, null, sortOrder);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        return cat;
    }

    // (a) No envelope marked → null, nothing stored.
    [Fact]
    public async Task RefreezeAsync_returns_null_and_stores_nothing_when_no_envelope_is_marked()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0); // NOT marked everyday
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 10000m));
        await fx.Db.SaveChangesAsync();

        var sut = new AllowanceFreezer(fx.Db);

        var result = await sut.RefreezeAsync(fx.Family.Id, new DateOnly(2026, 8, 21), CancellationToken.None);

        result.Should().BeNull();
        fx.Db.DailyAllowances.Should().BeEmpty();
    }

    // (b) One marked envelope holding 6,000 on the seeded worked-example date.
    [Fact]
    public async Task RefreezeAsync_freezes_the_pot_of_the_marked_envelope()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedEverydayCategory(fx);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 6000m));
        await fx.Db.SaveChangesAsync();

        var sut = new AllowanceFreezer(fx.Db);

        var result = await sut.RefreezeAsync(fx.Family.Id, new DateOnly(2026, 8, 21), CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        result.Should().NotBeNull();
        result!.FrozenPot.Should().Be(6000m);
        result.Amount.Should().BeApproximately(545.4545m, 0.0001m); // menunest-181's worked example: 6000 / 11 days
        result.ForYear.Should().Be(2026);
        result.ForMonth.Should().Be(8);
        fx.Db.DailyAllowances.Should().ContainSingle();
    }

    // (c) A second RefreezeAsync overwrites — never a second row.
    [Fact]
    public async Task RefreezeAsync_overwrites_the_existing_row_instead_of_inserting_a_second_one()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedEverydayCategory(fx);
        var assignment = MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 6000m);
        fx.Db.MonthlyAssignments.Add(assignment);
        await fx.Db.SaveChangesAsync();

        var sut = new AllowanceFreezer(fx.Db);
        var first = await sut.RefreezeAsync(fx.Family.Id, new DateOnly(2026, 8, 21), CancellationToken.None);
        await fx.Db.SaveChangesAsync();
        first!.FrozenPot.Should().Be(6000m);

        assignment.SetAmount(9000m);
        await fx.Db.SaveChangesAsync(); // the assignment change must land before the next freeze reads it
        var second = await sut.RefreezeAsync(fx.Family.Id, new DateOnly(2026, 8, 23), CancellationToken.None);
        await fx.Db.SaveChangesAsync();

        fx.Db.DailyAllowances.Should().HaveCount(1);
        second!.FrozenPot.Should().Be(9000m);
        second.FrozenOn.Should().Be(new DateOnly(2026, 8, 23));
    }

    // (d) Money in an unmarked envelope is excluded from the pot.
    [Fact]
    public async Task CurrentPotAsync_excludes_money_in_an_envelope_that_is_not_marked_everyday()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Mixed", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var everyday = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        everyday.MarkEveryday(true);
        var notEveryday = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 1); // left unmarked
        fx.Db.BudgetCategories.AddRange(everyday, notEveryday);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, everyday.Id, 2026, 8, 1000m));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, notEveryday.Id, 2026, 8, 20000m));
        await fx.Db.SaveChangesAsync();

        var sut = new AllowanceFreezer(fx.Db);

        var pot = await sut.CurrentPotAsync(fx.Family.Id, new DateOnly(2026, 8, 21), CancellationToken.None);

        pot.Should().Be(1000m, "the 20,000 sitting in Rent must never enter the everyday pot");
    }

    // Rounds out (b)/(d): CurrentPotAsync must net signed activity against the
    // assignment, not just sum assignments — otherwise a spend would never move
    // the frozen figure at the next Budgeting event.
    [Fact]
    public async Task CurrentPotAsync_nets_signed_activity_against_the_assignment()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedEverydayCategory(fx);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 6000m));
        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, cat.Id, -500m, new DateOnly(2026, 8, 10), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new AllowanceFreezer(fx.Db);

        var pot = await sut.CurrentPotAsync(fx.Family.Id, new DateOnly(2026, 8, 21), CancellationToken.None);

        pot.Should().Be(5500m);
    }

    [Fact]
    public async Task HasMarksAsync_is_false_until_an_envelope_is_marked_everyday()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new AllowanceFreezer(fx.Db);
        (await sut.HasMarksAsync(fx.Family.Id, CancellationToken.None)).Should().BeFalse();

        cat.MarkEveryday(true);
        await fx.Db.SaveChangesAsync();

        (await sut.HasMarksAsync(fx.Family.Id, CancellationToken.None)).Should().BeTrue();
    }
}
