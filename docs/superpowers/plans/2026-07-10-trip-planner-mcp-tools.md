# Trip Planner over MCP — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose all 17 Trip Planner use cases as MCP tools so Claude can plan and manage trips conversationally, by adding one `TripTools` class to the existing `MenuNest.McpServer`.

**Architecture:** One new `[McpServerToolType]` class, `TripTools(IMediator mediator)`, whose 17 methods are thin passthroughs to the existing Trip CQRS handlers via `IMediator` — identical in shape to the existing `RecipeTools`/`BudgetTools`. Registered with a single `.WithTools<Tools.TripTools>()` line. Auth (Entra ID OAuth, user-scoped), the global `McpToolErrorMapper` filter, DI, and App Service hosting are all reused unchanged. No new use cases, endpoints, entities, EF migrations, Azure resources, or NuGet packages.

**Tech Stack:** C# / .NET 10; `ModelContextProtocol.AspNetCore` 1.0.0 (already referenced); `Mediator` (`IMediator`); xUnit (existing handler tests only).

**Design source:** [docs/superpowers/specs/2026-07-10-trip-planner-mcp-tools-design.md](../specs/2026-07-10-trip-planner-mcp-tools-design.md) · [ADR-034](../../adr/034-trips-exposed-via-mcp-server.md) · [ADR-035](../../adr/035-mcp-place-capture-via-resolve-place.md)

## Global Constraints

Every task's requirements implicitly include this section.

