using Mediator;
using MenuNest.Application.UseCases.Trips;

namespace MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;

public sealed record SetChecklistEntryCheckedCommand(Guid TripId, Guid PlaceId, Guid EntryId, bool IsChecked)
    : ICommand<PlaceChecklistEntryDto>;