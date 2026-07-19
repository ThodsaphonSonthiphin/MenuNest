using Mediator;
using MenuNest.Application.UseCases.Me;

namespace MenuNest.Application.UseCases.Me.UpdateUserSettings;

public sealed record UpdateUserSettingsCommand(string? HomePath) : ICommand<UserSettingsDto>;
