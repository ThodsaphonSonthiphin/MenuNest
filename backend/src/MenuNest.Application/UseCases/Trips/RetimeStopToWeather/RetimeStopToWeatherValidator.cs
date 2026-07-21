using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToWeather;

public sealed class RetimeStopToWeatherValidator : AbstractValidator<RetimeStopToWeatherCommand>
{
    private static readonly string[] Kinds = { "hour", "coolestDaytime", "coolestNighttime" };

    public RetimeStopToWeatherValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.DayId).NotEmpty();
        RuleFor(x => x.StopId).NotEmpty();
        RuleFor(x => x.Target).NotNull();

        When(x => x.Target is not null, () =>
        {
            RuleFor(x => x.Target.Kind)
                .Must(k => Kinds.Contains(k))
                .WithMessage("Target.Kind must be one of: hour, coolestDaytime, coolestNighttime.");

            RuleFor(x => x.Target.LocalDateTime)
                .NotNull()
                .When(x => x.Target.Kind == "hour")
                .WithMessage("Target.LocalDateTime is required when Kind is 'hour'.");

            RuleFor(x => x.Target.WindowHours)
                .InclusiveBetween(1, 240)
                .When(x => x.Target.Kind != "hour" && x.Target.WindowHours.HasValue);
        });
    }
}
