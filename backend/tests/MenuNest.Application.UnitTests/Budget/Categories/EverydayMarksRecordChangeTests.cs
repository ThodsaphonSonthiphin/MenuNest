using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Categories.SetEverydayMarks;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Categories;

public class EverydayMarksRecordChangeTests
{
    private const string Bkk = "Asia/Bangkok";

    private static SetEverydayMarksHandler Sut(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new SetEverydayMarksValidator(),
            new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

    private static (BudgetCategory already, BudgetCategory toFlip) Seed(HandlerTestFixture fx)
    {
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var already = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        already.MarkEveryday(true);
        var toFlip = BudgetCategory.Create(fx.Family.Id, group.Id, "Dining", null, 1);
        fx.Db.BudgetCategories.AddRange(already, toFlip);
        fx.Db.SaveChanges();
        return (already, toFlip);
    }

    [Fact]
    public async Task Records_only_the_marks_that_actually_flipped()
    {
        using var fx = new HandlerTestFixture();
        var (already, toFlip) = Seed(fx);

        await Sut(fx).Handle(
            new SetEverydayMarksCommand(
                new[]
                {
                    new EverydayMark(already.Id, true),   // unchanged
                    new EverydayMark(toFlip.Id, true),    // flips false -> true
                },
                Bkk),
            CancellationToken.None);

        var change = fx.Db.BudgetChanges.Single();
        change.Kind.Should().Be(BudgetChangeKind.EverydayMark);
        change.CategoryId.Should().Be(toFlip.Id);
        change.FlagValue.Should().BeTrue();
        change.Delta.Should().Be(0m);
        change.UserId.Should().Be(fx.User.Id);
        // The fixture clock is 2026-01-01 UTC, which is 2026-01-01 in Bangkok.
        change.Year.Should().Be(2026);
        change.Month.Should().Be(1);
    }

    [Fact]
    public async Task Records_nothing_when_the_sheet_changes_no_envelope()
    {
        using var fx = new HandlerTestFixture();
        var (already, toFlip) = Seed(fx);

        await Sut(fx).Handle(
            new SetEverydayMarksCommand(
                new[]
                {
                    new EverydayMark(already.Id, true),
                    new EverydayMark(toFlip.Id, false),
                },
                Bkk),
            CancellationToken.None);

        fx.Db.BudgetChanges.Should().BeEmpty();
    }
}
