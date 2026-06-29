using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.AddStop;
public sealed class AddStopValidator : AbstractValidator<AddStopCommand>
{
    public AddStopValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.DayId).NotEmpty();
        RuleFor(x => x.TripPlaceId).NotEmpty();
        RuleFor(x => x.DwellMinutes).GreaterThan(0);
    }
}
