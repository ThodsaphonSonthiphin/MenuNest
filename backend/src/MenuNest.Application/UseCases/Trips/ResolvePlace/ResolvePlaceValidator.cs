using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;
public sealed class ResolvePlaceValidator : AbstractValidator<ResolvePlaceCommand>
{
    private static readonly HashSet<string> AllowedHosts =
        new(StringComparer.OrdinalIgnoreCase)
        { "maps.app.goo.gl", "goo.gl", "maps.google.com", "www.google.com", "google.com", "g.co" };

    private static bool IsGoogleMapsHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || uri.Scheme == "http")
        && (AllowedHosts.Contains(uri.Host) || uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase));

    public ResolvePlaceValidator() => RuleFor(x => x.Url).NotEmpty().Must(IsGoogleMapsHost)
        .WithMessage("Provide a valid Google Maps link.");
}
