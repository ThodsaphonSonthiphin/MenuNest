using FluentValidation;

namespace MenuNest.Application.UseCases.Health.Share.CreateShareLink;

public sealed class CreateShareLinkValidator : AbstractValidator<CreateShareLinkCommand>
{
    public CreateShareLinkValidator()
    {
        RuleFor(x => x.DateTo)
            .GreaterThanOrEqualTo(x => x.DateFrom)
            .WithMessage("DateTo must be on or after DateFrom.");

        RuleFor(x => x.ValidForDays)
            .InclusiveBetween(1, 90)
            .WithMessage("ValidForDays must be between 1 and 90.");
    }
}
