using Mediator;
using MenuNest.Application.Abstractions;
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
