---
type: daily-state
schema_version: 1
updated: '2026-06-30T00:27:13+07:00'
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
