using Mediator;

namespace MenuNest.Application.UseCases.Trips.GetHourlyForecast;

public sealed record GetHourlyForecastQuery(double Lat, double Lng, int Hours)
    : IQuery<IReadOnlyList<HourlyReadingDto>>;
