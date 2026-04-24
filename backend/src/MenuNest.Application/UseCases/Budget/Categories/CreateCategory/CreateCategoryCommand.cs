using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.Categories.CreateCategory;

public sealed record CreateCategoryCommand(
    Guid GroupId, string Name, string? Emoji, int SortOrder,
    BudgetTargetType TargetType, decimal? TargetAmount,
    DateOnly? TargetDueDate, int? TargetDayOfMonth)
    : ICommand<BudgetCategoryDto>;
