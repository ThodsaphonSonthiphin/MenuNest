using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.GetStopWeather;

public sealed class GetStopWeatherHandler : IQueryHandler<GetStopWeatherQuery, IReadOnlyList<WeatherReadingDto>>
{
    private readonly IWeatherService _weather;
    private readonly IValidator<GetStopWeatherQuery> _validator;
    public GetStopWeatherHandler(IWeatherService weather, IValidator<GetStopWeatherQuery> validator)
    { _weather = weather; _validator = validator; }

    public async ValueTask<IReadOnlyList<WeatherReadingDto>> Handle(GetStopWeatherQuery q, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(q, ct);
        var points = q.Points
            .Select(p => new WeatherPoint(p.StopId, p.Lat, p.Lng,
                q.Kind == WeatherReadingKind.OnArrival ? p.ArrivalIso : null))
            .ToList();
        var readings = await _weather.GetReadingsAsync(points, q.Kind, ct);
        return readings
            .Select(r => new WeatherReadingDto(r.StopId, r.HasData, r.ConditionType, r.IconBaseUri, r.TempC, r.RainPct, r.Description))
            .ToList();
    }
}
