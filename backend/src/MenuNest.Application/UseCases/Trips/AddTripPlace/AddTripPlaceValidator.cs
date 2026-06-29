using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.AddTripPlace;
public sealed class AddTripPlaceValidator : AbstractValidator<AddTripPlaceCommand>
{
    public AddTripPlaceValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.PriceLevel).InclusiveBetween(0, 4).When(x => x.PriceLevel.HasValue);
    }
}
