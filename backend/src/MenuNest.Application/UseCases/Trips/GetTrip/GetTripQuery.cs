using Mediator;
using MenuNest.Application.UseCases.Trips;
namespace MenuNest.Application.UseCases.Trips.GetTrip;

public sealed record GetTripQuery(Guid TripId) : IQuery<TripDto>;
