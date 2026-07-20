using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Me;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Me.UpdateUserSettings;

/// <summary>
/// Sets the current caller's Home page. Creates the caller's
/// <c>UserSettings</c> row lazily on first write.
/// </summary>
public sealed class UpdateUserSettingsHandler : ICommandHandler<UpdateUserSettingsCommand, UserSettingsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<UpdateUserSettingsCommand> _validator;

    public UpdateUserSettingsHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<UpdateUserSettingsCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<UserSettingsDto> Handle(UpdateUserSettingsCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == user.Id, ct);
        if (settings is null)
        {
            settings = UserSettings.Create(user.Id);
            _db.UserSettings.Add(settings);
        }

        settings.SetHomePath(command.HomePath);
        settings.SetWeatherAlerts(command.UvWarnThreshold, command.FeelsLikeWarnThreshold);
        await _db.SaveChangesAsync(ct);

        return new UserSettingsDto(settings.HomePath, settings.UvWarnThreshold, settings.FeelsLikeWarnThreshold);
    }
}
