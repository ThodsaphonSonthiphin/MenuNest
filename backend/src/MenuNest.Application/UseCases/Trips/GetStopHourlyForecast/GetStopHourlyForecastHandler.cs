using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.GetStopHourlyForecast;

public sealed class GetStopHourlyForecastHandler : IQueryHandler<GetStopHourlyForecastQuery, IReadOnlyList<HourlyReadingDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IWeatherService _weather;
    private readonly IValidator<GetStopHourlyForecastQuery> _validator;

    public GetStopHourlyForecastHandler(
        IApplicationDbContext db, IUserProvisioner users, IWeatherService weather, IValidator<GetStopHourlyForecastQuery> validator)
    { _db = db; _users = users; _weather = weather; _validator = validator; }

    public async ValueTask<IReadOnlyList<HourlyReadingDto>> Handle(GetStopHourlyForecastQuery q, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(q, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);

        // Owner-scoped: the stop must belong to a day of the given trip owned by the current user.
        var stop = await _db.Stops.FirstOrDefaultAsync(s =>
                s.Id == q.StopId &&
                _db.ItineraryDays.Any(d => d.Id == s.ItineraryDayId && d.TripId == q.TripId &&
                    _db.Trips.Any(t => t.Id == d.TripId && t.UserId == user.Id && t.DeletedAt == null)), ct)
            ?? throw new DomainException("Stop not found.");

        var place = await _db.TripPlaces.FirstOrDefaultAsync(p => p.Id == stop.TripPlaceId, ct)
            ?? throw new DomainException("Place not found.");

        var hours = await _weather.GetHourlyAsync(new WeatherPoint("", place.Lat, place.Lng, null), q.Hours, ct);
        return hours
            .Select(h => new HourlyReadingDto(h.DisplayLocal, h.IsDaytime, h.TempC, h.FeelsLikeC, h.ConditionType, h.IconBaseUri, h.RainPct, h.UvIndex))
            .ToList();
    }
}
