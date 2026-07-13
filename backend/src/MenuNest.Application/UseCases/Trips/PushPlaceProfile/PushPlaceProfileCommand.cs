using Mediator;
namespace MenuNest.Application.UseCases.Trips.PushPlaceProfile;

public sealed record PushPlaceProfileCommand(Guid TripId, Guid PlaceId) : ICommand<TripPlaceDto>;