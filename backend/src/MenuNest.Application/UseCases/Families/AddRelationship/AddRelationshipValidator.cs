using FluentValidation;

namespace MenuNest.Application.UseCases.Families.AddRelationship;

public sealed class AddRelationshipValidator : AbstractValidator<AddRelationshipCommand>
{
    public AddRelationshipValidator()
    {
        RuleFor(x => x.FromUserId).NotEmpty().WithMessage("From user is required.");
        RuleFor(x => x.ToUserId).NotEmpty().WithMessage("To user is required.");
        RuleFor(x => x.RelationType).IsInEnum().WithMessage("Invalid relationship type.");
        RuleFor(x => x)
            .Must(x => x.FromUserId != x.ToUserId)
            .WithMessage("Cannot create a relationship between the same person.");
    }
}
