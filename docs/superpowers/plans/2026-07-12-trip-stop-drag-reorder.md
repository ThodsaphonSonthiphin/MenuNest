# Trip Stop Drag-and-Drop Reorder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user drag an itinerary Stop to a new position within a Day (replacing the ▲▼ step-buttons), persist the new order, and show a full-view loading state until the server-recomputed times land.

**Architecture:** Frontend-only. Build a sortable stop list on **@dnd-kit** (`DndContext` + `SortableContext` + `useSortable`). Each Stop card is a sortable item activated by a dedicated drag handle; dropping computes the new id order with a pure helper and calls the **existing** `reorderStops` RTK mutation, which invalidates `TripItinerary` and refetches. A local flag drives a full-view loading overlay across the POST **and** the refetch (ADR-045). The backend `ReorderStops` use case and `POST /api/trips/{tripId}/days/{dayId}/reorder` endpoint are reused unchanged.

**Tech Stack:** React 19.2, TypeScript, RTK Query, Vite, Vitest, Playwright, `@dnd-kit/core` + `@dnd-kit/sortable` + `@dnd-kit/modifiers` + `@dnd-kit/utilities`.

## Global Constraints

- **Frontend-only.** Do NOT change the backend C# `ReorderStops` handler/command/controller. The RTK mutation `reorderStops` keeps its existing `invalidatesTags` (ADR-045).
- **Icons are SVG components, never emoji glyphs** (project rule). The drag handle uses a new inline-SVG `GripIcon`.
- **UI copy is Thai**; code, ADRs, commit messages, and this plan stay English.
- **Governing ADRs:** 043 (@dnd-kit), 044 (drag handle replaces ▲▼, keyboard preserved), 045 (full-view loading, no optimistic patch, no stale times), 046 (vertical-axis only, single-Day, Legs hidden during active drag).
- **Commits reference the tracking issue** (project rule). Open the issue in the Prep step; use its number `#<n>` in every commit; the feature-completing commit uses `(closes #<n>)`.
- **Commit narrowly:** always `git add <explicit paths>` — never `git add -A`/`.`. Do not stage `daily-state.md` or `AGENTS.md`.
- **Pre-commit hook runs the full suite** (backend build+test, `tsc -b`, `vite build`, ~40s) on every commit. Expect the wait; never `--no-verify`.

---

## Prep: open the tracking issue

- [ ] **Step 1: Create the GitHub issue and capture its number**

Run (from repo root):

```bash
gh issue create \
  --title "Trips: drag-and-drop reorder for itinerary Stops" \
  --body "Replace the ▲▼ step-buttons on itinerary Stops with drag-and-drop reordering (@dnd-kit). Frontend-only — reuses the existing reorderStops endpoint. Design: docs/superpowers/specs/2026-07-12-trip-stop-drag-reorder-design.md; ADR-043..046."
```

Note the returned issue number — referred to below as `#<n>`. Substitute the real number in every commit message.

---

## Task 1: `computeReorder` pure helper

Pure, dependency-free reorder logic so the drop decision is unit-testable without simulating drag gestures.

**Files:**
- Create: `frontend/src/pages/trips/lib/reorder.ts`
- Test: `frontend/src/pages/trips/lib/reorder.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: `computeReorder(ids: string[], activeId: string, overId: string): string[] | null` — returns the new id order after moving `activeId` to `overId`'s slot, or `null` when the order would not change (`activeId === overId`) or either id is absent.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/trips/lib/reorder.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {computeReorder} from './reorder'

describe('computeReorder', () => {
  const ids = ['a', 'b', 'c', 'd']

  it('moves an item down to the target slot', () => {
    expect(computeReorder(ids, 'a', 'c')).toEqual(['b', 'c', 'a', 'd'])
  })

  it('moves an item up to the target slot', () => {
    expect(computeReorder(ids, 'd', 'b')).toEqual(['a', 'd', 'b', 'c'])
  })

  it('returns null when active and over are the same', () => {
    expect(computeReorder(ids, 'b', 'b')).toBeNull()
  })

  it('returns null when the active id is not in the list', () => {
    expect(computeReorder(ids, 'x', 'c')).toBeNull()
  })

  it('returns null when the over id is not in the list', () => {
    expect(computeReorder(ids, 'a', 'x')).toBeNull()
  })

  it('preserves length and membership', () => {
    const out = computeReorder(ids, 'a', 'd')!
    expect(out).toHaveLength(4)
    expect([...out].sort()).toEqual(['a', 'b', 'c', 'd'])
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run (cwd `frontend`): `npx vitest run src/pages/trips/lib/reorder.test.ts`
Expected: FAIL — cannot resolve `./reorder` / `computeReorder is not a function`.

- [ ] **Step 3: Write the minimal implementation**

Create `frontend/src/pages/trips/lib/reorder.ts`:

```ts
// Pure reorder logic for the itinerary drag-and-drop (ADR-043/046). Kept free of
// @dnd-kit so the drop decision is unit-testable without simulating gestures.

