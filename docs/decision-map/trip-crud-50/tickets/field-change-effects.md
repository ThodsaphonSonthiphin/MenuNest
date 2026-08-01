---
title: Field change effects - what does editing each Trip field actually destroy or silently alter?
type: research
mode: AFK
status: closed
assignee: 
blocked_by: []
gist: Shrinking dayCount hard-deletes the trailing days and DB-cascades their Stops (IsVisited, notes, dwell, day start time, use-current-time all go, unrecoverably); checklist entries, TripPlaces and profiles survive. A startDate move destroys nothing but silently re-derives weather, season and opening-hours flags. defaultTravelMode re-costs nothing and only affects new stops. TripDetailPage can compute the at-risk stop count from cache it already holds; TripsPage cannot - TripDto carries nothing below the trip row.
---

## Question

For each field an edit surface would expose (name, destination, startDate, dayCount, defaultTravelMode), enumerate precisely what changing it does to existing trip data today. Read UpdateTripHandler, DayRealigner, Trip.Reschedule, the ItineraryDay to Stop FK cascade, and every consumer of a day's date. Answer at minimum: (a) which stops, visited flags, day start times, use-current-time flags, checklist entries, and weather or route cache are destroyed or silently altered when dayCount shrinks; (b) what a startDate move does to the days that are kept, and to anything keyed on a date; (c) whether changing defaultTravelMode re-costs existing legs or only affects new ones; (d) whether the SPA can compute the at-risk stop count from data it already holds on the trip detail page, and separately on the trips list page.

<!-- decision-map:resolution:start -->
## Resolution

Shrinking dayCount hard-deletes the trailing days and DB-cascades their Stops (IsVisited, notes, dwell, day start time, use-current-time all go, unrecoverably); checklist entries, TripPlaces and profiles survive. A startDate move destroys nothing but silently re-derives weather, season and opening-hours flags. defaultTravelMode re-costs nothing and only affects new stops. TripDetailPage can compute the at-risk stop count from cache it already holds; TripsPage cannot - TripDto carries nothing below the trip row.

Resolved AFK by a research subagent reading the repo at `c:\Repo2\t\menunest` (read-only). Every claim below is cited to a file and line; the "Uncertainties" section at the end is load-bearing — treat it as the boundary of what this ticket actually establishes.

## Orientation

A full backend edit path already exists and is reachable in prod: `PUT /api/trips/{id}` -> `UpdateTripHandler` (`backend/src/MenuNest.WebApi/Controllers/TripsController.cs:52-54`). The SPA already calls it, but only from `TripDateEditor`, which sends **startDate only** and carries every other field through unchanged (`frontend/src/pages/trips/components/TripDateEditor.tsx:75-82`). So a `dayCount` shrink is **currently unreachable from the SPA** but fully reachable from MCP (`backend/src/MenuNest.McpServer/Tools/TripTools.cs:57-65`).

`deleteTrip` is wired in RTK Query (`frontend/src/shared/api/api.ts:1371-1374`, hook exported at `:1678`) with **zero call sites** in `frontend/src`.

## (a) What a dayCount shrink destroys

### UpdateTripHandler, statement by statement

