using FluentValidation;

namespace MenuNest.Application.UseCases.Families.CreateFamily;

public sealed class CreateFamilyValidator : AbstractValidator<CreateFamilyCommand>
{
    public CreateFamilyValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Family name is required.")
            .MaximumLength(120).WithMessage("Family name must be 120 characters or less.");
    }
}
