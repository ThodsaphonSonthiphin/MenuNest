namespace MenuNest.Application.UseCases.Me;

/// <summary>Response payload for <c>PUT /api/me/settings</c>.</summary>
public sealed record UserSettingsDto(string? HomePath);
