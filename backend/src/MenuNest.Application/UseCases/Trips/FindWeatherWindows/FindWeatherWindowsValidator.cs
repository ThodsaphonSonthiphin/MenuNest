using FluentValidation;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

public sealed class FindWeatherWindowsValidator : AbstractValidator<FindWeatherWindowsQuery>
{
    public FindWeatherWindowsValidator()
    {
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180);

        RuleFor(x => x.Signals)
            .NotEmpty()
            .WithMessage("Select at least one signal: Rain, Heat or Sun. For raw hours use the hourly forecast.");
        RuleForEach(x => x.Signals).IsInEnum();

        RuleFor(x => x.FromHour!.Value).InclusiveBetween(0, 23)
            .When(x => x.FromHour is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.FromHour));
        RuleFor(x => x.ToHour!.Value).InclusiveBetween(1, 24)
            .When(x => x.ToHour is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.ToHour));

        RuleFor(x => x)
            .Must(x => x.FromDate is null || x.ToDate is null || x.FromDate <= x.ToDate)
            .WithMessage("fromDate must not be after toDate.");

        // A per-call threshold is never "off" (that is the stored 0); to drop a gate, leave the signal out.
        RuleFor(x => x.MaxRainPct!.Value).InclusiveBetween(1, 100)
            .When(x => x.MaxRainPct is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.MaxRainPct));
        RuleFor(x => x.MaxFeelsLikeC!.Value).InclusiveBetween(1, 60)
            .When(x => x.MaxFeelsLikeC is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.MaxFeelsLikeC));
        RuleFor(x => x.MaxUvIndex!.Value).InclusiveBetween(1, 20)
            .When(x => x.MaxUvIndex is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.MaxUvIndex));

        // menunest-220: a threshold for an unselected signal means the caller misunderstands the tool.
        RuleFor(x => x.MaxRainPct).Null()
            .When(x => x.Signals is not null && !x.Signals.Contains(WeatherSignal.Rain))
            .WithMessage("maxRainPct was given but Rain is not in signals.");
        RuleFor(x => x.MaxFeelsLikeC).Null()
            .When(x => x.Signals is not null && !x.Signals.Contains(WeatherSignal.Heat))
            .WithMessage("maxFeelsLikeC was given but Heat is not in signals.");
        RuleFor(x => x.MaxUvIndex).Null()
            .When(x => x.Signals is not null && !x.Signals.Contains(WeatherSignal.Sun))
            .WithMessage("maxUvIndex was given but Sun is not in signals.");

        RuleFor(x => x.MinWindowHours!.Value).InclusiveBetween(1, 24)
            .When(x => x.MinWindowHours is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.MinWindowHours));
    }
}
