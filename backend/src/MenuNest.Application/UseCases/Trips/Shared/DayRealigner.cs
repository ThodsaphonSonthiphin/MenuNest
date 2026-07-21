using MenuNest.Domain.Entities;

namespace MenuNest.Application.UseCases.Trips.Shared;

/// <summary>
/// Re-dates a contiguous, pre-ordered run of <see cref="ItineraryDay"/>s to start at
/// <paramref name="newStartDate"/> (day i -> newStartDate + i days).
///
/// Invariant: the caller MUST apply these date changes in a SINGLE SaveChanges. EF orders
/// the per-row UPDATEs so the unique (TripId, Date) index is never transiently violated;
/// splitting the realign across multiple SaveChanges can collide mid-shift
/// (see UpdateTripHandler / RetimeStopToHourHandler and reference_ef_relational_testing).
/// This helper only mutates the in-memory entities and never calls SaveChanges itself.
/// </summary>
public static class DayRealigner
{
    public static void RealignDays(IReadOnlyList<ItineraryDay> orderedDays, DateOnly newStartDate)
    {
        for (var i = 0; i < orderedDays.Count; i++)
            orderedDays[i].SetDate(newStartDate.AddDays(i));
    }
}
