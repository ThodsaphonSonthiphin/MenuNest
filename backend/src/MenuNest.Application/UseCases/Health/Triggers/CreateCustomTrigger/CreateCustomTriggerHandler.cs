using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Triggers.CreateCustomTrigger;

public sealed class CreateCustomTriggerHandler
    : ICommandHandler<CreateCustomTriggerCommand, TriggerDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public CreateCustomTriggerHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<TriggerDto> Handle(
        CreateCustomTriggerCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new DomainException("Trigger name is required.");

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);
        var trimmed = command.Name.Trim();

        var clash = await _db.Triggers.AnyAsync(t =>
            t.Name == trimmed &&
            (t.IsSeed || t.UserId == user.Id), ct);
        if (clash)
            throw new DomainException("A trigger with this name already exists.");

        var trigger = Trigger.CreateCustom(trimmed, user.Id);
        _db.Triggers.Add(trigger);
        await _db.SaveChangesAsync(ct);

        return new TriggerDto(trigger.Id, trigger.Name, trigger.IsSeed);
    }
}
