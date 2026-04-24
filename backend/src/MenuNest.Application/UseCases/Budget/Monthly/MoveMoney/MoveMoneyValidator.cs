using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;

public sealed class MoveMoneyValidator : AbstractValidator<MoveMoneyCommand>
{
    public MoveMoneyValidator()
    {
        RuleFor(x => x.FromCategoryId).NotEmpty();
        RuleFor(x => x.ToCategoryId).NotEmpty()
            .Must((cmd, to) => to != cmd.FromCategoryId)
            .WithMessage("Source and destination must differ.");
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
