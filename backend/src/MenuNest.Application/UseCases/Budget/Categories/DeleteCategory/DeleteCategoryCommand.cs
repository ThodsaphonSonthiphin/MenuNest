using Mediator;

namespace MenuNest.Application.UseCases.Budget.Categories.DeleteCategory;

public sealed record DeleteCategoryCommand(Guid Id) : ICommand<Unit>;
