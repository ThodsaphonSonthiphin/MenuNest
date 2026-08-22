using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Categories.SetEverydayMarks;

public sealed class SetEverydayMarksValidator : AbstractValidator<SetEverydayMarksCommand>
{
    public SetEverydayMarksValidator()
    {
        // menunest-184: an empty sheet is not a Budgeting event — reject it
        // outright rather than letting it silently no-op through the handler.
        RuleFor(x => x.Marks).NotEmpty();
        RuleForEach(x => x.Marks).ChildRules(mark =>
        {
            mark.RuleFor(m => m.CategoryId).NotEmpty();
        });
    }
}
