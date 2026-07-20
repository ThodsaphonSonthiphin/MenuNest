using Mediator;

namespace MenuNest.Application.UseCases.Places.ListMyPlaces;

public sealed record ListMyPlacesQuery() : IQuery<IReadOnlyList<DiscoverPlaceDto>>;
