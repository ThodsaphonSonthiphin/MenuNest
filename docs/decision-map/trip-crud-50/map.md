# Decision map - Trip CRUD: edit every field, and delete (#50)

```mermaid
graph TD
    MAP["map (this file)"] --> T["tickets/*.md — one decision each"]
    T --> D["Decisions so far (index below)"]
```

## Destination
Trip editing and deletion are usable in the MenuNest SPA on production: every field the create dialog collects (name, destination, start date, day count, default travel mode) can be changed on an existing trip, a trip can be deleted, and neither path silently destroys scheduled stops.

## Notes
Frontend-heavy: PUT and DELETE /api/trips/{id} already exist and so do the RTK hooks - useDeleteTripMutation has zero call sites today, and useUpdateTripMutation is used only by TripDateEditor, which changes the start date and nothing else. Touching the backend is allowed if needed; apply any EF migration by hand per CLAUDE.md. Consult dev-workflows:grill-then-plan for each grilling ticket, superpowers:subagent-driven-development to execute, and docs/mocks plus the MenuNest Claude Design project for any UI mock. Standing preferences: inline-SVG icons and never emoji; Thai UI copy; commit referencing #50; stage explicit paths only. The SPA has no component or visual test harness and the review gates are blind to visual fidelity, so verify interactively before pushing. Once the frontier empties, hand to grill-then-plan, then SDD, then interactive verification on prod.

## Decisions so far

<!-- decision-map:decisions:start -->
- [Existing patterns - which edit, commit and destructive-confirm conventions should a trip-edit surface inherit?](tickets/existing-edit-patterns.md) — A shared useConfirm() destructive modal already exists and is mounted app-wide via AppLayout - but the entire Trips feature uses ZERO confirmations, every destructive trips action fires on first tap. ADR-013 mandates commit-on-change and its stated rationale (a mis-pick is cheap to re-pick) does not transfer to a field that destroys stops; ADR-085's own tie-breaker (single field -> autosave) fails for a multi-field form; ADR-137 forbids putting IsDaily on the full-replace UpdateTrip. The trip card is a single button so a secondary action needs it unwrapped, and dialogs are portaled so page-scoped trips tokens do not resolve - the two existing dialog families are teal and orange and do not match.
- [Field change effects - what does editing each Trip field actually destroy or silently alter?](tickets/field-change-effects.md) — Shrinking dayCount hard-deletes the trailing days and DB-cascades their Stops (IsVisited, notes, dwell, day start time, use-current-time all go, unrecoverably); checklist entries, TripPlaces and profiles survive. A startDate move destroys nothing but silently re-derives weather, season and opening-hours flags. defaultTravelMode re-costs nothing and only affects new stops. TripDetailPage can compute the at-risk stop count from cache it already holds; TripsPage cannot - TripDto carries nothing below the trip row.
<!-- decision-map:decisions:end -->

## Not yet specified

<!-- decision-map:fog:start -->
- Whether the trips list needs per-trip day or stop counts from the API - this only arises if a list-card action has to warn before destroying stops.
- Whether duplicating a trip belongs in this effort - the C of CRUD that #50 does not name, and that was not ruled out.
- Whether editing or deleting several trips at once belongs here, which would mean a list-management mode.
- Whether #50 reaches Place and Stop CRUD, which already have their own UI, or stops at the trip record itself.
- Whether the trips list needs sorting or search once its cards carry actions.
- Whether a trip whose start date is already in the past may have its start date or day count changed at all.
<!-- decision-map:fog:end -->

## Out of scope

<!-- decision-map:scope:start -->
- Restoring a deleted trip - no undo toast, no trash bin, no restore endpoint. Deletion is final from the user's point of view; the DB keeps the soft-deleted row only as a data-retention detail.
- Moving the daily on/off toggle into the edit surface - DailyToggle stays where it is on the trip detail header, so the edit form never sets IsDaily.
<!-- decision-map:scope:end -->
