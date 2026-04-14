using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.AddRelationship;

public sealed class AddRelationshipHandler
    : ICommandHandler<AddRelationshipCommand, RelationshipDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<AddRelationshipCommand> _validator;

    public AddRelationshipHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<AddRelationshipCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<RelationshipDto> Handle(
        AddRelationshipCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var fromUser = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == command.FromUserId && u.FamilyId == familyId, ct)
            ?? throw new DomainException("From user is not a member of this family.");

        var toUser = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == command.ToUserId && u.FamilyId == familyId, ct)
            ?? throw new DomainException("To user is not a member of this family.");

        var relationship = UserRelationship.Create(
            familyId, command.FromUserId, command.ToUserId, command.RelationType);

        _db.UserRelationships.Add(relationship);
        await _db.SaveChangesAsync(ct);

        return new RelationshipDto(
            Id: relationship.Id,
            FromUserId: relationship.FromUserId,
            FromUserName: fromUser.DisplayName,
            ToUserId: relationship.ToUserId,
            ToUserName: toUser.DisplayName,
            RelationType: relationship.RelationType.ToString());
    }
}
