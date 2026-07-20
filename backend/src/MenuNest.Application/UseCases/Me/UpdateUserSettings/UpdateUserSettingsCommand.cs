using Mediator;
using MenuNest.Application.UseCases.Me;

namespace MenuNest.Application.UseCases.Me.UpdateUserSettings;

public sealed record UpdateUserSettingsCommand(
    string? HomePath, int? UvWarnThreshold = null, int? FeelsLikeWarnThreshold = null) : ICommand<UserSettingsDto>;
