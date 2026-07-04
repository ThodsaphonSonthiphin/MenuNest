---
type: daily-state
schema_version: 1
updated: '2026-07-04T01:33:17+07:00'
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
