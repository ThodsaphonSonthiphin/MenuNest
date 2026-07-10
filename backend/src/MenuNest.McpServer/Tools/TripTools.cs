using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.CreateTrip;
using MenuNest.Application.UseCases.Trips.DeleteTrip;
using MenuNest.Application.UseCases.Trips.GetTrip;
using MenuNest.Application.UseCases.Trips.ListTrips;
using MenuNest.Application.UseCases.Trips.UpdateTrip;
using MenuNest.Domain.Enums;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class TripTools(IMediator mediator)
{
    [McpServerTool, Description("List all trips owned by the current user")]
    public async Task<IReadOnlyList<TripDto>> list_trips(CancellationToken ct)
        => await mediator.Send(new ListTripsQuery(), ct);

    [McpServerTool, Description("Get one trip by ID")]
    public async Task<TripDto> get_trip(
        [Description("Trip ID")] Guid tripId,
        CancellationToken ct)
        => await mediator.Send(new GetTripQuery(tripId), ct);

    [McpServerTool, Description("Create a trip. Itinerary days are auto-created from dayCount, one per day starting at startDate.")]
    public async Task<TripDto> create_trip(
        [Description("Trip name")] string name,
        [Description("Optional destination")] string? destination,
        [Description("Start date, YYYY-MM-DD")] DateOnly startDate,
        [Description("Number of itinerary days (1 or more)")] int dayCount,
        [Description("Default travel mode new legs inherit: Drive, Walk, or Transit")] TravelMode defaultTravelMode,
        CancellationToken ct)
        => await mediator.Send(new CreateTripCommand(name, destination, startDate, dayCount, defaultTravelMode), ct);

    [McpServerTool, Description("Update a trip's fields. WARNING: lowering dayCount deletes the trailing itinerary days AND their stops (cascade).")]
    public async Task<TripDto> update_trip(
        [Description("Trip ID")] Guid tripId,
        [Description("Trip name")] string name,
        [Description("Optional destination")] string? destination,
        [Description("Start date, YYYY-MM-DD")] DateOnly startDate,
        [Description("Number of itinerary days (1 or more); lowering removes trailing days and their stops")] int dayCount,
        [Description("Default travel mode: Drive, Walk, or Transit")] TravelMode defaultTravelMode,
        CancellationToken ct)
        => await mediator.Send(new UpdateTripCommand(tripId, name, destination, startDate, dayCount, defaultTravelMode), ct);

    [McpServerTool, Description("Delete a trip by ID")]
    public async Task delete_trip(
        [Description("Trip ID")] Guid tripId,
        CancellationToken ct)
        => await mediator.Send(new DeleteTripCommand(tripId), ct);
}