| Line | Statement | Effect on persisted rows |
|---|---|---|
| 22 | `ValidateAndThrowAsync` | Only `TripId` non-empty, `Name` non-empty/<=200, `DayCount` in `[1,60]` (`UpdateTripValidator.cs:9-12`). **No guard on shrinking at all, and no `Destination` rule.** |
| 24-26 | Load trip scoped by `UserId` and `DeletedAt == null` | Miss -> `DomainException("Trip not found.")` |
| 28 | `trip.UpdateDetails(...)` | `Trips` row only: `Name` (trimmed), `Destination = destination?.Trim()` — **full replace, a null clears it** (`Trip.cs:53-59`), `DefaultTravelMode`, `UpdatedAt` |
| 29 | `trip.Reschedule(...)` | `StartDate`, `DayCount`, `UpdatedAt` (`Trip.cs:61-69`). Throws if `dayCount < 1`, or if `IsDaily && dayCount > 1` — **a daily trip cannot be extended at all** |
| 31-34 | `ItineraryDays.Where(TripId).OrderBy(Date).ToList()` | Reads the **actual DB day rows**, not `trip.DayCount`. If the two ever drifted, the DB rows win |
| 37-38 | add missing trailing days | New days get `DayStartTime = 09:00`, `UseCurrentTimeAsStart = false` (`ItineraryDay.cs:19`, `:12`) — asymmetric with `CreateTripHandler.cs:27`, which sets the flag true for daily trips |
| **44-45** | **`foreach (var extra in days.Skip(c.DayCount)) _db.ItineraryDays.Remove(extra)`** | **The destruction.** `days` is `OrderBy(Date)`, so it drops the **latest-dated** days |
| 47-49 | `DayRealigner.RealignDays(...)` | Kept days keep their **row identity**; only `Date` and `UpdatedAt` change (`DayRealigner.cs:19-20`) |
| 51 | one `SaveChangesAsync` | Deletes + updates + inserts in a single EF batch, so the unique `(TripId, Date)` index is never transiently violated |

### The cascade — verified against configuration, not the comment

Two independent places, and they **agree**:

```
backend/src/MenuNest.Infrastructure/Persistence/Configurations/StopConfiguration.cs:22
b.HasOne<ItineraryDay>().WithMany().HasForeignKey(s => s.ItineraryDayId).OnDelete(DeleteBehavior.Cascade);
```

```
backend/src/MenuNest.Infrastructure/Persistence/Migrations/20260629104508_TripsInitial.cs:111-116
onDelete: ReferentialAction.Cascade
```

The handler never loads the `Stop` rows, so EF's client-side cascade cannot fire — deletion happens entirely via the **database** `ON DELETE CASCADE`. The MCP tool description already warns about it (`TripTools.cs:56`).

### Per-artifact verdict when a day is dropped

Every FK into `ItineraryDay` and into `Stop` was enumerated across all 38 `Configurations/*.cs`. **`Stop` is the only entity with an FK to `ItineraryDay`, and nothing anywhere has an FK to `Stop`.** Blast radius:

| Artifact | Verdict | Evidence |
|---|---|---|
| Stops on dropped days | **DESTROYED** (hard DELETE, no soft-delete, no undo) | `StopConfiguration.cs:22` + migration `:116` |
| `Stop.IsVisited` ("มาแล้ว") | **DESTROYED** — a column on the deleted row | `Stop.cs:21` |
| `Stop.Notes`, `DwellMinutes`, `Sequence`, `TravelModeToReach` | **DESTROYED** with the row | `Stop.cs:15-21` |
| `ItineraryDay.DayStartTime` on dropped days | **DESTROYED** | `ItineraryDay.cs:11` |
| `ItineraryDay.UseCurrentTimeAsStart` on dropped days | **DESTROYED** | `ItineraryDay.cs:12` |
| Checklist entries (`PlaceChecklistEntry`) | **UNTOUCHED** — they hang off `TripPlace` | `PlaceChecklistEntryConfiguration.cs:20` |
| Library `ChecklistItem` | **UNTOUCHED** — explicitly `Restrict` | `PlaceChecklistEntryConfiguration.cs:22` |
| `TripPlace` rows (the place pool) | **UNTOUCHED** — `Stop -> TripPlace` is `NoAction` | `StopConfiguration.cs:23` |
| `PlaceProfile` / season / best-time / review links | **UNTOUCHED** | `PlaceProfileConfiguration.cs:75`, `TripPlaceConfiguration.cs:80` |
| Route/leg cache | **UNTOUCHED and irrelevant** — legs are never persisted | see (c) |
| Weather cache | **UNTOUCHED and irrelevant** — in-memory only | see (c) |
| Orphans | **NONE possible** — no table references `Stop` | grep confirmed |

Semantics are **index-preserving**: days 1..N survive with all their stops; days N+1..old survive not at all.

