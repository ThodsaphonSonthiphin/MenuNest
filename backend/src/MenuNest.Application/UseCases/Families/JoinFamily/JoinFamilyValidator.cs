using FluentValidation;

namespace MenuNest.Application.UseCases.Families.JoinFamily;

public sealed class JoinFamilyValidator : AbstractValidator<JoinFamilyCommand>
{
    public JoinFamilyValidator()
    {
        RuleFor(x => x.InviteCode)
            .NotEmpty().WithMessage("Invite code is required.")
            .Matches(@"^[A-Z0-9]{4}-[A-Z0-9]{4}$").WithMessage("Invite code must be formatted as XXXX-XXXX.");
    }
}
