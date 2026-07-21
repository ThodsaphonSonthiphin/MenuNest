using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.RetimeStopToHour;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToWeather;

public sealed class RetimeStopToWeatherHandler : ICommandHandler<RetimeStopToWeatherCommand, RetimeResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IRouteService _routes;
    private readonly IWeatherService _weather;
    private readonly IMediator _mediator;
    private readonly IValidator<RetimeStopToWeatherCommand> _validator;

    public RetimeStopToWeatherHandler(
        IApplicationDbContext db, IUserProvisioner users, IRouteService routes, IWeatherService weather,
        IMediator mediator, IValidator<RetimeStopToWeatherCommand> validator)
    { _db = db; _users = users; _routes = routes; _weather = weather; _mediator = mediator; _validator = validator; }

    public async ValueTask<RetimeResultDto> Handle(RetimeStopToWeatherCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);

        var trip = await _db.Trips.FirstOrDefaultAsync(
                t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");
        var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.Id == c.DayId && d.TripId == trip.Id, ct)
            ?? throw new DomainException("Itinerary day not found.");

        var dayStops = await _db.Stops
            .Where(s => s.ItineraryDayId == day.Id).OrderBy(s => s.Sequence).ToListAsync(ct);
        var anchorIndex = dayStops.FindIndex(s => s.Id == c.StopId);
        if (anchorIndex < 0) throw new DomainException("Stop not found.");

        var placeIds = dayStops.Take(anchorIndex + 1).Select(s => s.TripPlaceId).Distinct().ToList();
        var places = await _db.TripPlaces.Where(p => placeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        // Offset from the day start to the anchor's arrival: Σ inter-stop leg minutes (rounded) up to the
        // anchor + Σ dwell of stops before it. NO approach leg — mirrors the client cascade minus its live leg.
        var offsetMin = 0;
        for (var i = 1; i <= anchorIndex; i++)
        {
            var origin = new RoutePoint(places[dayStops[i - 1].TripPlaceId].Lat, places[dayStops[i - 1].TripPlaceId].Lng);
            var dest = new RoutePoint(places[dayStops[i].TripPlaceId].Lat, places[dayStops[i].TripPlaceId].Lng);
            var legs = await _routes.GetLegTimesAsync(new[] { origin, dest }, dayStops[i].TravelModeToReach, ct);
            // Round half-up (AwayFromZero) to match the client JS Math.round in retiming.ts -
            // C# default Math.Round is banker rounding (.5 -> even), a latent cross-language divergence.
            offsetMin += (int)Math.Round(legs[0].Seconds / 60.0, MidpointRounding.AwayFromZero);
        }
        for (var i = 0; i < anchorIndex; i++)
            offsetMin += dayStops[i].DwellMinutes;

        var (date, time) = await ResolveTargetAsync(c.Target, places[dayStops[anchorIndex].TripPlaceId], ct);

        var startMin = (time.Hour * 60 + time.Minute) - offsetMin;
        if (startMin < 0) throw new DomainException("ไปถึงไม่ทัน");  // unreachably early
        var newStart = new TimeOnly(startMin / 60, startMin % 60);

        return await _mediator.Send(new RetimeStopToHourCommand(c.TripId, c.DayId, c.StopId, newStart, date), ct);
    }

    private async Task<(DateOnly Date, TimeOnly Time)> ResolveTargetAsync(RetimeTarget target, TripPlace anchor, CancellationToken ct)
    {
        if (target.Kind == "hour")
        {
            var ldt = target.LocalDateTime!.Value;   // validator guarantees non-null for 'hour'
            return (DateOnly.FromDateTime(ldt), TimeOnly.FromDateTime(ldt));
        }

        var daytime = target.Kind == "coolestDaytime";
        var hours = await _weather.GetHourlyAsync(
            new WeatherPoint("", anchor.Lat, anchor.Lng, null), target.WindowHours ?? 48, ct);
        var chosen = WeatherHourSelection.CoolestHour(hours, daytime)
            ?? throw new DomainException("ไม่พบชั่วโมงที่เหมาะสมในช่วงพยากรณ์");  // no forecast data for that half
        return (DateOnly.FromDateTime(chosen.DisplayLocal), TimeOnly.FromDateTime(chosen.DisplayLocal));
    }
}