- **Scope lock:** add ONE class `backend/src/MenuNest.McpServer/Tools/TripTools.cs` + ONE registration line. Do **not** add/modify any use case, controller, endpoint, entity, EF migration, DI wiring, Azure resource, or NuGet package.
- **Tool shape:** each tool is `[McpServerTool, Description("…")]`, takes **individual** parameters (construct the Command/Query inside the method body), and returns the handler's DTO — or `Task` (void) for `Unit`-returning commands (like `delete_recipe`). Parameter order: put `CancellationToken ct` last. Match the exact use-case signatures in §5 of the spec.
- **Namespaces:** use **per-file `using`** directives for the Trip use cases (as `ShoppingListTools.cs` does), plus `using MenuNest.Domain.Enums;`. The DTOs (`TripDto`, `TripPlaceDto`, `StopDto`, `ItineraryDayDto`, `ResolvedPlaceDto`, `WeatherPointDto`, `WeatherReadingDto`) live in the `MenuNest.Application.UseCases.Trips` namespace (`TripDtos.cs`). `System.ComponentModel`, `Mediator`, and `ModelContextProtocol.Server` are already in `GlobalUsings.cs` — do not re-import them.
- **Enums render as string member names** over MCP (verified against the SDK) — list the allowed values in each `[Description]`. Exact values: `PlaceCategory` = `Stay, Eat, See, Cafe, Shop, Other`; `TravelMode` = `Drive, Walk, Transit`; `WeatherReadingKind` = `Now, OnArrival`.
- **Type formats in descriptions:** Guid ids are strings; `DateOnly` = `YYYY-MM-DD`; `TimeOnly` = `HH:mm`; `DateTime` = ISO-8601. State the format in the relevant `[Description]`.
- **Canonical vocabulary** (CONTEXT.md): use "Stop", "Leg", "Dwell", "Now"/"On-arrival" — never "waypoint", "segment", "live".
- **Capture rules (ADR-035)** baked into `resolve_place` / `add_trip_place` descriptions: capture is `resolve_place` → `add_trip_place`; use the resolved `lat`/`lng`/`googlePlaceId`, **never invented values**; `resolve_place` returns `category = Other` so Claude must pick the real `PlaceCategory`; to search by name, wrap it as `https://www.google.com/maps/place/<url-encoded name + city>/`; `bestTimeStart`/`bestTimeEnd`/`feeNote`/`notes` are set later via `update_trip_place`.
- **Testing approach:** the tool classes are **thin passthroughs with no logic**; the existing MCP tool classes have **no** unit tests (behavior is covered by the handlers' xUnit tests, which already exist for Trips). Do **not** invent a new test harness. The per-task verification is **`dotnet build` succeeds** — the compiler validates that every Command/Query construction matches the verified signatures (the only thing that can go wrong in a passthrough). A final task does the live MCP smoke test.
- **Build command (per task):** from repo root, `cd backend && dotnet build src/MenuNest.McpServer/MenuNest.McpServer.csproj` → expect `Build succeeded` with `0 Error(s)`.
- **Commits (CLAUDE.md):** every commit references the tracking issue — `(#<ISSUE>)` for partial work, `(closes #<ISSUE>)` on the final code commit. Use conventional-commit `type(scope): summary`. Replace `<ISSUE>` with the number from Task 0 throughout.
- **Staging (CLAUDE.md):** always `git add <explicit paths>` — **never** `git add -A` / `git add .`. `daily-state.md`, `AGENTS.md`, and the pre-existing `CLAUDE.md` working change must never be staged into these commits.
- **Pre-commit hook:** `frontend/.husky/pre-commit` runs the FULL suite on every commit — backend `dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` + `npm run build` (~40s+). Expect the wait; never `--no-verify`.

---

### Task 0: Establish the tracking issue

**Files:** none (GitHub only).

**Interfaces:**
- Produces: `#<ISSUE>` — the GitHub issue number referenced by every commit below.

- [ ] **Step 1: Look for an existing issue**

Run: `gh issue list --search "MCP trip in:title,body" --state open`
(If `gh` returns 401, refresh the User `GH_TOKEN` env var and restart VS Code before retrying.)

- [ ] **Step 2: If none exists, create it**

```bash
gh issue create \
  --title "Trip Planner over MCP — expose 17 trip tools" \
  --body "Add a TripTools class to MenuNest.McpServer exposing all 17 Trip use cases as MCP tools. Design: docs/superpowers/specs/2026-07-10-trip-planner-mcp-tools-design.md (ADR-034, ADR-035)."
```

- [ ] **Step 3: Record the number**

Note the issue number as `<ISSUE>` and substitute it into every commit message in this plan.

---

### Task 1: Commit the design docs

The ADRs, spec, forward-note, and this plan already exist in the working tree; put them on record first so the code commits reference approved decisions.

**Files:**
- Add: `docs/adr/034-trips-exposed-via-mcp-server.md`
- Add: `docs/adr/035-mcp-place-capture-via-resolve-place.md`
- Add: `docs/superpowers/specs/2026-07-10-trip-planner-mcp-tools-design.md`
- Modify: `docs/superpowers/specs/2026-06-02-menunest-mcp-server-design.md` (forward-note)
- Add: `docs/superpowers/plans/2026-07-10-trip-planner-mcp-tools.md` (this file)

**Interfaces:**
- Consumes: `#<ISSUE>` from Task 0.
- Produces: the design docs on the branch.

- [ ] **Step 1: Stage exactly these files**

```bash
git add docs/adr/034-trips-exposed-via-mcp-server.md \
        docs/adr/035-mcp-place-capture-via-resolve-place.md \
        docs/superpowers/specs/2026-07-10-trip-planner-mcp-tools-design.md \
        docs/superpowers/specs/2026-06-02-menunest-mcp-server-design.md \
        docs/superpowers/plans/2026-07-10-trip-planner-mcp-tools.md
```

- [ ] **Step 2: Verify nothing else is staged**

Run: `git status --short`
Expected: only the five docs above appear as staged (`A`/`M`); `CLAUDE.md`, `daily-state.md`, `AGENTS.md` remain **unstaged**.

- [ ] **Step 3: Commit** (the pre-commit gauntlet runs the full suite; it passes since no code changed)

```bash
git commit -m "docs(trips): ADR-034/035 + spec + plan for Trip Planner over MCP (#<ISSUE>)"
```

---

### Task 2: Scaffold `TripTools` + register it + Trips CRUD tools (5)

Create the class with the five trip-level tools and wire the registration. This task proves the whole MCP wiring end-to-end (a registered class that compiles).

**Files:**
- Create: `backend/src/MenuNest.McpServer/Tools/TripTools.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs` (add one `.WithTools<>()` line after `BudgetTools`, line 16, before `.WithRequestFilters(...)`)

**Interfaces:**
- Consumes: existing `ListTripsQuery`, `GetTripQuery(Guid)`, `CreateTripCommand(string, string?, DateOnly, int, TravelMode)`, `UpdateTripCommand(Guid, string, string?, DateOnly, int, TravelMode)`, `DeleteTripCommand(Guid)`; DTO `TripDto`.
- Produces: `public sealed class TripTools(IMediator mediator)` registered in the server; tools `list_trips`, `get_trip`, `create_trip`, `update_trip`, `delete_trip`.

- [ ] **Step 1: Add the registration line (red — type does not exist yet)**

In `backend/src/MenuNest.McpServer/McpServerRegistration.cs`, add the new line immediately after `.WithTools<Tools.BudgetTools>()`:

```csharp
            .WithTools<Tools.BudgetTools>()
            .WithTools<Tools.TripTools>()
            // Translate expected domain/validation exceptions from tools into clean
```

- [ ] **Step 2: Build to confirm it fails**

Run: `cd backend && dotnet build src/MenuNest.McpServer/MenuNest.McpServer.csproj`
Expected: FAIL — `error CS0234`/`CS0246`: the type or namespace name `TripTools` does not exist in `MenuNest.McpServer.Tools`.

- [ ] **Step 3: Create `TripTools.cs` with the five Trips CRUD tools**

Create `backend/src/MenuNest.McpServer/Tools/TripTools.cs`:

```csharp
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
```

- [ ] **Step 4: Build to confirm it passes**

Run: `cd backend && dotnet build src/MenuNest.McpServer/MenuNest.McpServer.csproj`
Expected: PASS — `Build succeeded`, `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/src/MenuNest.McpServer/McpServerRegistration.cs
git commit -m "feat(trips): TripTools MCP class + Trips CRUD tools (#<ISSUE>)"
```

---

### Task 3: Place tools (5) — incl. resolve → add capture

Append the place tools to `TripTools`, encoding the ADR-035 capture rules in the descriptions.

**Files:**
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (add 5 `using`s + 5 methods)

**Interfaces:**
- Consumes: `ResolvePlaceCommand(string)`→`ResolvedPlaceDto`; `ListTripPlacesQuery(Guid)`→`TripPlaceDto[]`; `AddTripPlaceCommand(Guid, string, double, double, PlaceCategory, string?, string?, int?, string?, string?)`→`TripPlaceDto`; `UpdateTripPlaceCommand(Guid, Guid, string, PlaceCategory, string?, string?, string?, TimeOnly?, TimeOnly?)`→`TripPlaceDto`; `DeleteTripPlaceCommand(Guid, Guid)`.
- Produces: tools `resolve_place`, `list_trip_places`, `add_trip_place`, `update_trip_place`, `delete_trip_place`.

- [ ] **Step 1: Add the place-tool `using` directives**

At the top of `TripTools.cs`, add these below the existing Trip `using`s (keep them grouped, before `using MenuNest.Domain.Enums;`):

```csharp
using MenuNest.Application.UseCases.Trips.ResolvePlace;
using MenuNest.Application.UseCases.Trips.ListTripPlaces;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Application.UseCases.Trips.DeleteTripPlace;
```

- [ ] **Step 2: Add the five place tools inside the `TripTools` class**

Add these methods (after `delete_trip`, before the closing brace):

```csharp
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
```

- [ ] **Step 3: Build to confirm it passes**

Run: `cd backend && dotnet build src/MenuNest.McpServer/MenuNest.McpServer.csproj`
Expected: PASS — `Build succeeded`, `0 Error(s)`. (A wrong param order/type in any Command construction fails here.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/TripTools.cs
git commit -m "feat(trips): MCP place tools incl resolve-place capture flow (#<ISSUE>)"
```

---

### Task 4: Itinerary & stop tools (6)

**Files:**
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (add 6 `using`s + 6 methods)

**Interfaces:**
- Consumes: `GetItineraryQuery(Guid, double?, double?)`→`ItineraryDayDto[]`; `AddStopCommand(Guid, Guid, Guid, int, TravelMode)`→`StopDto`; `UpdateStopCommand(Guid, Guid, int?, TravelMode?)`; `RemoveStopCommand(Guid, Guid)`; `ReorderStopsCommand(Guid, Guid, IReadOnlyList<Guid>)`; `SetDayStartTimeCommand(Guid, Guid, TimeOnly)`.
- Produces: tools `get_itinerary`, `add_stop`, `update_stop`, `remove_stop`, `reorder_stops`, `set_day_start_time`.

- [ ] **Step 1: Add the itinerary/stop `using` directives**

At the top of `TripTools.cs`, add:

```csharp
using MenuNest.Application.UseCases.Trips.GetItinerary;
using MenuNest.Application.UseCases.Trips.AddStop;
using MenuNest.Application.UseCases.Trips.UpdateStop;
using MenuNest.Application.UseCases.Trips.RemoveStop;
using MenuNest.Application.UseCases.Trips.ReorderStops;
using MenuNest.Application.UseCases.Trips.SetDayStartTime;
```

- [ ] **Step 2: Add the six itinerary/stop tools inside the `TripTools` class**

```csharp
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
```

- [ ] **Step 3: Build to confirm it passes**

Run: `cd backend && dotnet build src/MenuNest.McpServer/MenuNest.McpServer.csproj`
Expected: PASS — `Build succeeded`, `0 Error(s)`. (`Guid[]` satisfies the `IReadOnlyList<Guid>` parameter of `ReorderStopsCommand`.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/TripTools.cs
git commit -m "feat(trips): MCP itinerary + stop tools (#<ISSUE>)"
```

---

### Task 5: Weather tool (1)

**Files:**
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (add 1 `using` + 1 method)

**Interfaces:**
- Consumes: `GetStopWeatherQuery(WeatherReadingKind, IReadOnlyList<WeatherPointDto>)`→`WeatherReadingDto[]`; `WeatherPointDto(string StopId, double Lat, double Lng, DateTime? ArrivalIso)` (namespace `MenuNest.Application.UseCases.Trips`).
- Produces: tool `get_stop_weather`.

- [ ] **Step 1: Add the weather `using` directive**

At the top of `TripTools.cs`, add:

```csharp
using MenuNest.Application.UseCases.Trips.GetStopWeather;
```

- [ ] **Step 2: Add the weather tool inside the `TripTools` class**

```csharp
    [McpServerTool, Description("Batch weather for stops. kind=Now returns current conditions; kind=OnArrival returns the forecast at each point's arrivalIso. Assemble points from list_trip_places (lat/lng — StopDto has none) + get_itinerary (arrival times). Out-of-range/past/no-coord points return hasData=false, not an error.")]
    public async Task<IReadOnlyList<WeatherReadingDto>> get_stop_weather(
        [Description("Reading kind: Now or OnArrival")] WeatherReadingKind kind,
        [Description("Points to read: each { stopId, lat, lng, arrivalIso? }. arrivalIso is the stop's local wall-clock arrival (ISO-8601), used only for OnArrival.")] WeatherPointDto[] points,
        CancellationToken ct)
        => await mediator.Send(new GetStopWeatherQuery(kind, points), ct);
```

- [ ] **Step 3: Build to confirm it passes**

Run: `cd backend && dotnet build src/MenuNest.McpServer/MenuNest.McpServer.csproj`
Expected: PASS — `Build succeeded`, `0 Error(s)`. (`WeatherPointDto[]` satisfies the `IReadOnlyList<WeatherPointDto>` parameter.)

- [ ] **Step 4: Commit** (final code commit — closes the issue on merge)

```bash
git add backend/src/MenuNest.McpServer/Tools/TripTools.cs
git commit -m "feat(trips): MCP stop-weather batch tool (closes #<ISSUE>)"
```

---

### Task 6: Full gauntlet + deploy + live MCP smoke verification

Verify the whole solution builds & tests green, deploy via the existing CD, then confirm the 17 tools are live and a real call chain works (the ADR-034 acceptance path). No new code unless a defect is found.

**Files:** none (verification), unless a fix is required.

**Interfaces:**
- Consumes: all tools from Tasks 2–5.

- [ ] **Step 1: Run the full backend gauntlet locally**

Run: `cd backend && dotnet build && dotnet test -c Release`
Expected: build succeeds; all tests pass (the pre-commit hook already enforced this per commit — this is a final confirmation across the solution).

- [ ] **Step 2: Deploy**

Merge the branch to `main` (or push per the team's flow). The existing CD pipeline `.github/workflows/main_menunest.yml` builds and deploys `MenuNest.WebApi` (which references `MenuNest.McpServer`) to the App Service. No migration step — nothing new is persisted. Wait for the workflow to go green.

- [ ] **Step 3: Confirm the tools are live**

In Claude → the menunest MCP connector (`https://menunest.azurewebsites.net/mcp`), refresh the tool list.
Expected: the 17 trip tools appear alongside the 49 existing (66 total): `list_trips, get_trip, create_trip, update_trip, delete_trip, resolve_place, list_trip_places, add_trip_place, update_trip_place, delete_trip_place, get_itinerary, add_stop, update_stop, remove_stop, reorder_stops, set_day_start_time, get_stop_weather`.

- [ ] **Step 4: Run the acceptance call chain**

Drive this end-to-end via Claude against the personal-tenant identity:
1. `create_trip(name:"MCP smoke", startDate: <today+7, YYYY-MM-DD>, dayCount:1, defaultTravelMode:"Drive")` → returns a `TripDto` with an `id`.
2. `resolve_place(url:"https://www.google.com/maps/place/Wat+Arun+Bangkok/")` → returns a `ResolvedPlaceDto` with non-zero `lat`/`lng` and a `googlePlaceId`.
3. `add_trip_place(tripId, name:"Wat Arun", lat, lng, category:"See", googlePlaceId, …)` → returns a `TripPlaceDto` with an `id`.
4. `get_itinerary(tripId)` → returns 1 day; note its `dayId`.
5. `add_stop(tripId, dayId, tripPlaceId, dwellMinutes:90, travelModeToReach:"Drive")` → returns a `StopDto`.
6. `get_itinerary(tripId)` → the day now lists the stop with a `legToReach`.
7. `get_stop_weather(kind:"Now", points:[{stopId, lat, lng}])` → returns a reading (`hasData:true` if the key/billing is on, else `false` — both are acceptable, no error).
Expected: each call returns the described shape with no tool error.

- [ ] **Step 5: Error-mapping spot check**

Call `get_trip(tripId: "00000000-0000-0000-0000-000000000000")`.
Expected: a clean tool error (from the global `McpToolErrorMapper` mapping the handler's `DomainException "Trip not found."`), not a raw stack trace — confirming the new class inherits the global filter.

- [ ] **Step 6: Clean up the smoke trip**

Call `delete_trip(tripId)` for the "MCP smoke" trip created in Step 4.
Expected: success; `list_trips` no longer shows it.

---

## Self-Review

**1. Spec coverage:** All 17 tools in spec §5 are implemented (Task 2: 5 trips CRUD; Task 3: 5 places; Task 4: 6 itinerary/stop; Task 5: 1 weather). Registration + GlobalUsings/per-file usings (spec §4, §9) → Task 2 + the `using` steps in Tasks 3–5. Type/enum handling (spec §6) → baked into `[Description]`s and Global Constraints. Capture rules (spec §7, ADR-035) → Task 3 descriptions. Weather assembly (spec §8) → `get_stop_weather` description + Task 6 Step 4. Docs/forward-note (spec §9) → Task 1. Testing approach (spec §10) → Global Constraints + Task 6. Claude config (spec §12) → Task 6 Step 3. No gaps.

**2. Placeholder scan:** No `TBD`/`TODO`/"handle edge cases"/"similar to Task N" — every tool method is written out in full. The only intentional token is `<ISSUE>` (defined in Task 0, substituted throughout) and the concrete date `<today+7>` in the smoke test (a runtime value).

**3. Type consistency:** Command/Query constructor argument orders match the verified signatures exactly (e.g. `AddTripPlaceCommand(tripId, name, lat, lng, category, googlePlaceId, address, priceLevel, photoUrl, openingHoursJson)`; `UpdateStopCommand(tripId, stopId, dwellMinutes?, travelModeToReach?)`). Enum values (`Stay/Eat/See/Cafe/Shop/Other`, `Drive/Walk/Transit`, `Now/OnArrival`) are consistent across descriptions. DTO names (`TripDto`, `TripPlaceDto`, `StopDto`, `ItineraryDayDto`, `ResolvedPlaceDto`, `WeatherReadingDto`) match the `…UseCases.Trips` namespace. `Guid[]`/`WeatherPointDto[]` satisfy the `IReadOnlyList<>` parameters.
