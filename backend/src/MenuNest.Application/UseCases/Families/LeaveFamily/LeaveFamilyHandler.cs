using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.LeaveFamily;

public sealed class LeaveFamilyHandler : ICommandHandler<LeaveFamilyCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public LeaveFamilyHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(LeaveFamilyCommand command, CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == familyId, ct)
            ?? throw new DomainException("Family not found.");

        if (family.HeadUserId == user.Id)
        {
            // menunest-201: authority is always taken deliberately. Auto-passing
            // the role would hand somebody power they never asked for and may
            // not notice. The escape is never blocked — hand over, then leave.
            var othersRemain = await _db.Users
                .AnyAsync(u => u.FamilyId == familyId && u.Id != user.Id, ct);

            if (othersRemain)
            {
                throw new DomainException(
                    "You are the family head. Hand the role over to another member before you leave.");
            }

            family.ClearHead();
        }

        var relationships = await _db.UserRelationships
            .Where(r => r.FamilyId == familyId
                        && (r.FromUserId == user.Id || r.ToUserId == user.Id))
            .ToListAsync(ct);

        _db.UserRelationships.RemoveRange(relationships);

        user.LeaveFamily();
        await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
