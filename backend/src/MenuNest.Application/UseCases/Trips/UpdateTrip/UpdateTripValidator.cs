using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.UpdateTrip;

public sealed class UpdateTripValidator : AbstractValidator<UpdateTripCommand>
{
    public UpdateTripValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DayCount).InclusiveBetween(1, 60);
    }
}
