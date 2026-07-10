using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.GetStopWeather;

public sealed record GetStopWeatherQuery(WeatherReadingKind Kind, IReadOnlyList<WeatherPointDto> Points)
    : IQuery<IReadOnlyList<WeatherReadingDto>>;
