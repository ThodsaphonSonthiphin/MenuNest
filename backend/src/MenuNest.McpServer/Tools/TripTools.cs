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
using MenuNest.Application.UseCases.Trips.PushPlaceProfile;
using MenuNest.Application.UseCases.Trips.DeleteTripPlace;
using MenuNest.Application.UseCases.Trips.GetItinerary;
using MenuNest.Application.UseCases.Trips.AddStop;
using MenuNest.Application.UseCases.Trips.UpdateStop;
using MenuNest.Application.UseCases.Trips.RemoveStop;
using MenuNest.Application.UseCases.Trips.ReorderStops;
using MenuNest.Application.UseCases.Trips.SetDayStartTime;
using MenuNest.Application.UseCases.Trips.SetDayUseCurrentTime;
using MenuNest.Application.UseCases.Trips.GetStopWeather;
using MenuNest.Application.UseCases.Trips.GetStopHourlyForecast;
using MenuNest.Application.UseCases.Trips.RetimeStopToWeather;
using MenuNest.Application.UseCases.Trips.ListChecklistItems;
using MenuNest.Application.UseCases.Trips.AttachChecklistItem;
using MenuNest.Application.UseCases.Trips.DetachChecklistItem;
using MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;
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

    [McpServerTool, Description("Update a saved place's editable fields. FULL REPLACE of the listed fields: address, feeNote, notes, the best-visit window (bestTimeStart/bestTimeEnd), reviewLinks, and seasonPeriods are overwritten — omitting or passing an empty/null value CLEARS the stored value. To change just one field, pass the current values of the others (get them from list_trip_places). The notes and reviewLinks also propagate to the user's saved master profile for this place and appear on Discover immediately (no push needed). CAUTION: notes/reviewLinks are shared per Google place across ALL of the user's trips that reference it — this save is last-write-wins, so a save from any trip (even one only changing an unrelated field, like category, if that trip's own notes/reviewLinks happen to be empty) overwrites the shared value used by Discover. Always send the intended CURRENT notes/reviewLinks for this place, not blank placeholders.")]
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
        [Description("Review links (TikTok/YouTube/etc.), each {url,label?}; max 10; FULL REPLACE")] IReadOnlyList<ReviewLinkDto> reviewLinks,
        [Description("Season periods, each { kind: 'Good'|'Bad', months: int[] 0-11 (0=Jan), note?: string }; max 12; FULL REPLACE — omitting or passing an empty array CLEARS all seasons.")] IReadOnlyList<SeasonPeriodDto> seasonPeriods,
        CancellationToken ct)
        => await mediator.Send(new UpdateTripPlaceCommand(
            tripId, placeId, name, category, address, feeNote, notes, bestTimeStart, bestTimeEnd, reviewLinks, seasonPeriods), ct);

    [McpServerTool, Description("Push the current per-trip enrichment of a saved place UP to the user's master place-profile (FULL overwrite of the master: best-time window, review links, notes, checklist item-set, AND season periods), so future captures of the same Google place start from it. Shape the place with update_trip_place FIRST, then push. Returns the place.")]
    public async Task<TripPlaceDto> push_place_profile(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        CancellationToken ct)
        => await mediator.Send(new PushPlaceProfileCommand(tripId, placeId), ct);

    [McpServerTool, Description("Delete a saved place from a trip by ID")]
    public async Task delete_trip_place(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        CancellationToken ct)
        => await mediator.Send(new DeleteTripPlaceCommand(tripId, placeId), ct);

    [McpServerTool, Description("Get the trip's itinerary: each day's start time and ordered stops, with each stop's dwell, travel mode, and resolved leg-to-reach (seconds/meters/source). For a day set to 'always start from the current time', dayStart is resolved using timeZoneId when supplied. Arrival/leave times and timing flags are NOT included — compute arrivals as dayStart + running sum of (previous leg seconds + previous dwell). viewerLat/viewerLng are for the app's live location and are normally omitted.")]
    public async Task<IReadOnlyList<ItineraryDayDto>> get_itinerary(
        [Description("Trip ID")] Guid tripId,
        [Description("The user's IANA time zone, e.g. Asia/Bangkok. Prefer to always pass it whenever you know the user's zone; it is strictly required only when a day is set to 'always start from the current time' (a call that omits it then fails, and you cannot tell in advance which trips have such a day).")] string? timeZoneId,
        [Description("Viewer latitude for the approach leg (optional; usually omit)")] double? viewerLat,
        [Description("Viewer longitude for the approach leg (optional; usually omit)")] double? viewerLng,
        CancellationToken ct)
        => await mediator.Send(new GetItineraryQuery(tripId, timeZoneId, viewerLat, viewerLng), ct);

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

    [McpServerTool, Description("Toggle whether an itinerary day's start time always tracks the current real-world clock time (re-evaluated on every get_itinerary call) instead of a fixed picked time.")]
    public async Task set_day_use_current_time(
        [Description("Trip ID")] Guid tripId,
        [Description("Itinerary day ID")] Guid dayId,
        [Description("True to always start the day's schedule from the current time; false to use the last picked start time")] bool useCurrentTime,
        CancellationToken ct)
        => await mediator.Send(new SetDayUseCurrentTimeCommand(tripId, dayId, useCurrentTime), ct);

    [McpServerTool, Description("Batch weather for stops. kind=Now returns current conditions; kind=OnArrival returns the forecast at each point's arrivalIso. Assemble points from list_trip_places (lat/lng — StopDto has none) + get_itinerary (arrival times). Points outside the forecast window / in the past / with no coords return hasData=false rather than erroring (but lat/lng outside valid ranges are rejected).")]
    public async Task<IReadOnlyList<WeatherReadingDto>> get_stop_weather(
        [Description("Reading kind: Now or OnArrival")] WeatherReadingKind kind,
        [Description("Points to read: each { stopId, lat, lng, arrivalIso? }. arrivalIso is the stop's local wall-clock arrival (ISO-8601), used only for OnArrival.")] WeatherPointDto[] points,
        CancellationToken ct)
        => await mediator.Send(new GetStopWeatherQuery(kind, points), ct);

    [McpServerTool, Description("Hourly weather forecast for a stop's location (up to 240h). Each hour: local time, feels-like, temp, condition, isDaytime.")]
    public async Task<IReadOnlyList<HourlyReadingDto>> get_stop_hourly_forecast(
        [Description("Trip ID")] Guid tripId,
        [Description("Stop ID")] Guid stopId,
        [Description("Forecast hours to return (1-240)")] int hours,
        CancellationToken ct)
        => await mediator.Send(new GetStopHourlyForecastQuery(tripId, stopId, hours), ct);

    [McpServerTool, Description("Re-time the plan so a stop arrives at a target hour, or the coolest daytime/nighttime hour. Shifts the day start (and whole trip StartDate for a cross-day target); turns off the day's current-time-start. The resulting day-start assumes inter-stop travel only — no approach leg from a live location — so a user later opening the trip in-app with location enabled may see the anchor arrive slightly later than the targeted hour by their travel time to the first stop. Returns whether the whole trip moved.")]
    public async Task<RetimeResultDto> retime_stop_to_weather(
        [Description("Trip ID")] Guid tripId,
        [Description("Itinerary day ID of the anchor stop")] Guid dayId,
        [Description("Anchor stop ID")] Guid stopId,
        [Description("Target: { kind: 'hour'|'coolestDaytime'|'coolestNighttime', localDateTime?, windowHours? }. For coolestDaytime/coolestNighttime, windowHours (default 48) is counted from the CURRENT time, not the anchor stop's scheduled day; for a stop scheduled far in the future this may resolve to a near-term date and shift the whole trip's dates (ADR-114).")] RetimeTarget target,
        CancellationToken ct)
        => await mediator.Send(new RetimeStopToWeatherCommand(tripId, dayId, stopId, target), ct);

    [McpServerTool, Description("List the current user's reusable checklist items — the personal library of 'things to prepare/bring' reused across places and trips. Use these names with attach_checklist_item.")]
    public async Task<IReadOnlyList<ChecklistItemDto>> list_checklist_items(CancellationToken ct)
        => await mediator.Send(new ListChecklistItemsQuery(), ct);

    [McpServerTool, Description("Attach a checklist item to a place BY NAME. Create-or-reuse: an existing library item of the same name (case-insensitive) for this user is reused; a new name creates one. Idempotent per (place,item). Returns the place checklist entry.")]
    public async Task<PlaceChecklistEntryDto> attach_checklist_item(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        [Description("Checklist item name, e.g. ร่ม / passport / sunscreen")] string name,
        CancellationToken ct)
        => await mediator.Send(new AttachChecklistItemCommand(tripId, placeId, name), ct);

    [McpServerTool, Description("Detach a checklist item from a place. Removes the place's entry ONLY; the reusable library item survives for other places.")]
    public async Task<bool> detach_checklist_item(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        [Description("Place checklist entry ID (from the place's checklist)")] Guid entryId,
        CancellationToken ct)
        => await mediator.Send(new DetachChecklistItemCommand(tripId, placeId, entryId), ct);

    [McpServerTool, Description("Set the per-place checked ('prepared') state of a place checklist entry. Checked is independent per place.")]
    public async Task<PlaceChecklistEntryDto> set_checklist_item_checked(
        [Description("Trip ID")] Guid tripId,
        [Description("Place ID")] Guid placeId,
        [Description("Place checklist entry ID")] Guid entryId,
        [Description("true = prepared/checked, false = not yet")] bool isChecked,
        CancellationToken ct)
        => await mediator.Send(new SetChecklistEntryCheckedCommand(tripId, placeId, entryId, isChecked), ct);
}
