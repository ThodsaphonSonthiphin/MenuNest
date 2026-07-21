using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;

namespace MenuNest.Application.UseCases.Trips.GetHourlyForecast;

public sealed class GetHourlyForecastHandler : IQueryHandler<GetHourlyForecastQuery, IReadOnlyList<HourlyReadingDto>>
{
    private readonly IWeatherService _weather;
    private readonly IValidator<GetHourlyForecastQuery> _validator;
    public GetHourlyForecastHandler(IWeatherService weather, IValidator<GetHourlyForecastQuery> validator)
    { _weather = weather; _validator = validator; }

    public async ValueTask<IReadOnlyList<HourlyReadingDto>> Handle(GetHourlyForecastQuery q, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(q, ct);
        var hours = await _weather.GetHourlyAsync(new WeatherPoint("", q.Lat, q.Lng, null), q.Hours, ct);
        return hours
            .Select(h => new HourlyReadingDto(h.DisplayLocal, h.IsDaytime, h.TempC, h.FeelsLikeC, h.ConditionType, h.IconBaseUri, h.RainPct, h.UvIndex))
            .ToList();
    }
}
