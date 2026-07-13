using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.AttachChecklistItem;

public sealed class AttachChecklistItemValidator : AbstractValidator<AttachChecklistItemCommand>
{
    public AttachChecklistItemValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.PlaceId).NotEmpty();
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Checklist item name is required.")
            .Must(n => (n ?? string.Empty).Trim().Length <= 100).WithMessage("Checklist item name is too long (max 100).");
    }
}