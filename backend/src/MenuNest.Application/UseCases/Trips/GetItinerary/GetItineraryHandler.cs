using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.GetItinerary;

public sealed class GetItineraryHandler : IQueryHandler<GetItineraryQuery, IReadOnlyList<ItineraryDayDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IRouteService _routes;

    public GetItineraryHandler(IApplicationDbContext db, IUserProvisioner users, IRouteService routes)
    { _db = db; _users = users; _routes = routes; }

    public async ValueTask<IReadOnlyList<ItineraryDayDto>> Handle(GetItineraryQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == q.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        var days = await _db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).ToListAsync(ct);
        var stops = await _db.Stops
            .Where(s => _db.ItineraryDays.Any(d => d.Id == s.ItineraryDayId && d.TripId == trip.Id))
            .OrderBy(s => s.Sequence).ToListAsync(ct);
        var placeIds = stops.Select(s => s.TripPlaceId).Distinct().ToList();
        var places = await _db.TripPlaces.Where(p => placeIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var dayStopsById = days.ToDictionary(
            d => d.Id, d => stops.Where(s => s.ItineraryDayId == d.Id).OrderBy(s => s.Sequence).ToList());

        // Resolve every leg across all days concurrently. Each leg keeps its own travel mode
        // so legs cannot be merged into one matrix call — but a cold cache then costs ~1
        // round-trip instead of N sequential ones. No DbContext is touched past this point,
        // so the parallel route calls are safe (the context is not thread-safe).
        var legTasks = new List<Task<(Guid DayId, int Index, LegTime Leg)>>();
        foreach (var day in days)
        {
            var dayStops = dayStopsById[day.Id];
            for (var li = 1; li < dayStops.Count; li++)
            {
                var origin = new RoutePoint(places[dayStops[li - 1].TripPlaceId].Lat, places[dayStops[li - 1].TripPlaceId].Lng);
                var dest = new RoutePoint(places[dayStops[li].TripPlaceId].Lat, places[dayStops[li].TripPlaceId].Lng);
                legTasks.Add(ResolveLegAsync(day.Id, li, origin, dest, dayStops[li].TravelModeToReach, ct));
            }
        }
        var legResults = await Task.WhenAll(legTasks);
        var legByKey = legResults.ToDictionary(x => (x.DayId, x.Index), x => x.Leg);

        var result = new List<ItineraryDayDto>(days.Count);
        foreach (var day in days)
        {
            var dayStops = dayStopsById[day.Id];
            var stopDtos = new List<StopDto>(dayStops.Count);
            for (var i = 0; i < dayStops.Count; i++)
            {
                var s = dayStops[i];
                LegDto? leg = i == 0 ? null : new LegDto(legByKey[(day.Id, i)].Seconds, legByKey[(day.Id, i)].Meters);
                stopDtos.Add(new StopDto(s.Id, s.TripPlaceId, s.Sequence, s.DwellMinutes, s.TravelModeToReach, leg));
            }
            result.Add(new ItineraryDayDto(day.Id, day.Date, day.DayStartTime, stopDtos));
        }
        return result;

        async Task<(Guid, int, LegTime)> ResolveLegAsync(Guid dayId, int index, RoutePoint origin, RoutePoint dest, TravelMode mode, CancellationToken token)
        {
            var legs = await _routes.GetLegTimesAsync(new[] { origin, dest }, mode, token);
            return (dayId, index, legs[0]);
        }
    }
}
