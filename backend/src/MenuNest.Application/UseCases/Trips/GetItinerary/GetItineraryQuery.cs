using Mediator;
using MenuNest.Application.UseCases.Trips;
namespace MenuNest.Application.UseCases.Trips.GetItinerary;
public sealed record GetItineraryQuery(Guid TripId) : IQuery<IReadOnlyList<ItineraryDayDto>>;
