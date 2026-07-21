using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.GetHourlyForecast;

public sealed class GetHourlyForecastValidator : AbstractValidator<GetHourlyForecastQuery>
{
    public GetHourlyForecastValidator()
    {
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
        RuleFor(x => x.Hours).InclusiveBetween(1, 240);
    }
}
