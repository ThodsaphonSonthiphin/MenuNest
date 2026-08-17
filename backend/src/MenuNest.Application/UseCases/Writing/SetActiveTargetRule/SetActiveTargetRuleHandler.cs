using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.SetActiveTargetRule;

public sealed class SetActiveTargetRuleHandler : ICommandHandler<SetActiveTargetRuleCommand, string?>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<SetActiveTargetRuleCommand> _validator;

    public SetActiveTargetRuleHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<SetActiveTargetRuleCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<string?> Handle(SetActiveTargetRuleCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == user.Id, ct);
        if (settings is null)
        {
            settings = UserSettings.Create(user.Id);
            _db.UserSettings.Add(settings);
        }

        // Only the rule — HomePath and the weather thresholds are untouched.
        settings.SetActiveTargetRule(command.Rule);
        await _db.SaveChangesAsync(ct);

        return settings.ActiveTargetRule;
    }
}
