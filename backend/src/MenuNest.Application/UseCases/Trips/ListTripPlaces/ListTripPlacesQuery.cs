using Mediator;
namespace MenuNest.Application.UseCases.Trips.ListTripPlaces;
public sealed record ListTripPlacesQuery(Guid TripId) : IQuery<IReadOnlyList<TripPlaceDto>>;
