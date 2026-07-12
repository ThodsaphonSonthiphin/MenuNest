using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.UpdateTripPlace;
public sealed class UpdateTripPlaceValidator : AbstractValidator<UpdateTripPlaceCommand>
{
    public UpdateTripPlaceValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.PlaceId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.ReviewLinks).Must(l => l.Count <= 10)
            .WithMessage("A place can have at most 10 review links.");
        RuleForEach(x => x.ReviewLinks).ChildRules(link =>
        {
            link.RuleFor(l => l.Url).NotEmpty().MaximumLength(500)
                .Must(BeHttpUrl).WithMessage("Review link must be a valid http(s) URL.");
            link.RuleFor(l => l.Label).MaximumLength(80);
        });
    }

    private static bool BeHttpUrl(string? url) =>
        Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
