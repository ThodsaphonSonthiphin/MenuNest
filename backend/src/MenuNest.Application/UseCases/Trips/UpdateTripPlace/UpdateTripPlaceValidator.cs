using FluentValidation;
namespace MenuNest.Application.UseCases.Trips.UpdateTripPlace;
public sealed class UpdateTripPlaceValidator : AbstractValidator<UpdateTripPlaceCommand>
{
    public UpdateTripPlaceValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.PlaceId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Notes).MaximumLength(2000).WithMessage("Place note is too long (max 2000).");
        RuleFor(x => x.ReviewLinks).NotNull()
            .WithMessage("Review links are required (send an empty array for none).");
        RuleFor(x => x.ReviewLinks).Must(l => l is null || l.Count <= 10)
            .WithMessage("A place can have at most 10 review links.");
        RuleForEach(x => x.ReviewLinks).ChildRules(link =>
        {
            link.RuleFor(l => l.Url).NotEmpty().MaximumLength(500)
                .Must(BeHttpUrl).WithMessage("Review link must be a valid http(s) URL.");
            link.RuleFor(l => l.Label).MaximumLength(80);
        });
        RuleFor(x => x.SeasonPeriods).NotNull()
            .WithMessage("Season periods are required (send an empty array for none).");
        RuleFor(x => x.SeasonPeriods).Must(l => l is null || l.Count <= 12)
            .WithMessage("A place can have at most 12 season periods.");
        RuleForEach(x => x.SeasonPeriods).ChildRules(sp =>
        {
            sp.RuleFor(s => s.Months).NotEmpty().WithMessage("A season period needs at least one month.");
            sp.RuleForEach(s => s.Months).InclusiveBetween(0, 11);
            sp.RuleFor(s => s.Note).MaximumLength(200);
        });
        RuleFor(x => x.BestTimeWindows).NotNull()
            .WithMessage("Best-time windows are required (send an empty array for none).");
        RuleFor(x => x.BestTimeWindows).Must(l => l is null || l.Count <= 6)
            .WithMessage("A place can have at most 6 best-time windows.");
        RuleForEach(x => x.BestTimeWindows).ChildRules(w =>
        {
            w.RuleFor(x => x.End).GreaterThan(x => x.Start)
                .WithMessage("Best-time end must be after start.");
            w.RuleFor(x => x.Note).MaximumLength(200);
        });
    }

    private static bool BeHttpUrl(string? url) =>
        Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
