using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;

public sealed class UpdateWritingEntryTextValidator : AbstractValidator<UpdateWritingEntryTextCommand>
{
    public UpdateWritingEntryTextValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(50_000);
    }
}
