using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.History.UndoChange;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Moq;

namespace MenuNest.Application.UnitTests.Budget.History;

/// <summary>
/// menunest-198: the head may undo anyone's change. This is the app's ONLY
/// permission distinction, so it is worth pinning both halves — that the head
/// can, and that an ordinary member still cannot.
/// </summary>
public class HeadUndoesAnyoneTests
{
    private static (User other, BudgetChange change) Seed(HandlerTestFixture fx)
    {
        var other = User.CreateFromExternalLogin(
            "other-oid", "other@example.com", "Other Member", AuthProvider.Microsoft);
        other.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(other);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 300m));

        var change = BudgetChange.RecordAssign(fx.Family.Id, other.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        fx.Db.SaveChanges();
        return (other, change);
    }

    [Fact]
    public async Task The_head_may_undo_another_members_change()
    {
        using var fx = new HandlerTestFixture();   // fx.User created the family, so is head
        var (_, change) = Seed(fx);

        await new UndoChangeHandler(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock)
            .Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(0m);
        fx.Db.BudgetChanges.Single().UndoneByUserId.Should().Be(fx.User.Id);
    }

    [Fact]
    public async Task An_ordinary_member_still_cannot_undo_another_members_change()
    {
        using var fx = new HandlerTestFixture();
        Seed(fx);

        // A third member: not the head, not the author.
        var third = User.CreateFromExternalLogin(
            "third-oid", "third@example.com", "Third", AuthProvider.Microsoft);
        third.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(third);
        await fx.Db.SaveChangesAsync();
        fx.UserProvisioner
            .Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((third, fx.Family.Id));

        var change = fx.Db.BudgetChanges.Single();
        var act = async () =>
            await new UndoChangeHandler(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock)
                .Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*your own*");
    }
}
