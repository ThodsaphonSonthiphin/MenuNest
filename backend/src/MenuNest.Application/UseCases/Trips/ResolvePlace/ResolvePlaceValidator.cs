using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;
public sealed class ResolvePlaceValidator : AbstractValidator<ResolvePlaceCommand>
{
    public ResolvePlaceValidator() => RuleFor(x => x.Url).NotEmpty().Must(u =>
        Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
        .WithMessage("Provide a valid Google Maps link.");
}
