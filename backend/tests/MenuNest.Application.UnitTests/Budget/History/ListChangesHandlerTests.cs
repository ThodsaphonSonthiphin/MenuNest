using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History.ListChanges;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.History;

public class ListChangesHandlerTests
{
    private static ListChangesHandler Sut(HandlerTestFixture fx)
    {
        // Sit the clock inside August 2026 so the seven-day half of
        // menunest-194's window does not exclude the seeded rows.
        fx.Clock.UtcNow = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        return new ListChangesHandler(fx.Db, fx.UserProvisioner.Object, fx.Clock);
    }

    private static BudgetCategory SeedCategory(HandlerTestFixture fx, string name, int sort = 0)
    {
        var group = fx.Db.BudgetCategoryGroups.FirstOrDefault();
        if (group is null)
        {
            group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
            fx.Db.BudgetCategoryGroups.Add(group);
        }
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, name, null, sort);
        fx.Db.BudgetCategories.Add(cat);
        return cat;
    }

    [Fact]
    public async Task Returns_this_months_rows_and_excludes_last_month()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");

        var thisMonth = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        var lastMonth = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 7, cat.Id, 100m, null);
        fx.Db.BudgetChanges.AddRange(thisMonth, lastMonth);
        await fx.Db.SaveChangesAsync();

        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(thisMonth.Id);
        result[0].CategoryName.Should().Be("Groceries");
        result[0].CanUndo.Should().BeTrue();
        result[0].BlockedReason.Should().BeNull();
        result[0].UserDisplayName.Should().Be(fx.User.DisplayName);
    }

    [Fact]
    public async Task Excludes_a_row_older_than_seven_days_even_inside_the_same_month()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");

        var recent = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        var old = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 100m, null);
        fx.Db.BudgetChanges.AddRange(recent, old);
        await fx.Db.SaveChangesAsync();

        // Age `old` past the seven-day floor. CreatedAt is protected on Entity,
        // so reach it the way EF would.
        fx.Db.Entry(old).Property(nameof(BudgetChange.CreatedAt)).CurrentValue =
            new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);
        fx.Db.Entry(recent).Property(nameof(BudgetChange.CreatedAt)).CurrentValue =
            new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc);
        await fx.Db.SaveChangesAsync();

        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(recent.Id);
    }

    [Fact]
    public async Task Keeps_a_row_whose_envelope_is_gone_but_marks_it_unusable()
    {
        using var fx = new HandlerTestFixture();
        var orphan = BudgetChange.RecordAssign(
            fx.Family.Id, fx.User.Id, 2026, 8, Guid.NewGuid(), 300m, null);
        fx.Db.BudgetChanges.Add(orphan);
        await fx.Db.SaveChangesAsync();

        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);

        // menunest-197: the row STAYS, unpressable, saying why.
        result.Should().HaveCount(1);
        result[0].CanUndo.Should().BeFalse();
        result[0].BlockedReason.Should().Contain("deleted");
    }

    [Fact]
    public async Task Names_the_member_who_undid_a_row()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx, "Groceries");
        var change = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        change.MarkUndone(fx.User.Id, new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        fx.Db.BudgetChanges.Add(change);
        await fx.Db.SaveChangesAsync();

        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);

        result[0].IsUndone.Should().BeTrue();
        result[0].UndoneByDisplayName.Should().Be(fx.User.DisplayName);
    }
}
