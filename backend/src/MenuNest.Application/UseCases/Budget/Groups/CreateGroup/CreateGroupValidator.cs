using FluentValidation;

namespace MenuNest.Application.UseCases.Budget.Groups.CreateGroup;

public sealed class CreateGroupValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
    }
}
