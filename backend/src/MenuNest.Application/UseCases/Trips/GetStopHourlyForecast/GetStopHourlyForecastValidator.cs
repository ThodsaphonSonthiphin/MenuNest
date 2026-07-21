using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.GetStopHourlyForecast;

public sealed class GetStopHourlyForecastValidator : AbstractValidator<GetStopHourlyForecastQuery>
{
    public GetStopHourlyForecastValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.StopId).NotEmpty();
        RuleFor(x => x.Hours).InclusiveBetween(1, 240);
    }
}
