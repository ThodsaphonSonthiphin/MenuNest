namespace MenuNest.Application.UseCases.Trips.RetimeStopToWeather;

/// <summary>What hour to re-time an anchor stop to.
/// <list type="bullet">
/// <item><c>hour</c> — an explicit local wall-clock hour in <see cref="LocalDateTime"/>.</item>
/// <item><c>coolestDaytime</c>/<c>coolestNighttime</c> — the min feels-like hour of that half of
/// the day, searched over <see cref="WindowHours"/> (default 48, max 240) of forecast.</item>
/// </list></summary>
public sealed record RetimeTarget(string Kind, DateTime? LocalDateTime, int? WindowHours);
