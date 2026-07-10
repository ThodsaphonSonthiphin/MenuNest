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
using MenuNest.Application.UseCases.Trips.GetItinerary;
using MenuNest.Application.UseCases.Trips.AddStop;
using MenuNest.Application.UseCases.Trips.UpdateStop;
using MenuNest.Application.UseCases.Trips.RemoveStop;
using MenuNest.Application.UseCases.Trips.ReorderStops;
using MenuNest.Application.UseCases.Trips.SetDayStartTime;
using MenuNest.Application.UseCases.Trips.GetStopWeather;
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

    [McpServerTool, Description("Update a trip's fields (full replace — passing null for destination CLEARS it). WARNING: lowering dayCount deletes the trailing itinerary days AND their stops (cascade).")]
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

    [McpServerTool, Description("Add a saved place to a trip. Use lat/lng/googlePlaceId/priceLevel/openingHoursJson from resolve_place — do not invent coordinates. resolve_place returns category Other, so choose the real category here. bestTime/feeNote/notes are not set here — use update_trip_place afterward.")]
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

    [McpServerTool, Description("Update a saved place's editable fields. FULL REPLACE of the listed fields: address, feeNote, notes, and the best-visit window (bestTimeStart/bestTimeEnd) are overwritten — omitting or passing null for one CLEARS the stored value. To change just one field, pass the current values of the others (get them from list_trip_places).")]
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

    [McpServerTool, Description("Get the trip's itinerary: each day's start time and ordered stops, with each stop's dwell, travel mode, and resolved leg-to-reach (seconds/meters/source). Arrival/leave times and timing flags are NOT included — compute arrivals as dayStart + running sum of (previous leg seconds + previous dwell). viewerLat/viewerLng are for the app's live location and are normally omitted.")]
    public async Task<IReadOnlyList<ItineraryDayDto>> get_itinerary(
        [Description("Trip ID")] Guid tripId,
        [Description("Viewer latitude for the approach leg (optional; usually omit)")] double? viewerLat,
        [Description("Viewer longitude for the approach leg (optional; usually omit)")] double? viewerLng,
        CancellationToken ct)
        => await mediator.Send(new GetItineraryQuery(tripId, viewerLat, viewerLng), ct);

    [McpServerTool, Description("Add a stop to a specific itinerary day. tripPlaceId must be a place already saved on the trip (see list_trip_places / add_trip_place). dayId comes from get_itinerary.")]
    public async Task<StopDto> add_stop(
        [Description("Trip ID")] Guid tripId,
        [Description("Itinerary day ID (from get_itinerary)")] Guid dayId,
        [Description("Saved place ID to visit")] Guid tripPlaceId,
        [Description("Dwell — minutes planned at the stop")] int dwellMinutes,
        [Description("Travel mode to reach this stop: Drive, Walk, or Transit")] TravelMode travelModeToReach,
        CancellationToken ct)
        => await mediator.Send(new AddStopCommand(tripId, dayId, tripPlaceId, dwellMinutes, travelModeToReach), ct);

    [McpServerTool, Description("Update a stop's dwell and/or travel mode. Omit a field to leave it unchanged.")]
    public async Task update_stop(
        [Description("Trip ID")] Guid tripId,
        [Description("Stop ID")] Guid stopId,
        [Description("New dwell in minutes (optional)")] int? dwellMinutes,
        [Description("New travel mode to reach: Drive, Walk, or Transit (optional)")] TravelMode? travelModeToReach,
        CancellationToken ct)
        => await mediator.Send(new UpdateStopCommand(tripId, stopId, dwellMinutes, travelModeToReach), ct);

    [McpServerTool, Description("Remove a stop from its day by ID")]
    public async Task remove_stop(
        [Description("Trip ID")] Guid tripId,
        [Description("Stop ID")] Guid stopId,
        CancellationToken ct)
        => await mediator.Send(new RemoveStopCommand(tripId, stopId), ct);

    [McpServerTool, Description("Reorder all stops in a day. Provide every stop ID of that day in the desired order (get the current set from get_itinerary).")]
    public async Task reorder_stops(
        [Description("Trip ID")] Guid tripId,
        [Description("Itinerary day ID")] Guid dayId,
        [Description("All stop IDs of the day, in the new order")] Guid[] orderedStopIds,
        CancellationToken ct)
        => await mediator.Send(new ReorderStopsCommand(tripId, dayId, orderedStopIds), ct);

    [McpServerTool, Description("Set an itinerary day's start time, from which the schedule cascades. Time is HH:mm (24h).")]
    public async Task set_day_start_time(
        [Description("Trip ID")] Guid tripId,
        [Description("Itinerary day ID")] Guid dayId,
        [Description("Day start time, HH:mm (24h)")] TimeOnly startTime,
        CancellationToken ct)
        => await mediator.Send(new SetDayStartTimeCommand(tripId, dayId, startTime), ct);

    [McpServerTool, Description("Batch weather for stops. kind=Now returns current conditions; kind=OnArrival returns the forecast at each point's arrivalIso. Assemble points from list_trip_places (lat/lng — StopDto has none) + get_itinerary (arrival times). Points outside the forecast window / in the past / with no coords return hasData=false rather than erroring (but lat/lng outside valid ranges are rejected).")]
    public async Task<IReadOnlyList<WeatherReadingDto>> get_stop_weather(
        [Description("Reading kind: Now or OnArrival")] WeatherReadingKind kind,
        [Description("Points to read: each { stopId, lat, lng, arrivalIso? }. arrivalIso is the stop's local wall-clock arrival (ISO-8601), used only for OnArrival.")] WeatherPointDto[] points,
        CancellationToken ct)
        => await mediator.Send(new GetStopWeatherQuery(kind, points), ct);
}
