using Mediator;

namespace MenuNest.Application.UseCases.Trips.ListTrips;

public sealed record ListTripsQuery(
    int Skip = 0,
    int Take = 10,
    string? Search = null,
    string? SortColumn = null,
    string? SortDirection = null
) : IQuery<PagedResult<TripDto>>;
