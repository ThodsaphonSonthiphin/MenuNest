using Mediator;
namespace MenuNest.Application.UseCases.Trips.DeleteTripPlace;
public sealed record DeleteTripPlaceCommand(Guid TripId, Guid PlaceId) : ICommand<Unit>;
