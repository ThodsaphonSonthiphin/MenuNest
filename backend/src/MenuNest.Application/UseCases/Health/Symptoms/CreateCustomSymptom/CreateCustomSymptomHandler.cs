using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Symptoms.CreateCustomSymptom;

public sealed class CreateCustomSymptomHandler
    : ICommandHandler<CreateCustomSymptomCommand, SymptomDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public CreateCustomSymptomHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<SymptomDto> Handle(
        CreateCustomSymptomCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new DomainException("Symptom name is required.");

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);
        var trimmed = command.Name.Trim();

        // Reject if a seed with the same name already exists, OR the user
        // already has a custom symptom with the same name.
        var clash = await _db.Symptoms.AnyAsync(s =>
            s.Name == trimmed &&
            (s.IsSeed || s.UserId == user.Id), ct);
        if (clash)
            throw new DomainException("A symptom with this name already exists.");

        var symptom = Symptom.CreateCustom(trimmed, user.Id);
        _db.Symptoms.Add(symptom);
        await _db.SaveChangesAsync(ct);

        return new SymptomDto(symptom.Id, symptom.Name, symptom.IsSeed);
    }
}
