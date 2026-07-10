using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.GetStopWeather;

public sealed class GetStopWeatherValidator : AbstractValidator<GetStopWeatherQuery>
{
    public GetStopWeatherValidator()
    {
        RuleFor(x => x.Points).NotEmpty();
        RuleForEach(x => x.Points).ChildRules(p =>
        {
            p.RuleFor(x => x.StopId).NotEmpty();
            p.RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
            p.RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
        });
    }
}
