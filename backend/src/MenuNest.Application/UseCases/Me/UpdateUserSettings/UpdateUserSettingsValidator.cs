using FluentValidation;

namespace MenuNest.Application.UseCases.Me.UpdateUserSettings;

public sealed class UpdateUserSettingsValidator : AbstractValidator<UpdateUserSettingsCommand>
{
    public UpdateUserSettingsValidator()
    {
        // HomePath is optional (null clears it). Route validity is enforced
        // client-side against the home-eligible allowlist (ADR-084); the
        // server only bounds the length to match the column.
        RuleFor(x => x.HomePath)
            .MaximumLength(100).WithMessage("HomePath must be 100 characters or less.");
        RuleFor(x => x.UvWarnThreshold).InclusiveBetween(0, 15)
            .When(x => x.UvWarnThreshold.HasValue).WithMessage("UvWarnThreshold must be 0..15.");
        RuleFor(x => x.FeelsLikeWarnThreshold).InclusiveBetween(0, 60)
            .When(x => x.FeelsLikeWarnThreshold.HasValue).WithMessage("FeelsLikeWarnThreshold must be 0..60.");
    }
}
