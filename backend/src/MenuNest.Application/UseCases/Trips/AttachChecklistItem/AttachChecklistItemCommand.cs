using Mediator;
using MenuNest.Application.UseCases.Trips;
using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.AttachChecklistItem;

public sealed record AttachChecklistItemCommand(Guid TripId, Guid StopId, string Name) : ICommand<StopChecklistEntryDto>;
