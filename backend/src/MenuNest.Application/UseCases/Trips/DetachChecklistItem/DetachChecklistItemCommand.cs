using Mediator;

namespace MenuNest.Application.UseCases.Trips.DetachChecklistItem;

public sealed record DetachChecklistItemCommand(Guid TripId, Guid PlaceId, Guid EntryId) : ICommand<bool>;