using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UseCases.Families.CreateFamily;

/// <summary>
/// Creates a new family, sets the current caller as its creator, and
/// makes the caller a member. Rejects callers that already belong to
/// a family — they must leave first before creating another.
/// </summary>
public sealed class CreateFamilyHandler : ICommandHandler<CreateFamilyCommand, FamilyDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<CreateFamilyCommand> _validator;

    public CreateFamilyHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<CreateFamilyCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<FamilyDto> Handle(CreateFamilyCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        if (user.FamilyId.HasValue)
        {
            throw new DomainException("You already belong to a family. Leave it first before creating another.");
        }

        var family = Family.CreateNew(command.Name, user.Id);
        _db.Families.Add(family);
        user.JoinFamily(family.Id);

        await _db.SaveChangesAsync(ct);

        return new FamilyDto(
            Id: family.Id,
            Name: family.Name,
            InviteCode: family.InviteCode.Value,
            MemberCount: 1);
    }
}
