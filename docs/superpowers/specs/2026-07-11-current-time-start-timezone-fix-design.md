# Design — Fix "Current-time start" showing the server's UTC clock instead of the viewer's local time

**Date:** 2026-07-11
**Status:** Draft for approval
**ADR:** [ADR-038](../../adr/038-current-time-start-viewer-timezone.md). Root cause verified via debug-mantra (source trace + falsification), not hypothesised.
**Relates to:** ADR-008 (Smart Schedule cascade), ADR-012/013 (day-start inline edit + commit-on-change), ADR-027 (frontend supplies per-request input, backend resolves — the same pattern as the Approach leg). Regression introduced with the `UseCurrentTimeAsStart` flag in commit `cdd8b4f` (#25).

## Overview

A Day flagged **Current-time start** ("ใช้เวลาปัจจุบันเสมอ") shows a start time equal to
the viewer's UTC offset behind reality (Thailand 21:44 → shown 14:43). The backend
re-seeds the start with `TimeOnly.FromDateTime(DateTime.Now)`, and `DateTime.Now` on
Azure App Service is the **server's UTC clock**. The fix makes the caller supply its
**IANA time zone**, and the backend resolves "now" as `IClock.UtcNow` converted into
that zone. The value stays server-authoritative so `dayStartTime` remains the
effective start for both consumers (SPA + MCP).

```mermaid
flowchart TD
    subgraph Before["Before — bug"]
      B1["Day flag UseCurrentTimeAsStart = true"] --> B2["GetItineraryHandler:<br/>TimeOnly.FromDateTime(DateTime.Now)"]
      B2 --> B3["server clock = UTC on Azure"]
      B3 --> B4["start = 14:43 (7h behind ICT)"]
    end
    subgraph After["After — fix"]
      A1["caller sends IANA tz id<br/>(SPA: Intl…timeZone · MCP: required arg)"] --> A2["GetItineraryHandler:<br/>ConvertTimeFromUtc(IClock.UtcNow, tz)"]
      A2 --> A3["start = 21:44 (viewer-local ICT)"]
    end
```

## 1. Verified root cause

[GetItineraryHandler.cs:79](../../../backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs#L79):

```csharp
var startTime = day.UseCurrentTimeAsStart ? TimeOnly.FromDateTime(DateTime.Now) : day.DayStartTime;
```

- `DateTime.Now` returns the **server** local wall-clock; Azure App Service runs UTC, so it equals UTC — exactly the viewer's UTC+7 offset behind.
- The frontend "ตอนนี้" button (`dateToHms(new Date())`, [DayStartEditor.tsx:76](../../../frontend/src/pages/trips/components/DayStartEditor.tsx#L76)) uses the **browser** clock and is correct — only the backend-resolved persisted-flag path is wrong.
- The unit test ([GetItineraryHandlerTests.cs:149](../../../backend/tests/MenuNest.Application.UnitTests/Trips/GetItineraryHandlerTests.cs#L149)) asserts against the same `TimeOnly.FromDateTime(DateTime.Now)` source, so it **mirrors** the bug and passes on any machine.

## 2. Scope

- **In:** the one `GetItinerary` request path — query contract, handler, HTTP controller, MCP tool, SPA callers, and the mirror test.
- **Out (non-goals):** storing a time zone per Trip/Day/User (the caller supplies it per request); the "ตอนนี้" button (already correct); the weather endpoints (On-arrival derives from the client cascade that seeds off `dayStartTime`, so it is fixed **transitively** once the seed is correct — no change needed); any server timezone config (the fix removes that dependency entirely).

## 3. Contract change — `GetItinerary`'s IANA time zone is optional at the type level, required only when a current-time-start Day exists

**Scrutiny refinement:** the first version of this fix made the tz eagerly required
for every call, which took down normal itinerary reads for trips with no
current-time-start Day and broadened the MCP `get_itinerary` contract for every
trip. The contract below scopes the requirement to when it is actually used.

```mermaid
sequenceDiagram
    participant C as Caller (SPA / MCP AI)
    participant H as GetItineraryHandler
    participant K as IClock (UtcNow)
    C->>H: GetItineraryQuery(tripId, timeZoneId?, lat?, lng?)
    H->>H: load Days
    alt any Day.UseCurrentTimeAsStart
        H->>H: resolve tz = FindSystemTimeZoneById(timeZoneId)
        Note over H: missing/unresolvable → DomainException (reject, no fallback)
        H->>K: UtcNow
        K-->>H: 2026-07-11T14:44Z
        H->>H: flagged day → ConvertTimeFromUtc(UtcNow, tz) → 21:44
    else no Day flagged
        Note over H: timeZoneId ignored entirely — missing/bad value is not an error
    end
    H-->>C: ItineraryDayDto[] (dayStartTime = effective start)
```

### 3.1 Application layer

| File | Change |
|---|---|
| `GetItinerary/GetItineraryQuery.cs` | `record GetItineraryQuery(Guid TripId, string? TimeZoneId = null, double? ViewerLat = null, double? ViewerLng = null)` — `TimeZoneId` **optional** (nullable, default `null`). |
| `GetItinerary/GetItineraryHandler.cs` | Inject `IClock` (4th ctor arg). Load `days` first, then resolve the tz **lazily** — only `if (days.Any(d => d.UseCurrentTimeAsStart))` — so a missing/bad id is rejected only when it would actually be used. Replace the old start-time line with the converted-local computation guarded by the same flag. |

Handler shape (illustrative — exact code is the plan's job):

```csharp
public GetItineraryHandler(IApplicationDbContext db, IUserProvisioner users, IRouteService routes, IClock clock) { … }

var days = await _db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).ToListAsync(ct);

TimeZoneInfo? tz = null;
if (days.Any(d => d.UseCurrentTimeAsStart))
{
    if (string.IsNullOrWhiteSpace(q.TimeZoneId))
        throw new DomainException("Time zone is required for a day that starts from the current time.");
    try { tz = TimeZoneInfo.FindSystemTimeZoneById(q.TimeZoneId); }
    catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
    { throw new DomainException($"Unknown time zone: {q.TimeZoneId}"); }
}
…
var startTime = day.UseCurrentTimeAsStart
    ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz!))
    : day.DayStartTime;
```

- `IClock.UtcNow` is `DateTime.UtcNow` (Kind = Utc), which `ConvertTimeFromUtc` requires. `SystemClock`/`IClock` is already DI-registered and used by other handlers.
- `.NET` resolves IANA ids on both Linux and Windows (ICU) on the target runtime.
- `tz!` is safe: `tz` is only read inside `day.UseCurrentTimeAsStart ? … : …`, and whenever any day is flagged, the guard above has already assigned it or thrown.

### 3.2 Web API

| File | Change |
|---|---|
| `TripsController.cs` (GetItinerary action) | `[FromQuery] string? tz` **before** `lat`/`lng`; pass to `new GetItineraryQuery(id, tz, lat, lng)`. Nullable ⇒ no model-binding-level requirement; the handler enforces the requirement only when a flagged Day exists. |

### 3.3 MCP tool

| File | Change |
|---|---|
| `TripTools.cs` (`get_itinerary`) | `string? timeZoneId` param with a `[Description]` of *"The user's IANA time zone, e.g. Asia/Bangkok. Optional — required ONLY if the trip has a day set to 'always start from the current time'; omit otherwise."* Pass into `GetItineraryQuery`. Update the tool `[Description]` to say `dayStart` for a current-time day is resolved using `timeZoneId` when supplied. |

A nullable C# param ⇒ the MCP schema marks `timeZoneId` **optional**; the AI only needs to supply it for trips it knows (from `get_itinerary`'s own prior response, or `get_trip`) contain a current-time-start day.

### 3.4 Frontend (SPA)

| File | Change |
|---|---|
| `shared/api/api.ts` (`getItinerary`) | Arg type `{tripId: string; tz: string; lat?: number; lng?: number}` (`tz` required). Query string always includes `tz`; `lat`/`lng` stay conditional. |
| `pages/trips/utils/time.ts` (or a small new util) | Add `getViewerTimeZone()` → `Intl.DateTimeFormat().resolvedOptions().timeZone`, so callers don't duplicate it. |
| `pages/trips/components/ItineraryTab.tsx:110` | Pass `tz: getViewerTimeZone()` to `useGetItineraryQuery`. |
| `pages/trips/hooks/useDayRoute.ts:74` | Same. |

- `tz` is stable per session, so it is a clean RTK Query cache-key component — no cache thrash, and the "re-seed on every fetch" semantics are unchanged (each real network fetch recomputes `UtcNow`+tz server-side).
- Any e2e mock (`e2e/helpers/mockRoutes`) or component test that builds the itinerary URL / arg must be updated for the new `tz`.

## 4. Behavior & edge cases

| Case | Result |
|---|---|
| Flagged day, valid tz (SPA) | `dayStartTime` = viewer-local now (e.g. 21:44 ICT). |
| Flagged day, MCP with user's tz | Same — Claude receives the correct local start. |
| Non-flagged day, tz present | `dayStartTime` = persisted value, unchanged; tz is not validated (never used). |
| Non-flagged day, tz missing | `dayStartTime` = persisted value, unchanged; no error — a missing tz is fine when nothing needs it. |
| Non-flagged day, tz unresolvable (bad id) | `dayStartTime` = persisted value, unchanged; no error — the bad id is never validated because it is never used. |
| Flagged day, tz missing | `DomainException("Time zone is required for a day that starts from the current time.")`. |
| Flagged day, tz present but unresolvable | `DomainException("Unknown time zone: …")`, surfaced as an error. |
| DST zones | Correct for any date via `ConvertTimeFromUtc` (Thailand has no DST; the design is DST-proof regardless). |

## 5. Testing strategy

**Backend (replaces the mirror test):**

- Extend the test fixture to inject a `FixedClock` (UtcNow with `DateTimeKind.Utc`). Update **every** `new GetItineraryHandler(...)` call in `GetItineraryHandlerTests.cs` for the new ctor arg.
- `Resolves_a_current_time_day_start_in_the_supplied_time_zone`: flagged day + `Asia/Bangkok`, FixedClock `2026-01-15T12:00:00Z` → expect `TimeOnly(19, 0)` (12:00 UTC + 7h ICT). Asserts a concrete converted value — cannot mirror the implementation.
- `Applies_the_time_zone_not_the_server_clock`: flagged day + `America/New_York`, same fixed UTC → `TimeOnly(7, 0)` (12:00 UTC − 5h EST, no DST in January) — a **different** local time than the Bangkok case, proving the tz is actually applied.
- `Ignores_a_missing_time_zone_when_no_day_is_flagged`: no day flagged, no tz supplied → no throw, `dayStartTime` = persisted value.
- `Ignores_an_unresolvable_time_zone_when_no_day_is_flagged`: no day flagged, tz = `"Not/AZone"` → no throw, `dayStartTime` = persisted value; the bad id is never validated because it is never used.
- `Rejects_a_missing_time_zone_when_a_day_is_flagged`: a day is flagged, no tz supplied → `DomainException`.
- `Rejects_an_unresolvable_time_zone_when_a_day_is_flagged`: a day is flagged, tz = `"Not/AZone"` → `DomainException`.

The 4 pre-existing non-time tests (ordered stops, leg travel modes, approach leg with/without viewer coords) drop the `"UTC"` argument entirely, since `TimeZoneId` is now optional and none of those days are flagged.

**Frontend:** update mocks/tests that reference the `getItinerary` arg/URL for the added `tz`; `tsc --noEmit` must pass with the new required arg (the compiler will flag any caller that forgot `tz`).

## 6. Rollout

- **No DB migration, no Azure config change.** The fix is code-only and makes the server timezone irrelevant to correctness.
- The `frontend/.husky/pre-commit` hook runs the full backend build+test and frontend `tsc`+build (~40s+); expect the wait, do not `--no-verify`.
- **Ticket:** open a GitHub issue for this bug first (CLAUDE.md: every commit references a tracking ticket); the fix commit closes it. Stage narrowly (`git add` explicit paths — never `-A`).

## 7. Self-review

- No placeholders / TODOs; every named file/line verified against the current tree.
- Internally consistent with ADR-038 (all five decisions reflected: A1, IANA, required, reject-always, IClock).
- Scope matches the recap; non-goals explicit. Transitive weather correctness noted, no extra work claimed.
- Ambiguity resolved: eager tz validation, required-arg enforcement mechanism, FixedClock Kind, cache-key stability all stated.
