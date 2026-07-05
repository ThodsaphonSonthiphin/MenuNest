using Mediator;
using MenuNest.Application.UseCases.Trips;

namespace MenuNest.Application.UseCases.Trips.GetItinerary;

public sealed record GetItineraryQuery(Guid TripId, double? ViewerLat = null, double? ViewerLng = null)
    : IQuery<IReadOnlyList<ItineraryDayDto>>;
