using Mediator;

namespace MenuNest.Application.UseCases.Trips.ListTrips;

public sealed record ListTripsQuery() : IQuery<IReadOnlyList<TripDto>>;
