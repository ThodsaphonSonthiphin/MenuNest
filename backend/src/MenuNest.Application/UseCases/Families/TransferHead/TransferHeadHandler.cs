using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.TransferHead;

/// <summary>
/// menunest-201: the only way the head role moves between people. Taken
/// deliberately by the giver, never assigned by the system — the one exception
/// being a headless family adopting its next joiner (JoinFamilyHandler).
/// </summary>
public sealed class TransferHeadHandler : ICommandHandler<TransferHeadCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public TransferHeadHandler(IApplicationDbContext db, IUserProvisioner users)
    {
        _db = db;
        _users = users;
    }

    public async ValueTask<Unit> Handle(TransferHeadCommand cmd, CancellationToken ct)
    {
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == familyId, ct)
            ?? throw new DomainException("Family not found.");

        var isMember = await _db.Users
            .AnyAsync(u => u.Id == cmd.NewHeadUserId && u.FamilyId == familyId, ct);

        if (!isMember)
        {
            throw new DomainException("That person is not a member of this family.");
        }

        // The entity enforces "only the current head" so the rule lives in one
        // place and cannot be bypassed by a second caller later.
        family.TransferHeadTo(user.Id, cmd.NewHeadUserId);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
