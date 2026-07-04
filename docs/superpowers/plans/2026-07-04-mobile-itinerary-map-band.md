# Mobile Itinerary Collapsible Map Band — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix GitHub issue #8 — on mobile/tablet the Itinerary view shows no map — by rendering the active day's route as a collapsible ~188px `TripMap` band, expanded by default.

**Architecture:** Frontend-only. `TripDetailPage` already computes `useDayRoute(tripId)` unconditionally and discards it on mobile; pass it into `ItineraryTab` as an optional prop (desktop passes none → no band, map stays the right pane). `ItineraryTab` renders the band between the day-tabs and the dark `.day-summary` bar. Collapse state is one non-persisted `tripsSlice` bit (resets to expanded each load, so a fresh open always shows the map). The map stays **mounted** across collapse/expand (CSS height only) and a `ResizeObserver` child forces a re-layout so tiles never grey.

**Tech Stack:** React 19 + TypeScript ~6.0.2 + Vite 8 + Redux Toolkit 2 + `@vis.gl/react-google-maps` 1.8.3 + Vitest 4. Design source: [spec](../specs/2026-07-04-mobile-itinerary-map-band-design.md), [ADR-026](../../adr/026-mobile-itinerary-collapsible-map-band.md), [mock](../../mocks/trip-itinerary-map-toggle-mock.html).

## Global Constraints

- **Frontend-only** — no backend, DB, or API change.
- **All commands run from `frontend/`.** Typecheck+build: `npm run build` (`tsc -b && vite build`). Lint: `npm run lint`. Unit tests: `npm test` (`vitest run`). Dev server: `npm run dev`.
- **Map must stay MOUNTED** across collapse/expand — collapse via CSS `height` only. **Never** `display:none` and never unmount/remount the map, or Google re-inits and tiles render grey.
- **CSS band rule MUST set `min-height: 0`** on both the band and its inner `.trip-map`, or the base `.trip-map` `min-height` (320px / 280px `@≤479px`) floors the box.
- **Icons: inline hand-authored SVG components** (added to `TripFormIcons.tsx`, `viewBox 0 0 24 24`, `stroke: currentColor`, `1em`). **Never emoji / raw unicode glyphs** in UI. `@syncfusion/react-icons` is not a declared dependency — do not import it.
- **User-visible strings are Thai.** Teal is a Trips-only accent (tokens on `.trip-detail`: `--teal #0e8f9e`, `--teal-deep #0b7a87`, `--ink #0f172a`, `--surface`, `--muted`, `--border`).
- **Commits** end with the trailer:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

### Task 1: tripsSlice — `itineraryMapCollapsed` UI state (default expanded)

