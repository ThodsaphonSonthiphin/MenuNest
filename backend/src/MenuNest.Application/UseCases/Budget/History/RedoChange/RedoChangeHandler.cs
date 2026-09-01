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

        // BEFORE the permission check, because the check below reads
        // UndoneByUserId and that is null on a row nobody has undone — so without
        // this line "redo something that is not undone" would be reported as a
        // permission failure. MarkRedone throws the same sentence; it just throws
        // it too late to be the message the caller sees.
        if (!change.IsUndone) throw new DomainException("This change is not undone.");

        // Same seam as UndoChangeHandler and widened the same way to the FAMILY
        // HEAD (menunest-198) — but it reads a DIFFERENT field, and that is the
        // whole of menunest-216. Redo reverses an UNDOING, not an authoring, so it
        // belongs to whoever undid the row. Reading change.UserId here instead
        // would let the author redo an undo the head performed, which would leave
        // the head's one power (menunest-201) lasting exactly until the author
        // pressed ทำซ้ำ.
        if (change.UndoneByUserId != user.Id)
        {
            var isHead = await _db.Families
                .AnyAsync(f => f.Id == familyId && f.HeadUserId == user.Id, ct);

            if (!isHead)
            {
                throw new DomainException("You can only redo a change you undid yourself.");
            }
        }

        await _applier.ApplyAsync(change, +1, ct);
        change.MarkRedone();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