/**
 * Move `activeId` into `overId`'s slot within `ids`. Returns the new order, or
 * `null` when nothing would change (same id) or either id is not present.
 */
export function computeReorder(ids: string[], activeId: string, overId: string): string[] | null {
  if (activeId === overId) return null
  const from = ids.indexOf(activeId)
  const to = ids.indexOf(overId)
  if (from < 0 || to < 0) return null
  const next = ids.slice()
  next.splice(to, 0, next.splice(from, 1)[0])
  return next
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (cwd `frontend`): `npx vitest run src/pages/trips/lib/reorder.test.ts`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/reorder.ts frontend/src/pages/trips/lib/reorder.test.ts
git commit -m "feat(trips): computeReorder pure helper for stop drag reorder (#<n>)"
```

---

## Task 2: Add @dnd-kit dependencies

Install the libraries and prove they build under React 19 before any code depends on them (ADR-043 planning gate).

**Files:**
- Modify: `frontend/package.json` (+ `frontend/package-lock.json`) — via `npm install`.

**Interfaces:**
- Consumes: nothing.
- Produces: `@dnd-kit/core`, `@dnd-kit/sortable`, `@dnd-kit/modifiers`, `@dnd-kit/utilities` available to import.

- [ ] **Step 1: Install the packages**

Run (cwd `frontend`):

```bash
npm install @dnd-kit/core @dnd-kit/sortable @dnd-kit/modifiers @dnd-kit/utilities
```

- [ ] **Step 2: Verify React-19 compatibility via a clean build**

Run (cwd `frontend`): `npm run build`
Expected: PASS — `tsc -b` reports no errors and `vite build` completes. Peer-dependency **warnings** from npm are acceptable; a **build failure** or a TypeScript error involving `@dnd-kit/*` types is a blocker — stop and reassess the version (e.g. pin `@dnd-kit/core@^6`).

- [ ] **Step 3: Commit**

```bash
git add frontend/package.json frontend/package-lock.json
git commit -m "build(frontend): add @dnd-kit for stop drag-reorder (#<n>)"
```

---

## Task 3: Sortable Stop card + drag handle

Turn `ItineraryStopCard` into a `@dnd-kit` sortable item with a dedicated drag handle, add the `GripIcon`, and add the handle / dragging / leg-hide CSS. Removes the ▲▼ props.

**Files:**
- Modify: `frontend/src/pages/trips/components/ItineraryStopCard.tsx` (full rewrite below)
- Modify: `frontend/src/pages/trips/components/TripFormIcons.tsx` (append `GripIcon`)
- Modify: `frontend/src/pages/trips/trips-tokens.css` (replace `.stop-reorder` block; add dragging + leg-hide rules)

**Interfaces:**
- Consumes: `@dnd-kit/sortable` `useSortable`, `@dnd-kit/utilities` `CSS` (Task 2).
- Produces:
  - `GripIcon({className?: string})` — inline-SVG grip glyph.
  - `ItineraryStopCard` **new prop `id: string`** (the Stop id, used by `useSortable`); props `onUp`, `onDown`, `canUp`, `canDown` are **removed**. Root element renders `data-testid="itin-stop-card"` and `data-stop-id={id}`; the handle renders `data-testid="stop-drag-handle"`.

- [ ] **Step 1: Append `GripIcon` to `TripFormIcons.tsx`**

Add this export at the end of `frontend/src/pages/trips/components/TripFormIcons.tsx` (after the last icon):

```tsx
/** Grip dots — drag handle for reordering Stops (ADR-044). Uses filled dots, not the stroked `base`. */
export function GripIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" aria-hidden focusable={false} className={className}>
      <circle cx="9" cy="5" r="1.6" />
      <circle cx="15" cy="5" r="1.6" />
      <circle cx="9" cy="12" r="1.6" />
      <circle cx="15" cy="12" r="1.6" />
      <circle cx="9" cy="19" r="1.6" />
      <circle cx="15" cy="19" r="1.6" />
    </svg>
  )
}
```

- [ ] **Step 2: Rewrite `ItineraryStopCard.tsx`**

Replace the entire contents of `frontend/src/pages/trips/components/ItineraryStopCard.tsx` with:

```tsx
// frontend/src/pages/trips/components/ItineraryStopCard.tsx
import {useSortable} from '@dnd-kit/sortable'
import {CSS} from '@dnd-kit/utilities'
import type {TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {FlagReason, FlagSeverity, StopFlag, TimingFlag} from '../hooks/useSchedule'
import {catEmoji} from '../placeCategory'
import {flagText} from '../timingFlag'
import {NavIcon} from './NavIcon'
import {ClockIcon, LockIcon, MoonIcon, CheckIcon} from './FlagIcons'
import {GripIcon} from './TripFormIcons'
import {WeatherChip} from './WeatherChip'
import {formatDurationMinutes} from '../utils/time'

// Reason → icon component. `typeof LockIcon` avoids naming the JSX namespace.
const REASON_ICON: Record<FlagReason, typeof LockIcon> = {
  overflow: MoonIcon,
  closed: LockIcon,
  'off-window': ClockIcon,
}
// Severity → CSS class. NEVER interpolate the raw severity string (enum ≠ class name).
const CARD_CLASS: Record<FlagSeverity, string> = {problem: 'bad', suggestion: 'warn'}

function FlagNote({flag}: {flag: TimingFlag}) {
  const Icon = REASON_ICON[flag.reason]
  const {reasonLine, fixLine} = flagText(flag)
  return (
    <div className={`flag-note${flag.severity === 'problem' ? ' bad' : ''}`}>
      <Icon />
      <span><b>{reasonLine}</b> <span className="fix">{fixLine}</span></span>
    </div>
  )
}

export function ItineraryStopCard({
  id,
  place,
  arrival,
  depart,
  dwell,
  flag,
  onEdit,
  navUrl,
  onNavigate,
  nowReading,
  arrivalReading,
  weatherLoading = false,
  isVisited,
  onToggleVisited,
}: {
  id: string
  place: TripPlaceDto
  arrival: string
  depart: string
  dwell: number
  flag: StopFlag
  onEdit: () => void
  navUrl: string | null
  onNavigate?: () => void
  nowReading?: WeatherReadingDto
  arrivalReading?: WeatherReadingDto
  weatherLoading?: boolean
  isVisited: boolean
  onToggleVisited: (next: boolean) => void
}) {
  const {attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging} =
    useSortable({id})
  const style = {transform: CSS.Transform.toString(transform), transition}

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`stop-card${flag ? ' ' + CARD_CLASS[flag.severity] : ''}${isVisited ? ' visited' : ''}${isDragging ? ' dragging' : ''}`}
      data-testid="itin-stop-card"
      data-stop-id={id}
    >
      <label className="stop-check">
        <input
          type="checkbox"
          checked={isVisited}
          onChange={(e) => onToggleVisited(e.target.checked)}
          aria-label={`มาแล้ว: ${place.name}`}
        />
      </label>
      <div className="stop-rail">
        <div className="stop-arr">{arrival}</div>
        <div className="stop-dep">→{depart}</div>
      </div>
      <button className="stop-body" onClick={onEdit}>
        <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
        <div className="stop-chips">
          {isVisited && <span className="chip visited"><CheckIcon />มาแล้ว</span>}
          <span className="chip dwell">⏱ อยู่ {formatDurationMinutes(dwell)}</span>
          <WeatherChip kind="now" reading={nowReading} isLoading={weatherLoading} />
          <WeatherChip kind="arr" reading={arrivalReading} isLoading={weatherLoading} />
        </div>
        {flag && <FlagNote flag={flag} />}
      </button>
      {navUrl ? (
        <a
          className="stop-nav"
          href={navUrl}
          target="_blank"
          rel="noopener noreferrer"
          aria-label="นำทาง"
          onClick={(e) => {
            e.stopPropagation()
            onNavigate?.()
          }}
        >
          <NavIcon />
        </a>
      ) : (
        <span className="stop-nav" role="img" aria-label="ไม่มีพิกัดสำหรับนำทาง" aria-disabled="true">
          <NavIcon />
        </span>
      )}
      <button
        ref={setActivatorNodeRef}
        type="button"
        className="stop-drag-handle"
        aria-label="ลากเพื่อจัดลำดับ"
        data-testid="stop-drag-handle"
        {...attributes}
        {...listeners}
      >
        <GripIcon />
      </button>
    </div>
  )
}
```

- [ ] **Step 3: Replace the `.stop-reorder` CSS block**

In `frontend/src/pages/trips/trips-tokens.css`, **delete** the whole block that starts with the comment `/* reorder — subtle ghost arrows (drag-to-reorder is Phase 2) */` and its `.stop-reorder` + `.stop-reorder button` + `.stop-reorder button:hover…` + `.stop-reorder button:disabled` rules, and **replace** it with:

```css
/* drag handle — grab to reorder Stops (ADR-044) */
.stop-drag-handle {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  border: 0;
  background: transparent;
  color: #cbd5e1;
  cursor: grab;
  touch-action: none; /* hand pointer moves to @dnd-kit, not the browser's scroll */
  transition: background 0.12s ease, color 0.12s ease;
}
.stop-drag-handle:hover { background: var(--teal-soft); color: var(--teal-deep); }
.stop-drag-handle:focus-visible { outline: 2px solid var(--teal-deep); outline-offset: -2px; }
.stop-drag-handle:active { cursor: grabbing; }
.stop-drag-handle svg { width: 18px; height: 18px; }

