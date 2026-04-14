using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.DeleteRelationship;

public sealed class DeleteRelationshipHandler : ICommandHandler<DeleteRelationshipCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteRelationshipHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteRelationshipCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var relationship = await _db.UserRelationships
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.FamilyId == familyId, ct)
            ?? throw new DomainException("Relationship not found.");

        _db.UserRelationships.Remove(relationship);
        await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
