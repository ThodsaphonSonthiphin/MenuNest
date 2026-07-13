using Mediator;
using MenuNest.Application.UseCases.Trips;

namespace MenuNest.Application.UseCases.Trips.AttachChecklistItem;

public sealed record AttachChecklistItemCommand(Guid TripId, Guid PlaceId, string Name)
    : ICommand<PlaceChecklistEntryDto>;