### Test-coverage gap, relevant to a data-safety decision

**No test anywhere seeds a `Stop` and then shrinks the trip.** `UpdateTripHandlerTests.cs:21-25` and `UpdateTripHandlerRelationalTests.cs:59-68` seed only bare `ItineraryDay`s. The five relational tests lock in date-realignment collision safety, not cascade behaviour — the cascade is asserted by the schema and by nothing else in CI.

Secondary gap: **extend + backward shift is untested** (`Extend_with_forward_shift_realigns_and_adds_trailing_days` covers forward only).

## (b) What a startDate move does

`DayRealigner.RealignDays` mutates **only `Date` and `UpdatedAt`**, on the same row objects (`DayRealigner.cs:19-20`). Because row identity is preserved:

- **Stops do not move between days** and no `Stop` row is touched (`Stop` has no date column — arrival/leave are derived, `Stop.cs:11`)
- **`IsVisited` survives** untouched
- **`DayStartTime` survives** untouched
- **`UseCurrentTimeAsStart` survives** untouched. For a single-day trip with the flag on, `GetItineraryHandler.cs:105-107` overrides the returned date with the viewer's local today anyway, so the persisted move is **invisible in the UI** — the SPA already disables the picker in that state (`TripDateEditor.tsx:95,109`)
- The unique `(TripId, Date)` index (`ItineraryDayConfiguration.cs:18`) is why the realign must be one `SaveChanges`; locked in by `UpdateTripHandlerRelationalTests.cs:78-115`

Nothing date-keyed is destroyed, but several things **silently change value**:

| Consumer | Effect |
|---|---|
| Weather (on-arrival) — `useStopWeather.ts:30`, `weather.ts:34` | Arrival instants shift; the cache key changes (`GoogleWeatherService.cs:237-241`), so old readings are not reused |
| Weather 240h horizon gate — `weather.ts:8-12,57-58` | A move **into the past** flips stops to `'past'`; **beyond 10 days** flips to `'beyond'` — both render No-data. A working weather display silently degrades from a date change alone |
| Season warning — `ItineraryTab.tsx:204` | Crossing a month boundary can add or remove a good/avoid-season warning on untouched stops |
| Day-of-week flags — `useSchedule.ts:166` | Opening-hours flags recompute; a stop fine on a Tuesday can become a "closed" problem flag on a Sunday |
| `HourlyPlanner` — `HourlyPlanner.tsx:45,79` | Rebases against the new date |
| Route/leg cache | **Unaffected** — no date in the key (`GoogleRouteService.cs:97-98`) |
| `RetimeStopToHourHandler` | The other writer of `Trip.StartDate`, same `DayRealigner`. It has a **past-date guard** (`:40-41`) that `UpdateTripHandler` does **not** — UpdateTrip will happily move a trip into the past |

## (c) defaultTravelMode change

**It re-costs nothing.** It affects only stops added afterwards, plus two display-only derivations.

Backend: written at `Trip.cs:41`/`:57`, echoed into `TripDto` by every trip handler, and **read nowhere else** — it appears nowhere in `GetItineraryHandler`, nowhere in `AddStopHandler`, and nowhere in either route service. Leg cost is driven by the **per-Stop** mode (`GetItineraryHandler.cs:71`), and `AddStopHandler.cs:31` takes the mode from the command.

The trip default is applied purely **client-side at add time**: `ItineraryTab.tsx:109` and `TripDetailPage.tsx:73`.

- **Existing legs: left stale — not recomputed, not invalidated, not marked.** There is no persisted leg; `LegDto` is built fresh per request (`GetItineraryHandler.cs:94-96`) and the only cache is a 12-hour in-memory one keyed on `(origin, dest, mode)` (`GoogleRouteService.cs:97-98`), so existing stops keep hitting their own mode's entry
- **New stops inherit the new mode**
- Two visible-but-cosmetic effects, both in `ItineraryTab`: the Google Maps deep-link travel mode changes (`:189`), and **the "mixed mode" banner can flip on for an itinerary nobody touched** (`:190`), because every existing stop now disagrees with the new default

