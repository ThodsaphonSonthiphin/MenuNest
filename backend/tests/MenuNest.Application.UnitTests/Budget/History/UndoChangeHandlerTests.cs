using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.History.RedoChange;
using MenuNest.Application.UseCases.Budget.History.UndoChange;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.History;

public class UndoChangeHandlerTests
{
    private static UndoChangeHandler Sut(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock);

    private static RedoChangeHandler RedoSut(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db));

    private static BudgetChange Seed(HandlerTestFixture fx, Guid actorId)
    {
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 300m));
        var change = BudgetChange.RecordAssign(fx.Family.Id, actorId, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        fx.Db.SaveChanges();
        return change;
    }

    [Fact]
    public async Task Undoes_my_own_change_and_marks_the_row()
    {
        using var fx = new HandlerTestFixture();
        var change = Seed(fx, fx.User.Id);

        await Sut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(0m);
        var reloaded = fx.Db.BudgetChanges.Single();
        reloaded.IsUndone.Should().BeTrue();
        reloaded.UndoneByUserId.Should().Be(fx.User.Id);
    }

    [Fact]
    public async Task Refuses_to_undo_another_members_change()
    {
        using var fx = new HandlerTestFixture();
        var change = Seed(fx, Guid.NewGuid());

        var act = async () => await Sut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*your own*");
    }

    [Fact]
    public async Task Refuses_to_undo_a_change_that_is_already_undone()
    {
        using var fx = new HandlerTestFixture();
        var change = Seed(fx, fx.User.Id);
        await Sut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        var act = async () => await Sut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already undone*");
    }

    [Fact]
    public async Task Refuses_a_change_belonging_to_another_family()
    {
        using var fx = new HandlerTestFixture();
        var orphan = BudgetChange.RecordAssign(Guid.NewGuid(), fx.User.Id, 2026, 8, Guid.NewGuid(), 300m, null);
        fx.Db.BudgetChanges.Add(orphan);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Sut(fx).Handle(new UndoChangeCommand(orphan.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task Redo_re_applies_the_change_and_clears_the_undone_state()
    {
        using var fx = new HandlerTestFixture();
        var change = Seed(fx, fx.User.Id);
        await Sut(fx).Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        await RedoSut(fx).Handle(new RedoChangeCommand(change.Id), CancellationToken.None);

        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(300m);
        var reloaded = fx.Db.BudgetChanges.Single();
        reloaded.IsUndone.Should().BeFalse();
        reloaded.UndoneByUserId.Should().BeNull();
    }
}
