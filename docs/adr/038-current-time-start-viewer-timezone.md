# ADR-038: "Current-time start" resolves the viewer's local now from a caller-supplied IANA time zone + the server's UTC clock (never server-local `DateTime.Now`)

**Date:** 2026-07-11
**Status:** Accepted
**Relates to:** ADR-008 (Smart Schedule cascade — this is what the start seeds), ADR-012 / ADR-013 (day-start inline edit + commit-on-change), ADR-027 (the "frontend supplies per-request input, backend resolves" pattern already used for the Approach leg's viewer lat/lng). Fixes the regression introduced with the `UseCurrentTimeAsStart` flag in commit `cdd8b4f` (#25).

```mermaid
flowchart TD
    Q{"UseCurrentTimeAsStart start time is wrong by the viewer's<br/>UTC offset: GetItineraryHandler uses server-local DateTime.Now,<br/>which is UTC on Azure App Service. How should 'now' resolve?"} -->|chosen| A["Backend-authoritative (A1): caller supplies its time zone;<br/>server computes IClock.UtcNow → viewer-local time.<br/>dayStartTime in the DTO stays the effective start for EVERY consumer"]
    Q -->|rejected| A2["Client seeds the cascade from new Date() (A2)<br/>— no API change, but dayStartTime-when-flag-on becomes a stale<br/>fallback the client must override, and the MCP consumer<br/>(which reads dayStart to compute arrivals) gets a wrong value"]
    A --> W{"Wire format"}
    W -->|chosen| W1["IANA time-zone id (e.g. Asia/Bangkok),<br/>converted via TimeZoneInfo.ConvertTimeFromUtc — DST-correct for any date"]
    W -->|rejected| W2["UTC offset in minutes — simpler + clock-skew-immune,<br/>but a snapshot with no DST rules; user chose DST-proof"]
    W -->|rejected| W3["Client local-time string — trusts the client clock AND<br/>thrashes/stales the RTK Query cache (arg changes every render)"]
    A --> R{"Who supplies it?"}
    R -->|chosen| R1["REQUIRED/VALIDATED only when the trip has a Day flagged<br/>UseCurrentTimeAsStart (SPA + MCP/AI).<br/>No silent UTC fallback when it IS needed — that silent fallback<br/>IS what caused this bug. A trip with no such Day needs no tz."]
    A --> F{"tz id won't resolve"}
    F -->|chosen| F1["Reject loudly (DomainException) — but only when a flagged<br/>Day is actually present; ignored otherwise (unused input)"]
```

## Context

Issue: with a Day flagged **Current-time start** ("ใช้เวลาปัจจุบันเสมอ", flag
`UseCurrentTimeAsStart`, added in #25), the itinerary shows a start time **7 hours
behind** the viewer's real clock in Thailand (e.g. real 21:44 → shown 14:43). The
cause is verified (debug-mantra): `GetItineraryHandler` re-seeds the start with

```csharp
var startTime = day.UseCurrentTimeAsStart ? TimeOnly.FromDateTime(DateTime.Now) : day.DayStartTime;
```

`DateTime.Now` returns the **server's** local wall-clock. On Azure App Service the
server clock is **UTC**, so `DateTime.Now` == UTC — exactly the viewer's UTC+7
offset behind. The frontend's own "ตอนนี้" button (`dateToHms(new Date())`) uses the
**browser** clock and is correct; only the persisted-flag path, which the backend
resolves, is wrong. The unit test never caught it because it asserts against the
same `TimeOnly.FromDateTime(DateTime.Now)` source — it mirrors the bug and passes on
any machine.

The backend cannot know the viewer's time zone on its own — nothing per-Trip,
per-Day, or per-User stores one. So the "now" must be resolved from information the
caller provides, the same shape ADR-027 already established for the Approach leg
(frontend sends viewer coordinates; backend resolves per request).

A scrutiny pass on the first version of this fix found that validating the tz
**eagerly and unconditionally** overshot the actual need: it took down normal
itinerary reads for trips with no current-time-start Day (a missing/bad tz on such a
trip is unused, yet was rejected), and it broadened the MCP `get_itinerary` contract
to require a tz on every call, not only the calls where a current-time Day exists.
Decisions 3 and 4 below were refined to scope the requirement to when it is actually
needed.

## Decision

1. **Backend-authoritative (A1), not client-seeded (A2).** The caller supplies its
   time zone; `GetItineraryHandler` computes the start as the viewer's local "now".
   `ItineraryDayDto.dayStartTime` therefore remains **the effective start for every
   consumer** — the SPA cascade, the day-start display, and the MCP `get_itinerary`
   tool (whose contract tells Claude to compute arrivals as `dayStart + running
   sum`). (Rejected — A2, seeding client-side from `new Date()`: needs no API
   change, but when the flag is on `dayStartTime` degrades to a stale fallback the
   client must silently override, and the MCP consumer would read that stale value
   and mis-compute every arrival.)

2. **Wire format — IANA time-zone id.** The caller sends e.g. `Asia/Bangkok`; the
   backend resolves it with `TimeZoneInfo.FindSystemTimeZoneById` and converts via
   `TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz)`. IANA ids resolve on both
   Linux and Windows on .NET 6+ (ICU). Chosen over a UTC-offset-in-minutes (simpler
   and immune to client clock skew, but a bare snapshot with no DST rules) because
   the owner wanted DST-correctness to be built in rather than a latent trap.
   (Rejected — client local-time string: trusts the client clock and, because RTK
   Query caches by argument, a per-render timestamp would either thrash the cache or
   go stale.)

3. **The time zone is REQUIRED and VALIDATED only when the trip actually needs it —
   no silent fallback when it IS needed.** `GetItineraryHandler` loads the trip's
   Days first; only if at least one Day is flagged `UseCurrentTimeAsStart` does it
   demand a time zone. The SPA sends `Intl.DateTimeFormat().resolvedOptions().timeZone`
   on every call (harmless when unused); the MCP `get_itinerary` tool's `timeZoneId`
   parameter is optional, with a description telling the AI it is required only when
   the trip has a current-time-start day. A trip with **no** such Day ignores the tz
   entirely — a missing or bad value there is not an error, because nothing would
   have used it. When a flagged Day **is** present, the missing-value path is still a
   hard failure: there is **no** silent default-to-UTC path — that default is
   precisely what produced the original bug.

4. **An unresolvable tz id is rejected loudly, but only when it would actually be
   used.** A supplied-but-unknown id (typo, bad value) throws a
   `DomainException("Unknown time zone: <id>")` whenever the trip has a Day flagged
   `UseCurrentTimeAsStart` — surfaced as an error rather than falling back to UTC or
   server-local. A correct SPA/AI never sends a bad id, so that is a caller bug that
   should fail visibly. For a trip with no flagged Day, an unresolvable or missing tz
   is simply never validated — it was never going to be used.

5. **Resolve "now" through `IClock`.** The handler injects `IClock` and uses
   `_clock.UtcNow` (the same testability seam the rest of the codebase uses), never
   `DateTime.Now`/`DateTime.UtcNow` directly. This also lets the test assert a
   deterministic converted value (fixed UTC + known zone → known local), so it can
   no longer mirror the implementation and hide a timezone regression.

## Consequences

**Positive:** the flagged start now shows the viewer's real local time; because the
conversion is anchored to `IClock.UtcNow` + an explicit zone, the **server's own
timezone becomes irrelevant to correctness** (no dependency on `WEBSITE_TIME_ZONE`).
DST is handled for any date. The DTO contract stays honest for both the SPA and MCP.
The mirror-bug test is replaced by a deterministic one.

**Negative:** `GetItineraryQuery.TimeZoneId` is optional at the type level and the
HTTP `tz` query param is optional too — this is **non-breaking** for callers/trips
that use no current-time-start Day (the SPA still always sends `tz`, which is simply
ignored when unused; the MCP `get_itinerary` tool's `timeZoneId` argument is likewise
optional and only becomes required, at the handler level, once a trip has a flagged
Day). `GetItineraryHandler` now depends on `IClock`. No data migration and no server
config change are needed.
