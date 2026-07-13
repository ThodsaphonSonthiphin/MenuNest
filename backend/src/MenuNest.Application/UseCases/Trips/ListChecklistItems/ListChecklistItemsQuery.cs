using Mediator;
using MenuNest.Application.UseCases.Trips;

namespace MenuNest.Application.UseCases.Trips.ListChecklistItems;

public sealed record ListChecklistItemsQuery : IQuery<IReadOnlyList<ChecklistItemDto>>;