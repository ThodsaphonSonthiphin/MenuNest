using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.History.UndoChange;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MenuNest.Application.UnitTests.Budget.History;

/// <summary>
/// menunest-201: undoing somebody else's work is visible to them. The third
/// test is the important one — best-effort has to actually be best-effort, or
/// a push outage would start failing legitimate corrections.
/// </summary>
public class HeadUndoNotifiesTests
{
    private static UndoChangeHandler Sut(HandlerTestFixture fx, IWebPushSender push) =>
        new(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock,
            push, NullLogger<UndoChangeHandler>.Instance);

    private static BudgetCategory SeedCategory(HandlerTestFixture fx)
    {
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 300m));
        return cat;
    }

    private static (User other, BudgetChange change) Seed(HandlerTestFixture fx)
    {
        var other = User.CreateFromExternalLogin(
            "other-oid", "other@example.com", "Other Member", AuthProvider.Microsoft);
        other.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(other);

        var cat = SeedCategory(fx);
        var change = BudgetChange.RecordAssign(fx.Family.Id, other.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        fx.Db.SaveChanges();
        return (other, change);
    }

    [Fact]
    public async Task The_author_is_notified_when_the_head_undoes_their_change()
    {
        using var fx = new HandlerTestFixture();
        var (other, change) = Seed(fx);
        var push = new Mock<IWebPushSender>();

        await Sut(fx, push.Object).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        push.Verify(p => p.SendToUserAsync(
            other.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Undoing_my_own_change_notifies_nobody()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx);
        var mine = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(mine);
        await fx.Db.SaveChangesAsync();
        var push = new Mock<IWebPushSender>();

        await Sut(fx, push.Object).Handle(new UndoChangeCommand(mine.Id), CancellationToken.None);

        push.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failing_push_does_not_fail_the_undo()
    {
        using var fx = new HandlerTestFixture();
        var (_, change) = Seed(fx);
        var push = new Mock<IWebPushSender>();
        push.Setup(p => p.SendToUserAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("push is down"));

        await Sut(fx, push.Object).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        fx.Db.BudgetChanges.Single().IsUndone.Should().BeTrue();
    }
}