/* lifted card while dragging */
.stop-card.dragging { box-shadow: 0 10px 26px rgba(2, 32, 27, 0.18); opacity: 0.97; }

/* during an active drag keep each Leg's space but hide its (now-stale) content —
   no layout jump, no misleading travel time (ADR-046) */
.stop-list.dragging .travel-leg { visibility: hidden; }
```

- [ ] **Step 4: Verify the build (type-check + bundle)**

Run (cwd `frontend`): `npm run build`
Expected: PASS. (A TS error like "Property 'onUp' is missing" from `ItineraryTab.tsx` is EXPECTED here — it is fixed in Task 4. If you are running tasks strictly one-at-a-time, this build stays red until Task 4; in that case verify only `npx tsc --noEmit -p tsconfig.app.json 2>&1 | grep ItineraryStopCard` shows no errors *inside this file*, and proceed. Otherwise complete Task 4 before the green build.)

- [ ] **Step 5: Commit** (stage with Task 4 if you deferred the green build)

```bash
git add frontend/src/pages/trips/components/ItineraryStopCard.tsx frontend/src/pages/trips/components/TripFormIcons.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): sortable stop card with drag handle (#<n>)"
```

> Note: the pre-commit hook runs `vite build`. If Task 3 is committed alone while `ItineraryTab.tsx` still passes the old props, the hook build fails. Prefer committing Task 3 and Task 4 together (stage both sets of paths, one commit `feat(trips): drag-to-reorder stops with full-view loading (closes #<n>)`), OR land Task 4's `ItineraryTab.tsx` edit before committing. This plan assumes the combined-commit path; adjust the two commit messages if you split them.

---

## Task 4: Wire the DnD context, drop handler, and full-view loading

Wrap the stop list in `DndContext` + `SortableContext`, drive the drop through `computeReorder` → `reorderStops`, hide Legs during an active drag, and show a full-view loading overlay across the POST and the refetch. Remove the `move()` helper and the ▲▼ wiring.

**Files:**
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css` (append overlay + spinner rules)

**Interfaces:**
- Consumes: `computeReorder` (Task 1); `ItineraryStopCard` new `id` prop, no `onUp/onDown/canUp/canDown` (Task 3); `@dnd-kit/core`, `@dnd-kit/sortable`, `@dnd-kit/modifiers` (Task 2); existing `useReorderStopsMutation`, `useGetItineraryQuery`.
- Produces: end-to-end drag reorder on the itinerary.

- [ ] **Step 1: Update the imports**

In `frontend/src/pages/trips/components/ItineraryTab.tsx`, change the React import and add the dnd-kit + helper imports. Replace:

```tsx
import {useMemo, useState} from 'react'
```

with:

```tsx
import {Fragment, useMemo, useState} from 'react'
import {
  DndContext,
  closestCenter,
  PointerSensor,
  KeyboardSensor,
  useSensor,
  useSensors,
  type DragStartEvent,
  type DragEndEvent,
} from '@dnd-kit/core'
import {SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy} from '@dnd-kit/sortable'
import {restrictToVerticalAxis} from '@dnd-kit/modifiers'
import {computeReorder} from '../lib/reorder'
```

- [ ] **Step 2: Add `isFetching` to the itinerary query and the reorder mutation's loading flag**

Replace:

```tsx
  const {data: days, isLoading: itineraryLoading, error: itineraryError} = useGetItineraryQuery({tripId, tz: getViewerTimeZone(), lat: viewerLocation?.lat, lng: viewerLocation?.lng})
  const {data: places} = useListTripPlacesQuery(tripId)
  const {data: trips} = useListTripsQuery()
  const [reorder] = useReorderStopsMutation()
```

with:

```tsx
  const {data: days, isLoading: itineraryLoading, isFetching: itineraryFetching, error: itineraryError} = useGetItineraryQuery({tripId, tz: getViewerTimeZone(), lat: viewerLocation?.lat, lng: viewerLocation?.lng})
  const {data: places} = useListTripPlacesQuery(tripId)
  const {data: trips} = useListTripsQuery()
  const [reorder, {isLoading: reorderLoading}] = useReorderStopsMutation()
```

- [ ] **Step 3: Add DnD state, the loading-flag reset, and sensors**

Immediately after the existing `const [actionError, setActionError] = useState<string | null>(null)` line, add:

```tsx
  const [activeDragId, setActiveDragId] = useState<string | null>(null)
  const [isReordering, setIsReordering] = useState(false)

  // Full-view loading spans BOTH the reorder POST (reorderLoading) and the
  // invalidation refetch that recomputes Legs/times (itineraryFetching). Once both
  // settle, drop the flag. `setIsReordering(true)` and the mutation dispatch happen
  // in the same event handler, so by the time this runs reorderLoading is already
  // true — no premature clear. Render-time reset (no effect), mirroring the
  // `lastDayId` pattern below. If a one-render flicker ever appears, switch the drop
  // handler to `await reorder(...).unwrap(); await refetch().unwrap()` in a finally.
  if (isReordering && !reorderLoading && !itineraryFetching) {
    setIsReordering(false)
  }

  const sensors = useSensors(
    // A few px of movement before a drag starts, so a tap/scroll is not misread.
    useSensor(PointerSensor, {activationConstraint: {distance: 6}}),
    useSensor(KeyboardSensor, {coordinateGetter: sortableKeyboardCoordinates}),
  )
```

- [ ] **Step 4: Replace the `move()` helper with the drag handlers**

Replace the whole `move` function:

```tsx
  const move = async (index: number, dir: -1 | 1) => {
    const ids = scheduled.map((s) => s.stop.id)
    const j = index + dir
    if (j < 0 || j >= ids.length) return
    ;[ids[index], ids[j]] = [ids[j], ids[index]]
    try {
      await reorder({tripId, dayId: resolvedDayId, orderedStopIds: ids}).unwrap()
    } catch (err) {
      setActionError(getErrorMessage(err))
    }
  }
```

with:

```tsx
  const handleDragStart = (e: DragStartEvent) => setActiveDragId(String(e.active.id))

  const handleDragEnd = async (e: DragEndEvent) => {
    setActiveDragId(null)
    const {active, over} = e
    if (!over) return
    const orderedStopIds = computeReorder(scheduled.map((s) => s.stop.id), String(active.id), String(over.id))
    if (!orderedStopIds) return
    setIsReordering(true)
    try {
      await reorder({tripId, dayId: resolvedDayId, orderedStopIds}).unwrap()
    } catch (err) {
      setActionError(getErrorMessage(err))
      setIsReordering(false) // no refetch fires on error — clear the loader now
    }
  }
```

- [ ] **Step 5: Wrap the stop list in the DnD context and update the card props**

Replace the whole `<div className="stop-list"> … </div>` block:

```tsx
      <div className="stop-list">
        {scheduled.map((s, i) => {
          const place = placesById[s.stop.tripPlaceId]
          const stopNav = place ? buildStopNavUrl(place, s.stop.travelModeToReach) : null
          return (
            <div key={s.stop.id}>
              {i > 0 && s.stop.legToReach && (
                <TravelLeg leg={s.stop.legToReach} mode={s.stop.travelModeToReach} />
              )}
              {place && (
                <ItineraryStopCard
                  place={place}
                  arrival={s.arrival}
                  depart={s.depart}
                  dwell={s.stop.dwellMinutes}
                  isVisited={s.stop.isVisited}
                  onToggleVisited={async (next) => {
                    try {
                      await setStopVisited({tripId, stopId: s.stop.id, isVisited: next}).unwrap()
                    } catch (err) {
                      setActionError(getErrorMessage(err))
                    }
                  }}
                  flag={s.flag}
                  onEdit={() => dispatch(setStopEditor(s.stop.id))}
                  onUp={() => move(i, -1)}
                  onDown={() => move(i, 1)}
                  canUp={i > 0}
                  canDown={i < scheduled.length - 1}
                  navUrl={stopNav}
                  onNavigate={() =>
                    appInsights.trackEvent(
                      {name: 'TripNavHandoff'},
                      {scope: 'stop', travelMode: s.stop.travelModeToReach, hasPlaceId: !!place.googlePlaceId},
                    )
                  }
                  nowReading={stopWeather[s.stop.id]?.now}
                  arrivalReading={stopWeather[s.stop.id]?.arrival}
                  weatherLoading={(stopWeather[s.stop.id]?.nowLoading ?? false) || (stopWeather[s.stop.id]?.arrivalLoading ?? false)}
                />
              )}
            </div>
          )
        })}
        {scheduled.length === 0 && (
          <p className="trips-empty">ยังไม่มีจุดแวะ — เพิ่มจากคลังสถานที่</p>
        )}
      </div>
```

with (note: `<Fragment>` sibling structure, `<DndContext>`/`<SortableContext>` wrappers, `dragging` class on the list, Legs suppressed via that class, `id` prop passed, ▲▼ props removed):

```tsx
      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        modifiers={[restrictToVerticalAxis]}
        onDragStart={handleDragStart}
        onDragEnd={handleDragEnd}
        accessibility={{
          announcements: {
            onDragStart: () => 'เริ่มลากจุดแวะ ใช้ลูกศรขึ้น–ลงเพื่อย้าย',
            onDragOver: () => 'กำลังย้ายจุดแวะ',
            onDragEnd: () => 'วางจุดแวะแล้ว กำลังคำนวณเวลาใหม่',
            onDragCancel: () => 'ยกเลิกการย้ายจุดแวะ',
          },
          screenReaderInstructions: {
            draggable: 'กดเว้นวรรคเพื่อยกจุดแวะ ใช้ลูกศรขึ้น–ลงเพื่อย้าย แล้วกดเว้นวรรคอีกครั้งเพื่อวาง หรือ Escape เพื่อยกเลิก',
          },
        }}
      >
        <SortableContext items={scheduled.map((s) => s.stop.id)} strategy={verticalListSortingStrategy}>
          <div className={`stop-list${activeDragId ? ' dragging' : ''}`}>
            {scheduled.map((s, i) => {
              const place = placesById[s.stop.tripPlaceId]
              const stopNav = place ? buildStopNavUrl(place, s.stop.travelModeToReach) : null
              return (
                <Fragment key={s.stop.id}>
                  {i > 0 && s.stop.legToReach && (
                    <TravelLeg leg={s.stop.legToReach} mode={s.stop.travelModeToReach} />
                  )}
                  {place && (
                    <ItineraryStopCard
                      id={s.stop.id}
                      place={place}
                      arrival={s.arrival}
                      depart={s.depart}
                      dwell={s.stop.dwellMinutes}
                      isVisited={s.stop.isVisited}
                      onToggleVisited={async (next) => {
                        try {
                          await setStopVisited({tripId, stopId: s.stop.id, isVisited: next}).unwrap()
                        } catch (err) {
                          setActionError(getErrorMessage(err))
                        }
                      }}
                      flag={s.flag}
                      onEdit={() => dispatch(setStopEditor(s.stop.id))}
                      navUrl={stopNav}
                      onNavigate={() =>
                        appInsights.trackEvent(
                          {name: 'TripNavHandoff'},
                          {scope: 'stop', travelMode: s.stop.travelModeToReach, hasPlaceId: !!place.googlePlaceId},
                        )
                      }
                      nowReading={stopWeather[s.stop.id]?.now}
                      arrivalReading={stopWeather[s.stop.id]?.arrival}
                      weatherLoading={(stopWeather[s.stop.id]?.nowLoading ?? false) || (stopWeather[s.stop.id]?.arrivalLoading ?? false)}
                    />
                  )}
                </Fragment>
              )
            })}
            {scheduled.length === 0 && (
              <p className="trips-empty">ยังไม่มีจุดแวะ — เพิ่มจากคลังสถานที่</p>
            )}
          </div>
        </SortableContext>
      </DndContext>
```

- [ ] **Step 6: Render the full-view loading overlay**

Immediately before the closing `</div>` of the `return (<div className="itinerary-tab"> … )` — i.e. after the `{editorStopId && ( <StopEditorDialog … /> )}` block and before the final `</div>` — add:

```tsx
      {isReordering && (
        <div className="itin-reorder-overlay" role="status" aria-live="polite">
          <span className="itin-reorder-spinner" aria-hidden="true" />
          <span>กำลังจัดลำดับใหม่…</span>
        </div>
      )}
```

- [ ] **Step 7: Append the overlay CSS**

Add to `frontend/src/pages/trips/trips-tokens.css`:

```css
/* full-view loading while a reorder POST + itinerary refetch settle (ADR-045) */
.itin-reorder-overlay {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  background: rgba(255, 255, 255, 0.72);
  backdrop-filter: blur(1.5px);
  color: var(--muted);
  font-size: 14px;
}
.itin-reorder-spinner {
  width: 30px;
  height: 30px;
  border: 3px solid var(--teal-soft);
  border-top-color: var(--teal-deep);
  border-radius: 50%;
  animation: itin-spin 0.7s linear infinite;
}
@keyframes itin-spin { to { transform: rotate(360deg); } }
```

- [ ] **Step 8: Verify the build**

Run (cwd `frontend`): `npm run build`
Expected: PASS — `tsc -b` clean (no unused `move`, no missing-prop errors) and `vite build` completes.

- [ ] **Step 9: Manual verification** (app running with a Day that has ≥2 Stops)

Start the app: `npm run dev` (cwd `frontend`). Open a Trip → Itinerary on a Day with ≥2 Stops and confirm:
1. Each Stop shows a grip handle where ▲▼ used to be; the ▲▼ buttons are gone.
2. Dragging a Stop by the handle reorders the list; movement is vertical only; the Leg chips disappear (space preserved, no jump) while dragging and reappear after drop.
3. On drop, a full-view loading overlay ("กำลังจัดลำดับใหม่…") shows and stays until the arrival times/Legs update to the recomputed values — it must NOT flash off before the times change. Reload the page; the new order persists.
4. Tapping the card body still opens the editor; the "มาแล้ว" checkbox and the nav icon still work and do not start a drag.
5. Keyboard: Tab to a handle, press Space (pick up), ArrowDown/ArrowUp (move), Space (drop) → order changes and persists; Escape cancels a pickup.

> If the overlay flickers off for one frame before times update (Step 9.3), apply the fallback noted in Step 3's comment: in `handleDragEnd`, pull `refetch` from `useGetItineraryQuery`, and after the POST do `await refetch().unwrap()` inside the `try`, moving `setIsReordering(false)` into a `finally` (and drop the render-time reset). Re-run Step 8 + 9.

- [ ] **Step 10: Commit** (feature-completing — closes the issue)

```bash
git add frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/components/ItineraryStopCard.tsx frontend/src/pages/trips/components/TripFormIcons.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): drag-to-reorder stops with full-view loading (closes #<n>)"
```

> If Task 3 was already committed separately and cleanly, stage only `ItineraryTab.tsx` + the overlay CSS here and keep the `(closes #<n>)` message.

---

## Task 5 (best-effort): keyboard-reorder E2E smoke

An E2E that drives reorder via the keyboard (reliable in Playwright, unlike pointer drag) and skips gracefully when the authed env has no Trip with ≥2 Stops — matching the repo's existing defensive E2E style (`e2e/budget.interactions.spec.ts`). Note: local runs have **no backend** (see `playwright.config.ts`), so this test **skips** locally; it exercises only in a seeded, authenticated environment.

**Files:**
- Create: `frontend/e2e/trips.reorder.spec.ts`

**Interfaces:**
- Consumes: `authedPage` fixture (`e2e/fixtures/healthFixture.ts`); `data-testid="itin-stop-card"`, `data-stop-id`, `data-testid="stop-drag-handle"` (Task 3).

- [ ] **Step 1: Write the spec**

Create `frontend/e2e/trips.reorder.spec.ts`:

```ts
import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

// Reads the current Stop order from the itinerary as an array of stop ids.
async function orderOf(page: import('@playwright/test').Page): Promise<string[]> {
  return page.getByTestId('itin-stop-card').evaluateAll((els) =>
    els.map((el) => el.getAttribute('data-stop-id') ?? ''),
  )
}

test.describe('Trips — itinerary reorder', () => {
  test('keyboard reorder moves the first Stop down and persists', async ({authedPage: page}) => {
    await page.goto('/trips')
    // Open the first trip if a trip list is shown; otherwise assume a trip route.
    const firstTrip = page.getByTestId('trip-card').first()
    if (await firstTrip.count()) await firstTrip.click()

    const cards = page.getByTestId('itin-stop-card')
    if (await cards.count() < 2) test.skip(true, 'needs a Day with ≥2 Stops (no seeded backend locally)')

    const before = await orderOf(page)

    // Pick up the first Stop's handle, move it down one slot, drop.
    const handle = page.getByTestId('stop-drag-handle').first()
    await handle.focus()
    await page.keyboard.press('Space')
    await page.keyboard.press('ArrowDown')
    await page.keyboard.press('Space')

    // Wait for the reorder overlay to appear and clear (recompute round-trip).
    await expect(page.locator('.itin-reorder-overlay')).toBeHidden({timeout: 10_000})

    const after = await orderOf(page)
    expect(after[0]).toBe(before[1])
    expect(after[1]).toBe(before[0])

    // Persists across reload.
    await page.reload()
    const reloaded = await orderOf(page)
    expect(reloaded.slice(0, 2)).toEqual(after.slice(0, 2))
  })
})
```

- [ ] **Step 2: Run it**

Run (cwd `frontend`): `npx playwright test e2e/trips.reorder.spec.ts`
Expected: PASS or SKIP — SKIP is acceptable locally (no seeded backend/trip). It must not FAIL on a wiring error. (If `data-testid="trip-card"` does not exist in the trips list, the test still proceeds when already on a trip route; if you know the real trip-list testid, substitute it.)

- [ ] **Step 3: Commit**

```bash
git add frontend/e2e/trips.reorder.spec.ts
git commit -m "test(trips): e2e keyboard reorder smoke (#<n>)"
```

---

## Self-Review

**1. Spec coverage**

| Spec item | Task |
|-----------|------|
| @dnd-kit sortable (ADR-043) | 2, 3, 4 |
| Drag handle replaces ▲▼; keyboard preserved (ADR-044) | 3 (handle + `GripIcon`, `setActivatorNodeRef`), 4 (`KeyboardSensor`, announcements) |
| Full-view loading, no optimism, no stale times (ADR-045) | 4 (Steps 3, 6, 7) |
| Vertical-axis, single-Day, Legs hidden during drag (ADR-046) | 4 (`restrictToVerticalAxis`, per-Day `resolvedDayId`, `.stop-list.dragging .travel-leg` hide) |
| Remove `move()` / ▲▼ props | 3 (props), 4 (Step 4, Step 5) |
| Thai copy + localized announcements | 3 (`aria-label`), 4 (announcements/instructions) |
| Icons are SVG, not emoji | 3 (`GripIcon`) |
| Unit test (pure reorder) | 1 |
| E2E keyboard reorder | 5 |
| Ticket reference in commits | Prep + every commit |

No gaps found.

**2. Placeholder scan:** `#<n>` is the tracking-issue number created in Prep (an external fact, substituted at execution) — not a content placeholder. No "TBD"/"add error handling"/"similar to Task N" placeholders; every code step shows full code.

**3. Type consistency:** `computeReorder(ids, activeId, overId): string[] | null` is defined in Task 1 and called with `(scheduled.map(s => s.stop.id), String(active.id), String(over.id))` in Task 4. `ItineraryStopCard`'s new `id: string` prop (Task 3) is passed in Task 4; removed props `onUp/onDown/canUp/canDown` are absent from both. `reorderLoading`/`itineraryFetching`/`isReordering`/`activeDragId` are declared before use. `GripIcon` (Task 3) is imported by `ItineraryStopCard` (Task 3). Consistent.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-12-trip-stop-drag-reorder.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
