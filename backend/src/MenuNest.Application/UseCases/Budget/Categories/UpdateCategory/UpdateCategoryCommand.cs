using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.Categories.UpdateCategory;

public sealed record UpdateCategoryCommand(
    Guid Id, Guid GroupId, string Name, string? Emoji, int SortOrder,
    BudgetTargetType TargetType, decimal? TargetAmount,
    DateOnly? TargetDueDate, int? TargetDayOfMonth)
    : ICommand<BudgetCategoryDto>;
