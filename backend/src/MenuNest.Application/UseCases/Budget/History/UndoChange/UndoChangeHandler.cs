using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MenuNest.Application.UseCases.Budget.History.UndoChange;

public sealed class UndoChangeHandler : ICommandHandler<UndoChangeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly BudgetChangeApplier _applier;
    private readonly IClock _clock;
    private readonly IWebPushSender _push;
    private readonly ILogger<UndoChangeHandler> _logger;

    public UndoChangeHandler(
        IApplicationDbContext db, IUserProvisioner users,
        BudgetChangeApplier applier, IClock clock,
        IWebPushSender push, ILogger<UndoChangeHandler> logger)
    { _db = db; _users = users; _applier = applier; _clock = clock; _push = push; _logger = logger; }

    public async ValueTask<Unit> Handle(UndoChangeCommand cmd, CancellationToken ct)
    {
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var change = await _db.BudgetChanges.FirstOrDefaultAsync(
            c => c.Id == cmd.ChangeId && c.FamilyId == familyId, ct)
            ?? throw new DomainException("Change not found.");

        // menunest-198: a member may undo their own; the FAMILY HEAD may undo
        // anyone's. This is the app's only permission distinction, and
        // menunest-201 keeps it to exactly this one power. This is the single
        // seam where that widening lives — do not scatter the rule.
        if (change.UserId != user.Id)
        {
            var isHead = await _db.Families
                .AnyAsync(f => f.Id == familyId && f.HeadUserId == user.Id, ct);

            if (!isHead)
            {
                throw new DomainException("You can only undo your own changes.");
            }
        }

        await _applier.ApplyAsync(change, -1, ct);
        change.MarkUndone(user.Id, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);

        // menunest-201: the author is told when somebody else undid their work.
        // AFTER the save, so a push failure can never roll back a completed
        // undo, and wrapped, because best-effort must actually be best-effort —
        // the Change history row names the undoer regardless, which is the
        // notice that always lands.
        if (change.UserId != user.Id)
        {
            try
            {
                await _push.SendToUserAsync(
                    change.UserId,
                    "A budget change was undone",
                    $"{user.DisplayName} undid one of your budget changes.",
                    "/budget",
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Undo notification failed for change {ChangeId}; the undo itself succeeded.",
                    change.Id);
            }
        }

        return Unit.Value;
    }
}