**Files:**
- Modify: `frontend/src/pages/trips/tripsSlice.ts`
- Test: `frontend/src/pages/trips/tripsSlice.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: `TripsState.itineraryMapCollapsed: boolean` (initial `false` = expanded); action creator `setItineraryMapCollapsed(value: boolean)`.

- [ ] **Step 1: Write the failing test**

Append to `frontend/src/pages/trips/tripsSlice.test.ts` and update the import on line 3 to include the new action:

```ts
// line 3 becomes:
import reducer, {setAddMode, setItineraryMapCollapsed} from './tripsSlice'
```

```ts
// append after the existing describe block:
describe('tripsSlice itinerary map band', () => {
  it('defaults itineraryMapCollapsed to false (map expanded on open — fixes #8)', () => {
    expect(init.itineraryMapCollapsed).toBe(false)
  })
  it('setItineraryMapCollapsed toggles the flag', () => {
    const collapsed = reducer(init, setItineraryMapCollapsed(true))
    expect(collapsed.itineraryMapCollapsed).toBe(true)
    const expanded = reducer(collapsed, setItineraryMapCollapsed(false))
    expect(expanded.itineraryMapCollapsed).toBe(false)
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- src/pages/trips/tripsSlice.test.ts`
Expected: FAIL — `setItineraryMapCollapsed` is not exported (import error) / `init.itineraryMapCollapsed` is `undefined`.

- [ ] **Step 3: Implement in `tripsSlice.ts`**

Add the field to the `TripsState` interface (after `addMode: boolean`):

```ts
  addMode: boolean
  itineraryMapCollapsed: boolean
  stopEditorStopId: string | null
```

Add the default to `initialState` (keep it near the other booleans):

```ts
const initialState: TripsState = {
  activeDayId: null, activeTab: 'itinerary', placesView: 'map',
  placeCategoryFilter: 'all', activeStopId: null,
  createTripOpen: false, addMode: false, itineraryMapCollapsed: false,
  stopEditorStopId: null,
}
```

Add the reducer (after `setAddMode`):

```ts
    setAddMode(s, a: PayloadAction<boolean>) { s.addMode = a.payload },
    setItineraryMapCollapsed(s, a: PayloadAction<boolean>) { s.itineraryMapCollapsed = a.payload },
```

Add the action to the exported destructure:

```ts
export const {
  setActiveDay, setActiveTab, setPlacesView, setPlaceCategoryFilter,
  setActiveStop, setCreateTripOpen, setAddMode, setItineraryMapCollapsed, setStopEditor,
} = tripsSlice.actions
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npm test -- src/pages/trips/tripsSlice.test.ts`
Expected: PASS (both new tests + the existing add-mode tests).

- [ ] **Step 5: Commit**

```bash
cd frontend
git add src/pages/trips/tripsSlice.ts src/pages/trips/tripsSlice.test.ts
git commit -m "feat(trips): add itineraryMapCollapsed UI state (default expanded)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: TripMap — `gestureHandling` prop + `MapAutoResize` child

**Files:**
- Modify: `frontend/src/pages/trips/components/TripMap.tsx`

**Interfaces:**
- Consumes: nothing.
- Produces: new optional prop `gestureHandling?: string` on `TripMap` (default `'greedy'`); a private `MapAutoResize` component rendered inside `<Map>` that forces a Google Maps re-layout on container resize.

> No unit-test harness exists for components (no jsdom/@testing-library — verified). This task is gated by `npm run build` (typecheck) + `npm run lint` + manual confirmation that the desktop and places→map maps are unchanged.

- [ ] **Step 1: Add the `MapAutoResize` component**

In `frontend/src/pages/trips/components/TripMap.tsx`, after the `FitBounds` function (currently ends ~line 80), add:

```tsx
// @vis.gl/react-google-maps (v1.8.3) ships no ResizeObserver, and its moveCamera
// re-layout self-heal only runs under `reuseMaps` (which we do not enable). So when the
// map container is resized — e.g. the itinerary band collapsing/expanding — nothing
// repaints the newly-revealed tiles and they stay grey. Observe the container and force a
// no-op camera move (the library's own documented re-layout remedy) which keeps the view.
function MapAutoResize() {
  const map = useMap()
  useEffect(() => {
    if (!map || typeof ResizeObserver === 'undefined') return
    const el = map.getDiv()
    if (!el) return
    let raf = 0
    const ro = new ResizeObserver(() => {
      cancelAnimationFrame(raf)
      raf = requestAnimationFrame(() => map.moveCamera({}))
    })
    ro.observe(el)
    return () => {
      ro.disconnect()
      cancelAnimationFrame(raf)
    }
  }, [map])
  return null
}
```

(`useEffect` and `useMap` are already imported at the top of the file — no import change.)

- [ ] **Step 2: Add the `gestureHandling` prop to the `TripMap` signature**

In the props type (the object type after `export function TripMap({...}: {`), add the optional prop next to `addMode`:

```tsx
  addMode?: boolean
  gestureHandling?: string
  tripId?: string
```

In the destructured parameters (the `{...}` before the `: {`), add the default:

```tsx
  addMode = false,
  gestureHandling = 'greedy',
  tripId,
```

- [ ] **Step 3: Use the prop and render `MapAutoResize`**

Change the hardcoded attribute on `<Map>` (currently `gestureHandling="greedy"`) to:

```tsx
          gestureHandling={gestureHandling}
```

Render `MapAutoResize` as the first child inside `<Map>` — immediately before the `{routeMode ? (` block:

```tsx
        >
          <MapAutoResize />
          {routeMode ? (
```

- [ ] **Step 4: Typecheck, build, and lint**

Run: `npm run build`
Expected: PASS — `tsc -b` reports no errors, `vite build` completes. (`map.moveCamera({})` typechecks: `CameraOptions` fields are all optional.)

Run: `npm run lint`
Expected: clean (no unused vars; the `useEffect` cleanup returns a disposer).

- [ ] **Step 5: Manual sanity (desktop + places→map unchanged)**

Run `npm run dev`, open a trip on a **desktop** width: the right-pane itinerary map and the mobile places→map view both still render and pan normally (they inherit the default `gestureHandling='greedy'`). No visual change expected from this task alone.

- [ ] **Step 6: Commit**

```bash
cd frontend
git add src/pages/trips/components/TripMap.tsx
git commit -m "feat(trips): TripMap gains gestureHandling prop + auto-resize re-layout

Adds a ResizeObserver child that calls map.moveCamera({}) on container
resize so tiles never stay grey when the container height changes
(@vis.gl v1.8.3 has no ResizeObserver and its self-heal needs reuseMaps).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: The map band in `ItineraryTab` (icons + `DayRoute` type + render + CSS)

**Files:**
- Modify: `frontend/src/pages/trips/components/TripFormIcons.tsx` (add 3 icons)
- Modify: `frontend/src/pages/trips/hooks/useDayRoute.ts` (export `DayRoute` type)
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx` (render the band)
- Modify: `frontend/src/pages/trips/TripDetailPage.css` (band + control CSS)

**Interfaces:**
- Consumes: `setItineraryMapCollapsed` + `itineraryMapCollapsed` (Task 1); `TripMap`'s `gestureHandling` prop (Task 2).
- Produces: `export type DayRoute = ReturnType<typeof useDayRoute>`; `ItineraryTab` now accepts an optional `dayRoute?: DayRoute` prop and renders a `.itin-map-band` **only when `dayRoute` is present** (so it stays dormant until Task 4 wires it on mobile).

> The band renders nothing until Task 4 passes `dayRoute`, so this task changes no visible behavior. Gate: `npm run build` + `npm run lint` + `npm test` (existing tests still pass).

- [ ] **Step 1: Add the three inline SVG icons**

Append to `frontend/src/pages/trips/components/TripFormIcons.tsx` (uses the file's existing `base` spread):

```tsx
/** Chevron up — collapse the itinerary map band. */
export function ChevronUpIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M6 15l6-6 6 6" />
    </svg>
  )
}

/** Chevron down — expand the collapsed itinerary map band. */
export function ChevronDownIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M6 9l6 6 6-6" />
    </svg>
  )
}

/** Folded map — lead glyph on the collapsed "show route map" strip. */
export function MapRouteIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M9 4 4 6v14l5-2 6 2 5-2V4l-5 2-6-2z" />
      <path d="M9 4v14M15 6v14" />
    </svg>
  )
}
```

- [ ] **Step 2: Export the `DayRoute` type**

At the end of `frontend/src/pages/trips/hooks/useDayRoute.ts`, add:

```ts
export type DayRoute = ReturnType<typeof useDayRoute>
```

- [ ] **Step 3: Wire imports + the collapse selector into `ItineraryTab.tsx`**

Add/extend imports at the top of `frontend/src/pages/trips/components/ItineraryTab.tsx`:

```tsx
import {setActiveDay, setStopEditor, setItineraryMapCollapsed} from '../tripsSlice'
import {TripMap} from './TripMap'
import {ChevronUpIcon, ChevronDownIcon, MapRouteIcon} from './TripFormIcons'
import type {DayRoute} from '../hooks/useDayRoute'
```

Change the component signature:

```tsx
export function ItineraryTab({tripId, dayRoute}: {tripId: string; dayRoute?: DayRoute}) {
```

Add the selector alongside the other `useAppSelector` calls near the top of the body:

```tsx
  const mapCollapsed = useAppSelector((s) => s.trips.itineraryMapCollapsed)
```

- [ ] **Step 4: Render the band between the day-tabs and the summary bar**

In the returned JSX, immediately **after** the day-tabs `<SegmentedTabs .../>` (the one whose options are `วัน {i+1}`) and **before** `<div className="day-summary">`, insert:

```tsx
      {dayRoute && (
        <div className={`itin-map-band${mapCollapsed ? ' collapsed' : ''}`}>
          <TripMap
            places={places ?? []}
            route={dayRoute.route}
            segments={dayRoute.segments}
            gestureHandling="cooperative"
          />
          {mapCollapsed ? (
            <button
              type="button"
              className="itin-map-strip"
              aria-label="แสดงแผนที่เส้นทาง"
              aria-expanded={false}
              onClick={() => dispatch(setItineraryMapCollapsed(false))}
            >
              <MapRouteIcon className="itin-map-strip-lead" />
              <span>แสดงแผนที่เส้นทาง</span>
              <ChevronDownIcon className="itin-map-strip-chev" />
            </button>
          ) : (
            <button
              type="button"
              className="itin-map-collapse"
              aria-label="ย่อแผนที่"
              aria-expanded={true}
              onClick={() => dispatch(setItineraryMapCollapsed(true))}
            >
              <ChevronUpIcon />
            </button>
          )}
        </div>
      )}
```

(`places` and `dispatch` already exist in `ItineraryTab` — `places` from `useListTripPlacesQuery`, `dispatch` from `useAppDispatch`. No summary props are passed, so `TripMap`'s `.map-day-card` overlay stays suppressed and the dark `.day-summary` bar below owns the day summary.)

- [ ] **Step 5: Add the band + control CSS**

Append to `frontend/src/pages/trips/TripDetailPage.css` (loaded after `trips-tokens.css`, so it wins equal-specificity ties):

```css
/* -------- Mobile/tablet itinerary route-map band (ADR-026, issue #8) -------- */
/* Scoped to .itinerary-tab so it never matches the mobile places->map (.trip-places)
   or the desktop right-pane map (.trip-detail-col-right). min-height:0 is REQUIRED —
   the base .trip-map carries min-height 320px (280 <=479px) which would floor the box. */
.itinerary-tab .itin-map-band {
  position: relative;
  flex: none;
  height: 188px;
  min-height: 0;
  overflow: hidden;
  border-radius: 12px;
  transition: height 0.2s ease;
}
.itinerary-tab .itin-map-band.collapsed { height: 44px; }
.itinerary-tab .itin-map-band .trip-map {
  height: 100%;
  min-height: 0;
}
/* Collapse affordance (expanded state) — small round icon button, top-right over the map */
.itinerary-tab .itin-map-collapse {
  position: absolute;
  top: 10px;
  right: 10px;
  z-index: 2;
  width: 30px;
  height: 30px;
  border: 0;
  border-radius: 9px;
  background: rgba(255, 255, 255, 0.95);
  color: var(--ink);
  box-shadow: 0 2px 8px rgba(15, 23, 42, 0.18);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.itinerary-tab .itin-map-collapse svg { width: 18px; height: 18px; }
/* Expand affordance (collapsed state) — opaque full-band strip over the clipped map */
.itinerary-tab .itin-map-strip {
  position: absolute;
  inset: 0;
  z-index: 2;
  width: 100%;
  height: 100%;
  border: 0;
  background: var(--surface);
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 14px;
  cursor: pointer;
  font-size: 12.5px;
  font-weight: 700;
  color: var(--teal-deep);
}
.itinerary-tab .itin-map-strip span { flex: 1; text-align: left; }
.itinerary-tab .itin-map-strip svg { width: 16px; height: 16px; flex: none; }
.itinerary-tab .itin-map-strip-chev { color: var(--muted); }
```

- [ ] **Step 6: Typecheck, build, lint, and run existing tests**

Run: `npm run build`
Expected: PASS (`tsc -b` clean, `vite build` completes).

Run: `npm run lint`
Expected: clean.

Run: `npm test`
Expected: PASS — all existing suites (incl. Task 1's new reducer tests) still pass; nothing renders the band yet, so no behavior changed.

- [ ] **Step 7: Commit**

```bash
cd frontend
git add src/pages/trips/components/TripFormIcons.tsx src/pages/trips/hooks/useDayRoute.ts src/pages/trips/components/ItineraryTab.tsx src/pages/trips/TripDetailPage.css
git commit -m "feat(trips): collapsible route-map band in ItineraryTab (dormant until wired)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Wire `dayRoute` into the mobile itinerary (band goes live — fixes #8)

**Files:**
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx`

**Interfaces:**
- Consumes: `ItineraryTab`'s new `dayRoute?: DayRoute` prop (Task 3); the already-computed `dayRoute = useDayRoute(tripId)` (existing, top of `TripDetailPage`).
- Produces: nothing (leaf integration). This is the change that makes the band appear on mobile/tablet.

> This is the go-live task. Gate: `npm run build` + `npm run lint`, then manual verification on a mobile viewport (the project's verify-in-app habit; no component/e2e harness for this — mobile e2e is deferred to Phase 2 per the spec).

- [ ] **Step 1: Pass `dayRoute` in the mobile branch only**

In `frontend/src/pages/trips/TripDetailPage.tsx`, the mobile/tablet branch renders (currently):

```tsx
      {tab === 'itinerary' && <ItineraryTab tripId={tripId} />}
```

Change **only this mobile-branch line** to pass the already-computed route:

```tsx
      {tab === 'itinerary' && <ItineraryTab tripId={tripId} dayRoute={dayRoute} />}
```

Leave the **desktop** branch's `<ItineraryTab tripId={tripId} />` (in the left column) **unchanged** — no `dayRoute` prop there, so no band renders in the desktop left column (the desktop map remains the right pane).

- [ ] **Step 2: Typecheck, build, lint**

Run: `npm run build`
Expected: PASS.

Run: `npm run lint`
Expected: clean.

- [ ] **Step 3: Manual verification on a mobile viewport**

Run `npm run dev`. Open a trip detail page (a trip with an itinerary day that has ≥1 stop) and verify at a **mobile width (<640px)** and a **tablet width (640–1023px)**:

1. **Band visible by default** on the แผนเที่ยว (itinerary) tab, ~188px, below the day-tabs and above the dark summary bar, showing numbered pins + route — **this is the #8 fix**.
2. Tap the **chevron (top-right)** → band collapses to the 44px **"แสดงแผนที่เส้นทาง"** strip; the stop list gets more room.
3. Tap the **strip** → band re-expands and **map tiles repaint (no grey)** — confirms `MapAutoResize`.
4. **One-finger vertical swipe over the map scrolls the page** (not trapped) — confirms `gestureHandling="cooperative"`.
5. At **desktop width (≥1024px)** the layout is unchanged — map is the right pane, no band in the left column.
6. Reload the page → band is **expanded again** (state not persisted), confirming a fresh open always shows the map.

(If the route reads too zoomed-out or top callouts clip at 188px, that's the optional `fitPadding` refinement noted in the spec §6 — out of scope here.)

- [ ] **Step 4: Commit**

```bash
cd frontend
git commit -am "fix(trips): show route-map band on mobile/tablet itinerary (closes #8)

The itinerary tab is the default tab and the Map-Forward hero, but on
mobile/tablet it rendered no map (route map was desktop-right-pane only).
Pass the already-computed dayRoute into ItineraryTab on the mobile branch
so the collapsible band renders. Desktop unchanged.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage** — every spec §4 change maps to a task: §4.1 tripsSlice → Task 1; §4.2 TripMap `gestureHandling` + `MapAutoResize` → Task 2; §4.3 `DayRoute` export → Task 3 Step 2; §4.4 TripDetailPage wiring → Task 4; §4.5 ItineraryTab band + controls → Task 3 Steps 3–4; §4.6 CSS → Task 3 Step 5. §7 testing → Task 1 (reducer) + Task 4 Step 3 (manual); §8 mobile e2e correctly deferred. No gaps.

**2. Placeholder scan** — every code step shows complete code; every command has expected output. Icons are concrete inline SVG (no "TBD"). No "similar to Task N".

**3. Type consistency** — `itineraryMapCollapsed` / `setItineraryMapCollapsed` spelled identically across Tasks 1, 3. `DayRoute` (Task 3 Step 2) matches the `dayRoute?: DayRoute` prop (Tasks 3, 4). `gestureHandling` prop (Task 2) matches its use in the band (`gestureHandling="cooperative"`, Task 3). CSS class names `.itin-map-band` / `.collapsed` / `.itin-map-collapse` / `.itin-map-strip` / `.itin-map-strip-chev` / `.itin-map-strip-lead` match between the JSX (Task 3 Step 4) and CSS (Task 3 Step 5).

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-04-mobile-itinerary-map-band.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
