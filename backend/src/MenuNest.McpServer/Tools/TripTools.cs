using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.CreateTrip;
using MenuNest.Application.UseCases.Trips.DeleteTrip;
using MenuNest.Application.UseCases.Trips.GetTrip;
using MenuNest.Application.UseCases.Trips.ListTrips;
using MenuNest.Application.UseCases.Trips.UpdateTrip;
using MenuNest.Application.UseCases.Trips.ResolvePlace;
using MenuNest.Application.UseCases.Trips.ListTripPlaces;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Application.UseCases.Trips.DeleteTripPlace;
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

    [McpServerTool, Description("Resolve a Google Maps link to an authoritative place snapshot from Google (place_id, coordinates, address, opening hours). To search by name, build the URL as https://www.google.com/maps/place/<url-encoded name and city>/. Feed the result into add_trip_place; never fabricate coordinates yourself.")]
    public async Task<ResolvedPlaceDto> resolve_place(
        [Description("A Google Maps URL. To search by name, use https://www.google.com/maps/place/<url-encoded name and city>/")] string url,
        CancellationToken ct)
        => await mediator.Send(new ResolvePlaceCommand(url), ct);

    [McpServerTool, Description("List all saved places of a trip")]
    public async Task<IReadOnlyList<TripPlaceDto>> list_trip_places(
        [Description("Trip ID")] Guid tripId,
        CancellationToken ct)
        => await mediator.Send(new ListTripPlacesQuery(tripId), ct);

    [McpServerTool, Description("Add a saved place to a trip. Use lat/lng/googlePlaceId from resolve_place — do not invent coordinates. resolve_place returns category Other, so choose the real category here. bestTime/feeNote/notes are not set here — use update_trip_place afterward.")]
    public async Task<TripPlaceDto> add_trip_place(
        [Description("Trip ID")] Guid tripId,
        [Description("Place name")] string name,
        [Description("Latitude from resolve_place")] double lat,
        [Description("Longitude from resolve_place")] double lng,
        [Description("Category: Stay, Eat, See, Cafe, Shop, or Other")] PlaceCategory category,
        [Description("Google place_id from resolve_place (optional)")] string? googlePlaceId,
        [Description("Formatted address (optional)")] string? address,
        [Description("Price level 0-4 (optional)")] int? priceLevel,
        [Description("Photo URL (optional; resolve_place returns none)")] string? photoUrl,
        [Description("Raw opening-hours JSON from resolve_place (optional)")] string? openingHoursJson,
        CancellationToken ct)
        => await mediator.Send(new AddTripPlaceCommand(
            tripId, name, lat, lng, category, googlePlaceId, address, priceLevel, photoUrl, openingHoursJson), ct);

    [McpServerTool, Description("Update a saved place's editable fields (name, category, address, fee note, notes, best-visit window)")]
    public async Task<TripPlaceDto> update_trip_place(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        [Description("Place name")] string name,
        [Description("Category: Stay, Eat, See, Cafe, Shop, or Other")] PlaceCategory category,
        [Description("Address (optional)")] string? address,
        [Description("Fee/ticket note (optional)")] string? feeNote,
        [Description("Free-form notes (optional)")] string? notes,
        [Description("Best-visit window start, HH:mm (optional)")] TimeOnly? bestTimeStart,
        [Description("Best-visit window end, HH:mm (optional)")] TimeOnly? bestTimeEnd,
        CancellationToken ct)
        => await mediator.Send(new UpdateTripPlaceCommand(
            tripId, placeId, name, category, address, feeNote, notes, bestTimeStart, bestTimeEnd), ct);

    [McpServerTool, Description("Delete a saved place from a trip by ID")]
    public async Task delete_trip_place(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        CancellationToken ct)
        => await mediator.Send(new DeleteTripPlaceCommand(tripId, placeId), ct);
}
