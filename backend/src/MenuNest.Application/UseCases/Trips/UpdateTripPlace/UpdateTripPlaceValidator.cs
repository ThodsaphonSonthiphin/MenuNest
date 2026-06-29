using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.UpdateTripPlace;
public sealed class UpdateTripPlaceValidator : AbstractValidator<UpdateTripPlaceCommand>
{
    public UpdateTripPlaceValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.PlaceId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
    }
}