## (d) Can the SPA compute the at-risk stop count?

### TripDetailPage — YES, with no additional fetch

`TripDetailPage.tsx:64-65` calls `useDayRoute(tripId)` unconditionally, before the not-found guard, and `useDayRoute.ts:75-78` fires the itinerary query with only an empty-id skip. It deliberately re-exports the day list for reuse (`useDayRoute.ts:167-170`).

`ItineraryDayDto` carries the full per-day stop array **including `isVisited`** (`api.ts:535-536`, `TripDtos.cs:32-37`), so an edit dialog opened from the detail page can compute from cache alone: `days.slice(newDayCount).reduce((n, d) => n + d.stops.length, 0)`, how many are already visited, and the place names (`listTripPlaces` is loaded too).

Three caveats to design around:
1. `days` is `undefined` while in flight, and the query key includes `lat`/`lng` from a geolocation callback (`TripDetailPage.tsx:38-51`), so it **refires once** when location resolves. A confirm dialog must handle "count not yet known" rather than defaulting to 0
2. Compute the drop set **by index, never by date** — both sides order by date (`GetItineraryHandler.cs:26`, `UpdateTripHandler.cs:33`), but the single-day date projection (`GetItineraryHandler.cs:105-107`) makes date matching unsafe
3. `ItineraryDayDto` does not carry `TripId`

### TripsPage — NO, not from anything it holds

`TripsPage.tsx:17` loads only `useListTripsQuery()`, whose payload is `TripDto` (`TripDtos.cs:5-7`, mirrored at `api.ts:498`): `Id, Name, Destination, StartDate, DayCount, DefaultTravelMode, IsDaily`. **No day IDs, no stop count, nothing below the trip row.** Three options, none free:

1. Fire `getItinerary` for the one trip when the dialog opens (one extra request; the `TripItinerary` cache is per-trip so it is a clean single fetch)
2. Add a count to `TripDto` — a backend change touching `TripDtos.cs`, `ListTripsHandler.cs`, `GetTripHandler.cs`, `CreateTripHandler.cs`, `UpdateTripHandler.cs`, `SetTripDailyHandler.cs`, all of which construct it **positionally**
3. Only expose the destructive shrink from the detail page, where the data is already loaded

**For the delete path this is moot.** `DeleteTripHandler.cs:21` is `trip.SoftDelete()` — sets `DeletedAt`/`UpdatedAt` and nothing else. No days, stops, places or checklist entries are removed. The only user-visible loss is that the trip's places vanish from Discover (`ListMyPlacesHandler` filters `t.DeletedAt == null`). Everything is recoverable by clearing `DeletedAt` in SQL; there is no un-delete endpoint and no hard-delete path.

## Uncertainties / could not determine

- **Extend + backward-shift statement ordering is untested and unverified.** Only the forward-shift case is proven (`UpdateTripHandlerRelationalTests.cs:117-127`)
- **Whether SQLite in the test fixture enforces the cascade** — `PRAGMA foreign_keys` was not checked. Immaterial to prod, but a future cascade test written against SQLite might pass or fail for the wrong reason
- **What `InMemoryAppDbContext` does on this path** was not opened. The InMemory provider has no store cascade and the handler never loads the Stops, so a shrink test there would probably orphan or throw
- **Whether any prod trip currently has stops on non-first days** — needs a DB query that was not run (it would need a temporary firewall rule per CLAUDE.md). So "how much data is actually at risk today" is unknown
- **Concurrency** — no `RowVersion` on `Trip`. Two simultaneous edits are last-write-wins; whether a concurrent `AddStop` into a day being deleted would FK-fail or silently vanish was not traced
- **`Destination` clearing** — `Trip.UpdateDetails` is a full replace, so a PUT omitting `destination` nulls it out. `TripDateEditor.tsx:78` passes it through correctly, but an edit form that forgets the field would silently wipe the destination. Likely but unverified at the deserialisation level

<!-- decision-map:resolution:end -->
