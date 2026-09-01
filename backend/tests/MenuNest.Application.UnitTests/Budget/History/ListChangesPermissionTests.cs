using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.History.ListChanges;
using MenuNest.Application.UseCases.Budget.History.RedoChange;
using MenuNest.Application.UseCases.Budget.History.UndoChange;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MenuNest.Application.UnitTests.Budget.History;

/// <summary>
/// menunest-216: the Change history row carries WHO may act on it, so the sheet
/// and the Shortcut rail never offer a control that the handler will refuse.
///
/// TRAP for anyone adding a case here: <c>fx.User</c> created the Family and is
/// therefore its head, so a test that does not repoint <c>fx.UserProvisioner</c>
/// is silently testing the head and passes whatever the rule says.
/// </summary>
public class ListChangesPermissionTests
{
    private static ListChangesHandler Sut(HandlerTestFixture fx)
    {
        // Same August 2026 clock as ListChangesHandlerTests, so menunest-194's
        // seven-day floor does not exclude the seeded rows.
        fx.Clock.UtcNow = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        return new ListChangesHandler(fx.Db, fx.UserProvisioner.Object, fx.Clock);
    }

    private static RedoChangeHandler RedoSut(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db));

    private static UndoChangeHandler UndoSut(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock,
            Mock.Of<IWebPushSender>(), NullLogger<UndoChangeHandler>.Instance);

    /// <summary>A second Family member who is NOT the head, and is not fx.User.</summary>
    private static User AddMember(HandlerTestFixture fx, string slug)
    {
        var member = User.CreateFromExternalLogin(
            $"{slug}-oid", $"{slug}@example.com", slug, AuthProvider.Microsoft);
        member.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(member);
        fx.Db.SaveChanges();
        return member;
    }

    private static void CallAs(HandlerTestFixture fx, User user) =>
        fx.UserProvisioner
            .Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((user, fx.Family.Id));

    private static BudgetCategory SeedCategory(HandlerTestFixture fx)
    {
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 300m));
        fx.Db.SaveChanges();
        return cat;
    }

    private static BudgetChange SeedChange(HandlerTestFixture fx, BudgetCategory cat, Guid actorId)
    {
        var change = BudgetChange.RecordAssign(fx.Family.Id, actorId, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        fx.Db.SaveChanges();
        return change;
    }

    [Fact]
    public async Task An_ordinary_member_may_undo_their_own_row_but_not_another_members()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx);
        var member = AddMember(fx, "member");

        var theirs = SeedChange(fx, cat, fx.User.Id);   // the head's row
        var mine = SeedChange(fx, cat, member.Id);
        CallAs(fx, member);

        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);

        result.Single(r => r.Id == mine.Id).CanUndo.Should().BeTrue();

        var foreign = result.Single(r => r.Id == theirs.Id);
        foreign.CanUndo.Should().BeFalse();
        foreign.BlockedReason.Should().Contain("family head");
        // menunest-216: the row is perfectly valid — only the caller is wrong — so
        // it must NOT be marked dead. IsDead is what greys the row, and greying a
        // row that is live in the head's hands is the thing this ADR refused.
        foreign.IsDead.Should().BeFalse();
    }

    [Fact]
    public async Task The_head_may_undo_every_row()
    {
        using var fx = new HandlerTestFixture();   // fx.User created the Family, so is head
        var cat = SeedCategory(fx);
        var member = AddMember(fx, "member");

        SeedChange(fx, cat, fx.User.Id);
        SeedChange(fx, cat, member.Id);

        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.CanUndo);
        result.Should().OnlyContain(r => r.BlockedReason == null);
    }

    [Fact]
    public async Task A_deleted_envelope_beats_the_permission_reason_and_still_marks_the_row_dead()
    {
        using var fx = new HandlerTestFixture();
        var member = AddMember(fx, "member");
        // Somebody else's row, on an Envelope that no longer exists. Both reasons
        // apply; menunest-216 says the Envelope wins, because that one is true for
        // the head too and "not yours" would be a lie to them.
        var orphan = BudgetChange.RecordAssign(
            fx.Family.Id, fx.User.Id, 2026, 8, Guid.NewGuid(), 300m, null);
        fx.Db.BudgetChanges.Add(orphan);
        await fx.Db.SaveChangesAsync();
        CallAs(fx, member);

        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);

        result[0].CanUndo.Should().BeFalse();
        result[0].IsDead.Should().BeTrue();
        result[0].BlockedReason.Should().Contain("deleted");
    }

    [Fact]
    public async Task The_heads_undo_sticks_so_the_author_cannot_redo_it()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx);
        var member = AddMember(fx, "member");
        var change = SeedChange(fx, cat, member.Id);

        // The head undoes the member's change (menunest-198).
        await UndoSut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        // The author lists history. The row is still THEIRS — but it is not theirs
        // to redo, which is the whole reason CanRedo is a second field.
        CallAs(fx, member);
        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);

        var row = result.Single();
        row.UserId.Should().Be(member.Id);
        row.IsUndone.Should().BeTrue();
        row.CanRedo.Should().BeFalse();
        row.IsDead.Should().BeFalse();
        row.BlockedReason.Should().Contain("redo");

        // …and the handler agrees, so the disabled button is not the only guard.
        var act = async () => await RedoSut(fx)
            .Handle(new RedoChangeCommand(change.Id), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*undid*");
    }

    [Fact]
    public async Task The_head_may_redo_the_undo_they_performed()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx);
        var member = AddMember(fx, "member");
        var change = SeedChange(fx, cat, member.Id);

        await UndoSut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);
        result.Single().CanRedo.Should().BeTrue();

        await RedoSut(fx).Handle(new RedoChangeCommand(change.Id), CancellationToken.None);
        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(300m);
    }

    [Fact]
    public async Task A_member_may_redo_an_undo_they_performed_themselves()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx);
        var member = AddMember(fx, "member");
        var change = SeedChange(fx, cat, member.Id);
        CallAs(fx, member);

        await UndoSut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        var result = await Sut(fx).Handle(new ListChangesQuery(2026, 8), CancellationToken.None);
        var row = result.Single();
        row.CanRedo.Should().BeTrue();
        // The undo button is gone on an undone row, so CanUndo is false for
        // everyone — the sheet renders ทำซ้ำ instead and reads CanRedo.
        row.CanUndo.Should().BeFalse();
        row.BlockedReason.Should().BeNull();

        await RedoSut(fx).Handle(new RedoChangeCommand(change.Id), CancellationToken.None);
        fx.Db.BudgetChanges.Single().IsUndone.Should().BeFalse();
    }

    [Fact]
    public async Task Redoing_a_row_that_is_not_undone_says_so_rather_than_blaming_permission()
    {
        using var fx = new HandlerTestFixture();
        var cat = SeedCategory(fx);
        var member = AddMember(fx, "member");
        var change = SeedChange(fx, cat, member.Id);
        CallAs(fx, member);

        // UndoneByUserId is null here, so the permission check would reject the
        // author of the row unless the not-undone guard runs first.
        var act = async () => await RedoSut(fx)
            .Handle(new RedoChangeCommand(change.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*not undone*");
    }
}
