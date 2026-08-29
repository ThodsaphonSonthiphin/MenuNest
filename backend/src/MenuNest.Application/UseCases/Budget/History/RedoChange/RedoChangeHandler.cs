using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.History.RedoChange;

/// <summary>
/// Deliberately a near-copy of <c>UndoChangeHandler</c> rather than a shared
/// base: the two are short, and the duplication reads more clearly than the
/// abstraction would. Redo needs no clock — <c>MarkRedone</c> clears the undone
/// stamp rather than writing a new one.
/// </summary>
public sealed class RedoChangeHandler : ICommandHandler<RedoChangeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly BudgetChangeApplier _applier;

    public RedoChangeHandler(
        IApplicationDbContext db, IUserProvisioner users, BudgetChangeApplier applier)
    { _db = db; _users = users; _applier = applier; }

    public async ValueTask<Unit> Handle(RedoChangeCommand cmd, CancellationToken ct)
    {
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var change = await _db.BudgetChanges.FirstOrDefaultAsync(
            c => c.Id == cmd.ChangeId && c.FamilyId == familyId, ct)
            ?? throw new DomainException("Change not found.");

        // Same seam as UndoChangeHandler, and widened the same way: a member may
        // redo their own, the FAMILY HEAD may redo anyone's (menunest-198).
        if (change.UserId != user.Id)
        {
            var isHead = await _db.Families
                .AnyAsync(f => f.Id == familyId && f.HeadUserId == user.Id, ct);

            if (!isHead)
            {
                throw new DomainException("You can only redo your own changes.");
            }
        }

        await _applier.ApplyAsync(change, +1, ct);
        change.MarkRedone();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
