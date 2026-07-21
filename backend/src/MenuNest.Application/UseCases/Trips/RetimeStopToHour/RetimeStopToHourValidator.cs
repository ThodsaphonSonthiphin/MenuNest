using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToHour;

public sealed class RetimeStopToHourValidator : AbstractValidator<RetimeStopToHourCommand>
{
    public RetimeStopToHourValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.DayId).NotEmpty();
        RuleFor(x => x.StopId).NotEmpty();
    }
}
