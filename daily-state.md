---
type: daily-state
schema_version: 1
updated: '2026-07-12T15:26:02+07:00'
---

## Log

- 2026-07-12T11:49:57+07:00 — docs(trips): reconcile ADR-040 with the shipped AA-safe visited treatment (#24)
- 2026-07-12T14:07:34+07:00 — feat(trips): computeReorder pure helper for stop drag reorder (#31)
- 2026-07-12T14:08:28+07:00 — feat(trips): computeReorder pure helper for stop drag reorder (#31)
- 2026-07-12T14:22:12+07:00 — $(cat <<'EOF'
feat(trips): drag-to-reorder stops with full-view loading (closes #31)
EOF
)
- 2026-07-12T14:30:17+07:00 — test(trips): e2e keyboard reorder smoke (#31)
- 2026-07-12T14:36:59+07:00 — $(cat <<'EOF'
fix(trips): use existing .trip-card selector in reorder e2e (#31)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-12T14:47:03+07:00 — docs(trips): ADR 043-046 + design spec + plan for stop drag-reorder (#31)
- 2026-07-12T15:26:02+07:00 — fix(trips): scrutinize follow-ups for stop drag-reorder (#31)

- Deterministic loader: reorderStops drops invalidatesTags; ItineraryTab drop
  handler awaits an explicit refetch then clears isReordering in finally, so the
  full-view loader spans the recompute without relying on RTK invalidation timing.
- Restore leg->card grouping weakened by the Fragment refactor (travel-leg
  margin-bottom: -8px cancels the flex gap below the leg).
- Overlay z-index 50 -> 1200 so the full-view loader actually covers the app nav.
- E2E: navigate via data-testid="trip-card" (added to TripsPage) instead of the
  brittle .trip-card class.
