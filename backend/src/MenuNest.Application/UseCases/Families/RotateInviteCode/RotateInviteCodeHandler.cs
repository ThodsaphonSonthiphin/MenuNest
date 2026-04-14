using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.RotateInviteCode;

public sealed class RotateInviteCodeHandler
    : ICommandHandler<RotateInviteCodeCommand, RotateInviteCodeResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public RotateInviteCodeHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<RotateInviteCodeResult> Handle(
        RotateInviteCodeCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var family = await _db.Families.FirstAsync(f => f.Id == familyId, ct);
        var newCode = family.RotateInviteCode();
        await _db.SaveChangesAsync(ct);

        return new RotateInviteCodeResult(newCode.Value);
    }
}
