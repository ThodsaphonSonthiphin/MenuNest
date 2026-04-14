using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.JoinFamily;

public sealed class JoinFamilyHandler : ICommandHandler<JoinFamilyCommand, FamilyDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<JoinFamilyCommand> _validator;

    public JoinFamilyHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<JoinFamilyCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<FamilyDto> Handle(JoinFamilyCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        if (user.FamilyId.HasValue)
            throw new DomainException("You already belong to a family. Leave it first before joining another.");

        var code = InviteCode.From(command.InviteCode);

        var family = await _db.Families
            .Include(f => f.Members)
            .FirstOrDefaultAsync(f => f.InviteCode == code, ct)
            ?? throw new DomainException("Invite code is invalid or expired.");

        user.JoinFamily(family.Id);
        await _db.SaveChangesAsync(ct);

        return new FamilyDto(
            Id: family.Id,
            Name: family.Name,
            InviteCode: family.InviteCode.Value,
            MemberCount: family.Members.Count);
    }
}
