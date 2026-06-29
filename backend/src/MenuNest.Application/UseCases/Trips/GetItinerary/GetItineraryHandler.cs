using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
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

        var result = new List<ItineraryDayDto>(days.Count);
        foreach (var day in days)
        {
            var dayStops = stops.Where(s => s.ItineraryDayId == day.Id).OrderBy(s => s.Sequence).ToList();
            var points = dayStops.Select(s => new RoutePoint(places[s.TripPlaceId].Lat, places[s.TripPlaceId].Lng)).ToList();
            var legTimes = new LegTime[dayStops.Count > 0 ? dayStops.Count - 1 : 0];
            for (var li = 1; li < dayStops.Count; li++)
            {
                var pairPoints = new[] { points[li - 1], points[li] };
                var pairLegs = await _routes.GetLegTimesAsync(pairPoints, dayStops[li].TravelModeToReach, ct);
                legTimes[li - 1] = pairLegs[0];
            }

            var stopDtos = new List<StopDto>(dayStops.Count);
            for (var i = 0; i < dayStops.Count; i++)
            {
                var s = dayStops[i];
                LegDto? leg = i == 0 ? null : new LegDto(legTimes[i - 1].Seconds, legTimes[i - 1].Meters);
                stopDtos.Add(new StopDto(s.Id, s.TripPlaceId, s.Sequence, s.DwellMinutes, s.TravelModeToReach, leg));
            }
            result.Add(new ItineraryDayDto(day.Id, day.Date, day.DayStartTime, stopDtos));
        }
        return result;
    }
}
