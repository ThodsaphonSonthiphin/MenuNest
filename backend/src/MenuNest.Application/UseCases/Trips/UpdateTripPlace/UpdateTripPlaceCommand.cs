using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.UpdateTripPlace;
public sealed record UpdateTripPlaceCommand(
    Guid TripId, Guid PlaceId, string Name, PlaceCategory Category,
    string? Address, string? FeeNote, string? Notes,
    TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd)
    : ICommand<TripPlaceDto>;
