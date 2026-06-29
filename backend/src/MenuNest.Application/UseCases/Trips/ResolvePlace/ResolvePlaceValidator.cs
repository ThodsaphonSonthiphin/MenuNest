using FluentValidation;
using MenuNest.Application.Abstractions;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;

public sealed class ResolvePlaceValidator : AbstractValidator<ResolvePlaceCommand>
{
    public ResolvePlaceValidator() => RuleFor(x => x.Url).NotEmpty().Must(GoogleMapsHosts.IsAllowedUrl)
        .WithMessage("Provide a valid Google Maps link.");
}
