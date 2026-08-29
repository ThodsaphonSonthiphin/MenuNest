using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.History.UndoChange;

public sealed class UndoChangeHandler : ICommandHandler<UndoChangeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly BudgetChangeApplier _applier;
    private readonly IClock _clock;

    public UndoChangeHandler(
        IApplicationDbContext db, IUserProvisioner users,
        BudgetChangeApplier applier, IClock clock)
    { _db = db; _users = users; _applier = applier; _clock = clock; }

    public async ValueTask<Unit> Handle(UndoChangeCommand cmd, CancellationToken ct)
    {
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var change = await _db.BudgetChanges.FirstOrDefaultAsync(
            c => c.Id == cmd.ChangeId && c.FamilyId == familyId, ct)
            ?? throw new DomainException("Change not found.");

        // menunest-198 also lets the FAMILY HEAD undo anyone's change. That role
        // does not exist yet — it is built in the family-head plan — so this is
        // the single seam where the check is widened. Do not scatter the rule.
        if (change.UserId != user.Id)
            throw new DomainException("You can only undo your own changes.");

        await _applier.ApplyAsync(change, -1, ct);
        change.MarkUndone(user.Id, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
