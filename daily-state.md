---
type: daily-state
schema_version: 1
updated: '2026-07-12T08:17:28+07:00'
---

## Log

- 2026-06-29T19:06:22+07:00 — $(cat <<'EOF'
feat(trips): trip detail shell with tabs + places list view

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-29T19:56:37+07:00 — $(cat <<'EOF'
refactor(trips): back SegmentedTabs with ej2 TabComponent (Pure-React has no Tab)

Replace hand-rolled segmented control with @syncfusion/ej2-react-navigations
TabComponent. Controlled selection via selectedItem prop (initial) + ref.select()
in useEffect for parent-driven updates; isInteracted guard on the selected event
prevents onChange feedback loops. Props contract unchanged â€” call sites untouched.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-29T20:08:35+07:00 — $(cat <<'EOF'
feat(trips): useSchedule cascade hook + best-time flag (tested)

Vitest setup (vitest@4.1.9, test block in vite.config.ts, include src/**/*.test.ts,
node env). TDD RED then GREEN: 4/4 tests pass. Build clean.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-29T20:08:41+07:00 — $(cat <<'EOF'
feat(trips): useSchedule cascade hook + best-time flag (tested)

Vitest setup (vitest@4.1.9, test block in vite.config.ts, include src/**/*.test.ts,
node env). TDD RED then GREEN: 4/4 tests pass. Build clean.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-29T20:14:08+07:00 — $(cat <<'EOF'
feat(trips): smart-schedule itinerary tab (cascade times, legs, reorder, best-time flag)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-29T20:19:02+07:00 — $(cat <<'EOF'
fix(trips): call useSchedule unconditionally to obey Rules of Hooks

useSchedule calls useMemo internally. It was previously called after
an early return guard, causing React's hook count to change between
renders (loading vs loaded), which crashes at runtime with Rendered
- 2026-06-29T20:26:45+07:00 — $(cat <<'EOF'
feat(trips): stop editor (dwell stepper, manual best-time, travel mode, computed preview)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-29T20:30:41+07:00 — $(cat <<'EOF'
refactor(trips): dwell quick-chips use Syncfusion Button

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-29T21:07:54+07:00 — docs(trips): record resolved UI decisions (ej2 Tab, Syncfusion date/time, reorder) in the plan

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-06-29T21:31:00+07:00 — $(cat <<'EOF'
fix(trips): scrutinize fixes â€” opening-hours flag, Google-call timeouts + parallel legs, GET trip

Major 1 â€” Smart Schedule now flags a stop scheduled when its place is closed:
isOpenAt() parses the regularOpeningHours snapshot (same/overnight periods,
24h, malformed) and useSchedule combines it with the best-time check (ADR-008).
The opening-hours data was already plumbed end-to-end but never evaluated.

Major 2 â€” bound every Google Routes/Places call with a per-call timeout
(8s routes / 10s resolve) via a linked CTS so one hung upstream cannot stall
the itinerary response; honour real caller cancellation before the Haversine
fallback; resolve all itinerary legs concurrently (cold cache ~1 round-trip
instead of N sequential); map resolver network failures to friendly
DomainExceptions instead of a 500.

Minor 1 â€” add GET /api/trips/{id} + GetTripHandler (owner-scoped, soft-delete
aware). Frontend fetches the single trip (provides the previously-dead
TripDetail tag) and renders a not-found state for deep-links to a missing/
unowned trip, instead of over-fetching the whole list.

Minor 2 â€” document the intentional shrink-trip cascade delete and the
UI-confirm owed when an edit-trip screen is built.

Nit 1 â€” single GoogleMapsHosts allowlist shared by the resolve validator
(gates input) and the resolver (re-checks final URL), removing duplication.

Tests: Application 314, WebApi 19, frontend useSchedule 11 â€” all green;
tsc -b + vite build clean.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-29T22:47:49+07:00 — (commit)
- 2026-06-29T23:02:33+07:00 — (commit)
- 2026-06-29T23:03:22+07:00 — (commit)
- 2026-06-30T00:27:07+07:00 — (commit)
- 2026-06-30T00:27:13+07:00 — $(cat <<'EOF'
refactor(trips): rebuild SegmentedTabs as stateless Pure React Buttons

Replace the ej2 TabComponent (which owns its selection index internally)
with a stateless control built from @syncfusion/react-buttons. `value` is
the single source of truth: the render reflects it and a click reports the
new value up via onChange â€” there is no internal selection state, so the
active segment can never drift out of sync with `value`.

This removes the tab
- 2026-06-30T08:08:22+07:00 — @
fix(syncfusion): register license with ej2-base to remove trial banner

The Syncfusion trial banner kept showing even with the license key set.
Cause: the app registered the key only with @syncfusion/react-base, but
the QR generator uses the legacy @syncfusion/ej2-* family, whose
@syncfusion/ej2-base keeps a SEPARATE license validator. With ej2-base
unregistered, rendering the QR component injected the trial banner into
document.body; as an SPA (React Router never reloads) that banner then
persisted across every subsequent route.

- Extract registration into shared/syncfusion/license.ts, registering the
  key with BOTH react-base and ej2-base
- Add a unit test guarding the dual registration (adversarially verified:
  removing the ej2 call turns the test red)
- Sync package-lock.json for the now-direct @syncfusion/ej2-base dependency
- Fix the stale env var name + description in docs/plan.md

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
@
- 2026-06-30T08:09:23+07:00 — (commit)
- 2026-06-30T08:33:15+07:00 — (commit)
- 2026-07-03T09:48:48+07:00 — (commit)
- 2026-07-03T15:41:01+07:00 — (commit)
- 2026-07-03T15:47:50+07:00 — refactor(trips): extract hms<->Date converters into utils/time with unit tests
- 2026-07-03T15:51:16+07:00 — feat(trips): pure Google Maps navigate-URL builders
- 2026-07-03T15:58:37+07:00 — feat(trips): add DayStartEditor (inline TimePicker, commit-on-change, optimistic revert)
- 2026-07-03T16:04:10+07:00 — $(cat <<'EOF'
fix(trips): omit travelmode for out-of-union mode in nav-URL builders
EOF
)
- 2026-07-03T16:09:42+07:00 — feat(trips): edit day start time inline on the summary bar
- 2026-07-03T16:10:24+07:00 — $(cat <<'EOF'
feat(trips): conservative mobile/desktop waypoint-cap detection

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-03T16:11:29+07:00 — feat(trips): edit day start time inline on the summary bar
- 2026-07-03T16:12:04+07:00 — $(cat <<'EOF'
feat(trips): conservative mobile/desktop waypoint-cap detection

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-03T16:20:56+07:00 — $(cat <<'EOF'
feat(trips): per-Stop navigate button opens Google Maps

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-03T16:33:35+07:00 — $(cat <<'EOF'
feat(trips): whole-day route pill + overflow/mixed-mode notes

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-03T16:39:55+07:00 — refactor(trips): reset day-start error via render-time check (avoid set-state-in-effect)
- 2026-07-03T16:43:45+07:00 — (commit)
- 2026-07-03T16:44:46+07:00 — (commit)
- 2026-07-03T16:54:37+07:00 — docs(trips): ADRs 012/013 + design spec + plan + mock for day-start-time inline edit

Captures the grill-then-plan design record for editing a Day's start time
inline on the itinerary summary bar (ADR-012 affordance, ADR-013 commit-on-change).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-03T23:18:41+07:00 — docs(trips): ADR-016 + design spec, plan & mock for add-place search (grounded vs google-maps-platform)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-03T23:20:39+07:00 — (commit)
- 2026-07-03T23:22:37+07:00 — $(cat <<'EOF'
feat(trips): categorizePlace â€” Google types â†’ PlaceCategory

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-03T23:33:22+07:00 — $(cat <<'EOF'
test(trips): cover extended place-type vocabulary; cite ADR filename

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-03T23:38:28+07:00 — $(cat <<'EOF'
feat(trips): toResolvedPlace snapshot mapper + field mask

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-03T23:45:41+07:00 — $(cat <<'EOF'
feat(trips): add addMode slice state (alongside addPlaceOpen)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-03T23:52:32+07:00 — feat(trips): usePlaceSearch â€” client-side Places autocomplete + details

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-04T00:00:04+07:00 — fix(trips): invalidate stale autocomplete on clear; reset session token on error; declare @types/google.maps — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-04T00:05:36+07:00 — $(cat <<'EOF'
feat(trips): AddPlacePreviewCard preview component

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-04T00:12:18+07:00 — $(cat <<'EOF'
feat(trips): PlaceLinkFallbackDialog â€” hidden paste-a-link path

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-04T00:16:11+07:00 — $(cat <<'EOF'
feat(trips): AddPlaceSearchBar â€” floating search + live suggestions

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-04T00:22:18+07:00 — $(cat <<'EOF'
feat(trips): AddPlaceMode orchestrator (search + tap + preview + add)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-04T00:31:00+07:00 — $(cat <<'EOF'
fix(trips): resolve each POI tap once (drop unstable effect dep + cancelled-flag race)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-04T00:36:32+07:00 — $(cat <<'EOF'
feat(trips): TripMap renders AddPlaceMode + captures POI taps in add-mode

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-04T00:41:48+07:00 — $(cat <<'EOF'
feat(trips): arm map-centric add-mode from TripDetailPage; drop AddPlaceSheet usage

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-04T00:47:01+07:00 — $(cat <<'EOF'
style(trips): add-mode search bar, suggestions & preview styles

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-04T01:03:15+07:00 — feat(trips): remove AddPlaceSheet; align add-mode CSS to mock; finalize add-place search

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-04T01:27:21+07:00 — fix(trips): add-mode exit control + correct opening-hours snapshot shape + tap hint (final review)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-04T01:33:17+07:00 — $(cat <<'EOF'
test(trips): make opening-hours getter test a real regression guard (non-enumerable getters)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-06T09:08:30+07:00 — docs(trips): weather design â€” ADRs 027-032, spec, plan, glossary (#10)

Grill-then-plan output for GitHub issue #10 (per-Stop weather on the itinerary):
CONTEXT glossary terms (Weather reading / Now / On-arrival / Forecast horizon /
No weather data), ADRs 027-032, design spec, UI mock, and the 11-task TDD
implementation plan. Google billing verified enabled; Weather API works on the
deployed key.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-06T09:13:55+07:00 — feat(trips): add IWeatherService seam + no-op fallback (#10)
- 2026-07-06T09:20:38+07:00 — feat(trips): GoogleWeatherService current-conditions lookup (#10)
- 2026-07-06T09:27:23+07:00 — $(cat <<'EOF'
test(trips): guard GoogleWeatherService sends no field mask (#10)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-06T09:33:11+07:00 — feat(trips): GoogleWeatherService on-arrival hourly bucket (#10)
- 2026-07-06T09:40:46+07:00 — feat(trips): GetStopWeather query+handler+validator (#10)
- 2026-07-06T09:47:41+07:00 — feat(trips): wire IWeatherService + POST api/trips/weather (#10)
- 2026-07-06T09:53:00+07:00 — feat(trips): add getStopWeather RTK endpoint + weather DTOs (#10)
- 2026-07-10T15:43:40+07:00 — feat(trips): pure weather helpers (window/icon/rainy/chip-state/batches) (#10)
- 2026-07-10T15:49:34+07:00 — feat(trips): WeatherIcons + WeatherChip component (#10)
- 2026-07-10T15:54:55+07:00 — feat(trips): weather chip tokens + styles (#10)
- 2026-07-10T16:00:37+07:00 — feat(trips): useStopWeather hook (Now + On-arrival batches) (#10)
- 2026-07-10T16:06:44+07:00 — $(cat <<'EOF'
feat(trips): render per-stop Now + On-arrival weather chips (closes #10)
EOF
)
- 2026-07-10T16:19:31+07:00 — $(cat <<'EOF'
fix(trips): re-stamp StopId on weather cache hit for duplicate-coord stops (#10)
EOF
)
- 2026-07-10T16:22:32+07:00 — (commit)
- 2026-07-10T16:44:59+07:00 — docs: document pre-commit hook + scoped-add commit convention

Repo-convention doc from a /reflect session (no feature ticket): the
frontend/.husky/pre-commit hook runs the full backend+frontend suite on every
commit, and feature commits must stage explicit paths (never git add -A) to
avoid sweeping the dirty daily-state.md / untracked AGENTS.md.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-10T16:52:24+07:00 — docs(trips): renumber weather ADRs 027-032 -> 028-033 (#10)

The weather feature was numbered ADR-027..032 on a base predating main's
concurrent approach-leg feature, which had already claimed ADR-027. Renumber the
six weather ADRs to 028-033 (approach-leg keeps 027) and update every reference
in the spec, plan, and CONTEXT.md glossary. Docs-only; no code change.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-10T16:53:43+07:00 — (commit)
- 2026-07-10T19:39:48+07:00 — docs(trips): ADR-034/035 + spec + plan for Trip Planner over MCP (#18) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-10T19:44:51+07:00 — feat(trips): TripTools MCP class + Trips CRUD tools (#18) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-10T19:51:37+07:00 — feat(trips): MCP place tools incl resolve-place capture flow (#18) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-10T19:57:31+07:00 — $(cat <<'EOF'
feat(trips): MCP itinerary + stop tools (#18)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-10T20:03:05+07:00 — $(cat <<'EOF'
feat(trips): MCP stop-weather batch tool (closes #18)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-10T20:17:50+07:00 — $(cat <<'EOF'
fix(trips): honest replace-semantics + weather wording in MCP tool descriptions (#18)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-11T22:24:50+07:00 — $(cat <<'EOF'
docs(trips): ADR-038 + spec/plan for current-time-start timezone fix (#30)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-11T22:30:02+07:00 — $(cat <<'EOF'
fix(trips): send the viewer's IANA time zone with the itinerary fetch (#30)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-11T22:36:41+07:00 — $(cat <<'EOF'
test(trips): exercise the getViewerTimeZone UTC fallback (#30)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-11T22:42:37+07:00 — $(cat <<'EOF'
fix(trips): resolve a current-time day start in the viewer's time zone (closes #30)

DateTime.Now returned the server's UTC clock on Azure; thread a required IANA
time zone through GetItinerary and convert IClock.UtcNow into it (ADR-038).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-11T23:07:59+07:00 — $(cat <<'EOF'
fix(trips): require the itinerary time zone only when a current-time day is present (#30)

Scrutiny found the eager, always-required tz gated the whole itinerary read
and broadened the MCP get_itinerary contract for every trip. Scope it to trips
with a UseCurrentTimeAsStart day (ADR-038 decisions 3-4 refined); still no
silent fallback when it is needed.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-11T23:14:37+07:00 — $(cat <<'EOF'
docs(trips): align spec overview/self-review with the tz-scope refinement (#30)

The Â§7 self-review and the Overview mermaid still described the pre-scrutiny
eager/always-required tz; reword to the when-flagged semantics already in Â§3-Â§5.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-12T08:17:28+07:00 — (commit)
