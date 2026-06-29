using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.CreateTrip;

public sealed class CreateTripValidator : AbstractValidator<CreateTripCommand>
{
    public CreateTripValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DayCount).InclusiveBetween(1, 60);
    }
}
