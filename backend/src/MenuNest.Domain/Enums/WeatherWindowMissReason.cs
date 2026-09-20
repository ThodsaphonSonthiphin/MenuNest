namespace MenuNest.Domain.Enums;

/// <summary>Why a Weather window query returned no window (menunest-225). NoWeatherData is never
/// conflated with AllHoursBlocked: "no forecast" is not "bad weather" (ADR-030/031).</summary>
public enum WeatherWindowMissReason
{
    NoWeatherData,
    AllHoursBlocked,
    NoWindowLongEnough,
}
