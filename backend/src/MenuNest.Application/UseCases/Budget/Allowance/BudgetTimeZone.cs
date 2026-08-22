using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UseCases.Budget.Allowance;

/// <summary>
/// Resolves the IANA time-zone id a Budget request supplies into the
/// <see cref="TimeZoneInfo"/> used to read the viewer's local "today"
/// (menunest-189). Applies ADR-038's Trips pattern
/// (<c>GetItineraryHandler</c>) to the Budget module: no silent UTC
/// fallback — a missing or unknown id is rejected wherever "today" is
/// actually needed, never quietly replaced.
/// </summary>
public static class BudgetTimeZone
{
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new DomainException("Time zone is required.");
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException($"Unknown time zone: {timeZoneId}");
        }
    }
}
