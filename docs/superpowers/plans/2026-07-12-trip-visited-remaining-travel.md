# Visited → "ที่เหลือ" list & remaining-travel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When Trip Stops are ticked **Visited** (มาแล้ว), move them out of the active itinerary list into a collapsible "มาแล้ว" drawer, and show the day's travel figure as **เหลือเดินทาง** = the travel time over the not-yet-visited Stops only.

**Architecture:** Frontend-only, additive, no re-cascade (ADR-047). `useSchedule` gains one pure travel-sum helper; `ItineraryTab` partitions the already-computed `scheduled` array into remaining/done views, feeds only remaining Stops to the existing `@dnd-kit` `SortableContext`, and renders visited Stops in a collapsed drawer. Reorder rebuilds the full-day id list with visited Stops pinned (ADR-048) and reuses the existing `reorderStops` contract. Times, flags, weather, map and backend are untouched (ADR-008 preserved).

**Tech Stack:** React 19 + TypeScript, RTK Query (`api.ts`, hand-maintained), `@dnd-kit/core`+`/sortable` (shipped #31), Vitest (unit), CSS in `trips-tokens.css` (teal design tokens). Thai UI copy.

## Global Constraints

- **Frontend-only.** No backend, no API, no EF migration. `StopDto.isVisited: boolean` and `StopDto.legToReach: LegDto | null` already ship; `setStopVisited` (non-invalidating, optimistic — ADR-042) already exists in `api.ts`. Do **not** add or change any C# / SQL / `api.ts` endpoint.
- **No re-cascade (ADR-008 / ADR-047).** `computeSchedule` must not change; `isVisited` must never enter the arrival/leave cascade, Timing flags, Weather, Approach leg, or Current-time start. Only a *filtered re-sum* of existing Leg values changes, plus display partitioning.
- **No emoji for UI chrome (project rule).** New icons are SVG components (`CheckIcon`, `ChevronDownIcon`). The empty state uses `CheckIcon`, never 🎉. (Travel-mode glyphs 🚗🚶🚃 in `TravelLeg` and `catEmoji` are pre-existing accepted exceptions — reuse them, don't add new emoji.)
- **Zero-visited must not regress.** With no Stop visited, the summary reads exactly today's `เดินทางรวม {total}` and the drawer is absent.
- **Commits reference the ticket** (`(#24)`) and stage **explicit paths only** — never `git add -A`/`.`. Do not `--no-verify` (pre-commit runs backend build+test and frontend `tsc -b` + `vite build`, ~40s).
- **Confirmed UI source of truth:** `docs/mocks/trip-visited-remaining-mock.html` (แนวทาง 1).

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `frontend/src/pages/trips/hooks/useSchedule.ts` | Schedule cascade + travel sums | Add pure `sumTravelSeconds`; return `remainingTravelSeconds` |
| `frontend/src/pages/trips/hooks/useSchedule.test.ts` | Unit tests | Add `sumTravelSeconds` cases + no-coupling assertion |
| `frontend/src/pages/trips/lib/reorder.ts` | Pure reorder helpers | Add `reorderKeepingVisited` |
| `frontend/src/pages/trips/lib/reorder.test.ts` | Unit tests | Add `reorderKeepingVisited` cases |
| `frontend/src/pages/trips/components/TravelLeg.tsx` | Travel-leg pill between Stops | Add optional `note` prop (leading-Leg line) |
| `frontend/src/pages/trips/components/VisitedStopRow.tsx` | Slim non-draggable done row | **Create** |
| `frontend/src/pages/trips/components/ItineraryTab.tsx` | Itinerary orchestration | Partition, summary label, SortableContext=remaining, lead Leg, drawer, reorder wiring, all-visited empty state |
| `frontend/src/pages/trips/trips-tokens.css` | Trips design tokens + itinerary CSS | Add drawer / done-row / lead-leg / remaining-figure rules |

---

## Task 1: `sumTravelSeconds` — remaining-travel calculation (pure)

**Files:**
- Modify: `frontend/src/pages/trips/hooks/useSchedule.ts` (add export ~before `useSchedule`; wire the hook return ~155-158)
- Test: `frontend/src/pages/trips/hooks/useSchedule.test.ts`

**Interfaces:**
- Consumes: `StopDto` (already imported in `useSchedule.ts`), `LegDto`.
- Produces: `export function sumTravelSeconds(stops: StopDto[], opts?: {excludeVisited?: boolean}): number`; the `useSchedule` return object gains `remainingTravelSeconds: number` (Task 4 consumes it).

- [ ] **Step 1: Write the failing tests**

In `useSchedule.test.ts`, extend the `stop()` factory with a trailing optional `visited` param (default `false`, keeps every existing call valid), add `sumTravelSeconds` to the import, and add the describe block.

Change the import line (currently line 3):
```ts
import {computeSchedule, dayOfWeek, isOpenAt, offWindowFlag, closedFlag, composeFlags, sumTravelSeconds} from './useSchedule'
```
Change the factory (currently lines 6-10):
```ts
const stop = (id: string, seq: number, dwell: number, legSec: number | null, visited = false) => ({
  id, tripPlaceId: `p${id}`, sequence: seq, dwellMinutes: dwell,
  travelModeToReach: 'Drive' as const, isVisited: visited,
  legToReach: legSec == null ? null : {seconds: legSec, meters: 1000, encodedPolyline: null, source: 'Estimated' as const},
})
```
Append this block at the end of the file:
```ts
describe('sumTravelSeconds', () => {
  // A(✓) → B(✓) → C → D. Legs: A=none, B=12m, C=6m (into first remaining), D=18m.
  const stops = [
    stop('1', 0, 60, null, true),
    stop('2', 1, 45, 12 * 60, true),
    stop('3', 2, 90, 6 * 60, false),
    stop('4', 3, 60, 18 * 60, false),
  ]

  it('sums every Leg by default (== เดินทางรวม)', () => {
    expect(sumTravelSeconds(stops)).toBe((12 + 6 + 18) * 60)
  })

  it('excludes visited Stops’ Legs with {excludeVisited} (== เหลือเดินทาง)', () => {
    // remaining = C(6) + D(18); the 6-min Leg INTO the first remaining Stop is included
    expect(sumTravelSeconds(stops, {excludeVisited: true})).toBe((6 + 18) * 60)
  })

  it('all visited → 0', () => {
    const allDone = stops.map((s) => ({...s, isVisited: true}))
    expect(sumTravelSeconds(allDone, {excludeVisited: true})).toBe(0)
  })

  it('a null Leg contributes 0', () => {
    expect(sumTravelSeconds([stop('1', 0, 60, null, false)], {excludeVisited: true})).toBe(0)
  })

  it('computeSchedule ignores isVisited (arrival/depart identical either way)', () => {
    const base = [stop('1', 0, 60, null), stop('2', 1, 45, 25 * 60)]
    const day = (v: boolean): ItineraryDayDto => ({
      id: 'd', date: '2026-11-14', dayStartTime: '09:00:00', useCurrentTimeAsStart: false,
      stops: base.map((s) => ({...s, isVisited: v})),
    })
    expect(computeSchedule(day(true)).map((s) => [s.arrival, s.depart]))
      .toEqual(computeSchedule(day(false)).map((s) => [s.arrival, s.depart]))
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/pages/trips/hooks/useSchedule.test.ts`
Expected: FAIL — `sumTravelSeconds is not a function` / not exported.

- [ ] **Step 3: Implement the pure helper and wire the hook**

In `useSchedule.ts`, add the export immediately above `export function useSchedule`:
```ts
/** Σ Leg travel time (seconds) over a day's Stops. With {excludeVisited}, Stops already
 *  marked Visited drop out — the basis for the "เหลือเดินทาง" figure (ADR-047).
 *  Order-independent (a plain sum), so it never depends on computeSchedule. */
export function sumTravelSeconds(stops: StopDto[], opts?: {excludeVisited?: boolean}): number {
  return stops.reduce(
    (sum, st) => sum + (opts?.excludeVisited && st.isVisited ? 0 : (st.legToReach?.seconds ?? 0)),
    0,
  )
}
```
Then replace the hook body's `totalTravelSeconds` line and return (currently lines 155-157):
```ts
    const totalTravelSeconds = sumTravelSeconds(day.stops)
    const remainingTravelSeconds = sumTravelSeconds(day.stops, {excludeVisited: true})
    const dayEnd = scheduled.length ? scheduled[scheduled.length - 1].depart : day.dayStartTime.slice(0, 5)
    return {scheduled, dayEnd, totalTravelSeconds, remainingTravelSeconds}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/pages/trips/hooks/useSchedule.test.ts`
Expected: PASS (all describe blocks, including the pre-existing cascade/flag tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/hooks/useSchedule.ts frontend/src/pages/trips/hooks/useSchedule.test.ts
git commit -m "feat(trips): sumTravelSeconds + remainingTravelSeconds for visited-aware travel (#24)"
```

---

## Task 2: `reorderKeepingVisited` — reorder scoped to remaining Stops (pure)

**Files:**
- Modify: `frontend/src/pages/trips/lib/reorder.ts` (append)
- Test: `frontend/src/pages/trips/lib/reorder.test.ts` (append)

**Interfaces:**
- Consumes: existing `computeReorder(ids, activeId, overId): string[] | null` (same file).
- Produces: `export function reorderKeepingVisited(fullIds: string[], visitedIds: ReadonlySet<string>, activeId: string, overId: string): string[] | null` — the full-day ordered ids with visited ids pinned at their original index, or `null` when nothing changes. Task 4 consumes it.

- [ ] **Step 1: Write the failing tests**

Append to `reorder.test.ts` (the import of `computeReorder` on line 2 becomes `import {computeReorder, reorderKeepingVisited} from './reorder'`):
```ts
describe('reorderKeepingVisited', () => {
  it('reorders the remaining suffix while the visited prefix stays pinned', () => {
    // full a(✓) b(✓) c d e ; drag e above c → remaining [c,d,e]→[e,c,d]
    expect(reorderKeepingVisited(['a', 'b', 'c', 'd', 'e'], new Set(['a', 'b']), 'e', 'c'))
      .toEqual(['a', 'b', 'e', 'c', 'd'])
  })

  it('keeps a visited Stop pinned in the MIDDLE while remaining reorder around it', () => {
    // full a b(✓) c d ; remaining [a,c,d] ; drag d above a → [d,a,c] ; b stays at index 1
    expect(reorderKeepingVisited(['a', 'b', 'c', 'd'], new Set(['b']), 'd', 'a'))
      .toEqual(['d', 'b', 'a', 'c'])
  })

  it('returns null when active and over are the same', () => {
    expect(reorderKeepingVisited(['a', 'b', 'c'], new Set(['a']), 'b', 'b')).toBeNull()
  })

  it('returns null when a drag id is not among the remaining (e.g. it is visited)', () => {
    expect(reorderKeepingVisited(['a', 'b', 'c'], new Set(['a']), 'a', 'c')).toBeNull()
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/pages/trips/lib/reorder.test.ts`
Expected: FAIL — `reorderKeepingVisited is not a function`.

- [ ] **Step 3: Implement the helper**

Append to `reorder.ts`:
```ts
/**
 * Reorder only the not-visited Stops among themselves; visited ids keep their original
 * index (ADR-048). Returns the FULL-day ordered ids to send to reorderStops, or `null`
 * when nothing changes (delegates the change/lookup rules to computeReorder).
 */
export function reorderKeepingVisited(
  fullIds: string[],
  visitedIds: ReadonlySet<string>,
  activeId: string,
  overId: string,
): string[] | null {
  const remainingIds = fullIds.filter((id) => !visitedIds.has(id))
  const nextRemaining = computeReorder(remainingIds, activeId, overId)
  if (!nextRemaining) return null
  let ri = 0
  return fullIds.map((id) => (visitedIds.has(id) ? id : nextRemaining[ri++]))
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/pages/trips/lib/reorder.test.ts`
Expected: PASS (both `computeReorder` and `reorderKeepingVisited` blocks).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/reorder.ts frontend/src/pages/trips/lib/reorder.test.ts
git commit -m "feat(trips): reorderKeepingVisited pins visited Stops during drag-reorder (#24)"
```

---

## Task 3: Presentational pieces — `TravelLeg` note prop, `VisitedStopRow`, CSS

**Files:**
- Modify: `frontend/src/pages/trips/components/TravelLeg.tsx`
- Create: `frontend/src/pages/trips/components/VisitedStopRow.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css` (append)

**Interfaces:**
- Consumes: `TripPlaceDto`, `catEmoji` (from `../placeCategory`), `LegDto`, `TravelMode`.
- Produces:
  - `TravelLeg` gains optional `note?: string` — when set, renders the "จากจุดที่เพิ่งไป" lead line (class `travel-leg lead`) instead of the distance.
  - `export function VisitedStopRow({place: TripPlaceDto, arrival: string, onUnvisit: () => void})` — a slim, non-draggable done row (never calls `useSortable`).
  - CSS classes: `.stat-remain`, `.travel-leg.lead .from`, `.done-drawer`, `.done-toggle` (+ `.chev`, `.badge`), `.done-body`, `.done-item` (+ `.di-time`, `.di-name`), `.trips-empty svg`.

No unit-test harness exists for presentational components (repo tests pure logic via Vitest; UI leans on `tsc -b`/`vite build` + visual/manual — see ADR-043). Gate here is the build plus a visual check against the confirmed mock.

- [ ] **Step 1: Add the optional `note` prop to `TravelLeg`**

Replace the body of `TravelLeg.tsx` (keep the top `import` and `ICON` map):
```tsx
export function TravelLeg({leg, mode, note}: {leg: LegDto; mode: TravelMode; note?: string}) {
  // Missing/undefined source is treated as Estimated so the pill never over-promises.
  const estimated = leg.source !== 'Routed'
  const prefix = estimated ? '~' : ''
  return (
    <div className={`travel-leg${note ? ' lead' : ''}`}>
      {/* ADR-024 locks this pill's exact text: full word "นาที", never abbreviated. */}
      <span className="leg-pill">{ICON[mode]} {prefix}{Math.round(leg.seconds / 60)} นาที</span>
      <span className="leg-line" />
      {note ? (
        <span className="from">{note}</span>
      ) : (
        <>
          <span className="leg-dist">{prefix}{(leg.meters / 1000).toFixed(1)} กม.</span>
          {estimated && <span className="leg-approx">ประมาณ</span>}
        </>
      )}
    </div>
  )
}
```

- [ ] **Step 2: Create `VisitedStopRow.tsx`**

```tsx
// frontend/src/pages/trips/components/VisitedStopRow.tsx
import type {TripPlaceDto} from '../../../shared/api/api'
import {catEmoji} from '../placeCategory'

/** Slim, non-draggable row for a Visited ("มาแล้ว") Stop inside the collapsible drawer
 *  (ADR-048). It lives OUTSIDE the DndContext and never calls useSortable. Un-ticking the
 *  checkbox sends the Stop back to the active "ที่เหลือ" list. */
export function VisitedStopRow({
  place,
  arrival,
  onUnvisit,
}: {
  place: TripPlaceDto
  arrival: string
  onUnvisit: () => void
}) {
  return (
    <div className="done-item">
      <label className="stop-check">
        <input
          type="checkbox"
          checked
          onChange={() => onUnvisit()}
          aria-label={`เอาออกจากรายการมาแล้ว: ${place.name}`}
        />
      </label>
      <span className="di-time">{arrival}</span>
      <span className="di-name">{catEmoji(place.category)} {place.name}</span>
    </div>
  )
}
```

- [ ] **Step 3: Append the CSS**

Append to `trips-tokens.css` (reuses the existing `--visited` / `--visited-bg` / `--muted` / `--border` tokens):
```css
/* ── Visited "มาแล้ว" drawer + remaining-travel figure (ADR-047 / ADR-048) ── */
/* remaining-travel accent on the dark day-summary bar */
.stat-remain b { color: #8ef0c0; }

/* leading-Leg line: the drive INTO the first remaining Stop, from the last visited place */
.travel-leg.lead .from { color: var(--muted); font-weight: 500; }

.done-drawer { margin-top: 12px; border-top: 1px dashed #d7dee6; padding-top: 10px; }
.done-toggle {
  width: 100%; display: flex; align-items: center; gap: 9px;
  background: transparent; border: 0; padding: 8px 4px; cursor: pointer;
  font: inherit; color: #475569; font-size: 12.5px; font-weight: 700;
}
.done-toggle .chev { color: #94a3b8; transition: transform 0.15s ease; }
.done-toggle[aria-expanded='false'] .chev { transform: rotate(-90deg); }
.done-toggle .badge {
  display: inline-flex; align-items: center; gap: 5px;
  background: var(--visited-bg); color: var(--visited);
  border-radius: 999px; padding: 2px 9px; font-size: 11px;
}
.done-toggle .badge svg { width: 11px; height: 11px; }
.done-body { display: flex; flex-direction: column; gap: 8px; margin-top: 6px; }
.done-item {
  display: flex; align-items: center; gap: 10px;
  background: #f6f8fa; border: 1px solid var(--border); border-radius: 11px;
  padding: 8px 12px 8px 0;
}
/* reuse .stop-check base; restyle for the slim row */
.done-item .stop-check {
  width: 38px; align-self: stretch; border-right-color: #cfe9d7;
  background: var(--visited-bg); border-radius: 11px 0 0 11px;
}
.done-item .di-time {
  flex: none; width: 44px; font-family: 'Spline Sans Mono', ui-monospace, monospace;
  font-size: 12px; color: var(--muted);
}
.done-item .di-name {
  flex: 1; min-width: 0; font-size: 13px; font-weight: 600; color: #475569;
  text-decoration: line-through; text-decoration-thickness: 1.5px;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}

/* CheckIcon in the all-visited empty state (icon, never an emoji) */
.trips-empty svg { width: 15px; height: 15px; vertical-align: -2px; color: var(--visited); margin-right: 4px; }
```

- [ ] **Step 4: Verify it compiles/builds**

Run: `cd frontend && npm run build`
Expected: PASS (`tsc -b` clean — `VisitedStopRow` is exported, `TravelLeg`'s `note` is optional so existing call sites are unaffected; `vite build` succeeds).

- [ ] **Step 5: Visual check against the mock (manual)**

Open `docs/mocks/trip-visited-remaining-mock.html` (แนวทาง 1) and confirm the coded CSS matches: slim done rows with the green check column, the collapsed chevron pointing right, the lead-Leg "จากจุดที่เพิ่งไป" line.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/TravelLeg.tsx frontend/src/pages/trips/components/VisitedStopRow.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): VisitedStopRow + lead-Leg note + มาแล้ว drawer styles (#24)"
```

---

## Task 4: Wire `ItineraryTab` — partition, drawer, remaining-travel, reorder

**Files:**
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`

**Interfaces:**
- Consumes: `remainingTravelSeconds` from `useSchedule` (Task 1); `reorderKeepingVisited` (Task 2); `VisitedStopRow`, `TravelLeg` note prop (Task 3); existing `CheckIcon`, `ChevronDownIcon`, `setStopVisited`, `reorderStops`.
- Produces: the fully wired feature. No new exports.

- [ ] **Step 1: Update imports**

- Change the reorder import (currently line 15) from `import {computeReorder} from '../lib/reorder'` to:
```ts
import {reorderKeepingVisited} from '../lib/reorder'
```
- Add two component imports next to the existing trips component imports (near lines 31-37):
```ts
import {VisitedStopRow} from './VisitedStopRow'
import {CheckIcon} from './FlagIcons'
```
(`ChevronDownIcon` is already imported from `./TripFormIcons`.)

- [ ] **Step 2: Add the drawer open/closed state**

After the existing `const [isReordering, setIsReordering] = useState(false)` (line ~124):
```ts
const [doneOpen, setDoneOpen] = useState(false)
```

- [ ] **Step 3: Destructure `remainingTravelSeconds`**

Change the `useSchedule` destructure (line ~148):
```ts
const {scheduled, dayEnd, totalTravelSeconds, remainingTravelSeconds} = useSchedule(day ?? EMPTY_DAY, placesById)
```

- [ ] **Step 4: Rewrite `handleDragEnd` to pin visited Stops**

Replace the body of `handleDragEnd` (currently lines 183-202) — only the `orderedStopIds` computation changes; the mutation/refetch/loader stay identical:
```tsx
  const handleDragEnd = async (e: DragEndEvent) => {
    setActiveDragId(null)
    const {active, over} = e
    if (!over) return
    const visitedIds = new Set(scheduled.filter((s) => s.stop.isVisited).map((s) => s.stop.id))
    const orderedStopIds = reorderKeepingVisited(
      scheduled.map((s) => s.stop.id),
      visitedIds,
      String(active.id),
      String(over.id),
    )
    if (!orderedStopIds) return
    setIsReordering(true)
    try {
      await reorder({tripId, dayId: resolvedDayId, orderedStopIds}).unwrap()
      await refetchItinerary().unwrap()
    } catch (err) {
      setActionError(getErrorMessage(err))
    } finally {
      setIsReordering(false)
    }
  }
```

- [ ] **Step 5: Derive the partition and lead Leg**

Replace the two lines at ~204-205:
```tsx
  const existingTripPlaceIds = new Set(scheduled.map((s) => s.stop.tripPlaceId))
  const visitedCount = scheduled.filter((s) => s.stop.isVisited).length
```
with:
```tsx
  const existingTripPlaceIds = new Set(scheduled.map((s) => s.stop.tripPlaceId))
  const remaining = scheduled.filter((s) => !s.stop.isVisited)
  const done = scheduled.filter((s) => s.stop.isVisited)
  const visitedCount = done.length
  const allVisited = scheduled.length > 0 && remaining.length === 0
  // Lead Leg = the drive INTO the first remaining Stop, shown only when a visited Stop
  // precedes it (i.e. it is not the day's very first Stop). Skipped at zero-visited. ADR-047 §4.
  const leadLeg =
    remaining.length > 0 && scheduled.indexOf(remaining[0]) > 0 ? remaining[0].stop.legToReach : null
```

- [ ] **Step 6: Swap the day-summary travel figure**

Replace the fixed `เดินทางรวม` span (currently lines 264-266):
```tsx
          <span>
            เดินทางรวม <b>{formatDurationMinutes(totalTravelSeconds / 60)}</b>
          </span>
```
with the label that flips once anything is visited (zero-visited = today's view verbatim):
```tsx
          {visitedCount > 0 ? (
            <span className="stat-remain">
              เหลือเดินทาง <b>{formatDurationMinutes(remainingTravelSeconds / 60)}</b>
            </span>
          ) : (
            <span>
              เดินทางรวม <b>{formatDurationMinutes(totalTravelSeconds / 60)}</b>
            </span>
          )}
```

- [ ] **Step 7: Render only remaining Stops in the sortable list + lead Leg + all-visited empty state**

Replace the whole `<SortableContext>…</SortableContext>` block (currently lines 330-376). `items` now lists **remaining** ids; the map is over `remaining`; the lead Leg reuses `TravelLeg` with a `note`; the all-visited empty state uses `CheckIcon`:
```tsx
        <SortableContext items={remaining.map((s) => s.stop.id)} strategy={verticalListSortingStrategy}>
          <div className={`stop-list${activeDragId ? ' dragging' : ''}`}>
            {leadLeg && (
              <TravelLeg leg={leadLeg} mode={remaining[0].stop.travelModeToReach} note="จากจุดที่เพิ่งไป" />
            )}
            {remaining.map((s, i) => {
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
            {allVisited && (
              <p className="trips-empty"><CheckIcon /> เที่ยวครบทุกจุดแล้ว</p>
            )}
          </div>
        </SortableContext>
```

- [ ] **Step 8: Add the collapsible "มาแล้ว" drawer**

Insert immediately AFTER the add-stop block (the `{pickerOpen ? <AddStopPicker … /> : <button className="btn-add-stop">…</button>}` closing, ~line 392) and before `{editorStopId && …}`:
```tsx
      {done.length > 0 && (
        <div className="done-drawer">
          <button
            type="button"
            className="done-toggle"
            aria-expanded={doneOpen}
            onClick={() => setDoneOpen((v) => !v)}
          >
            <ChevronDownIcon className="chev" />
            <span className="badge"><CheckIcon /> มาแล้ว {done.length}</span>
          </button>
          {doneOpen && (
            <div className="done-body">
              {done.map((s) => {
                const place = placesById[s.stop.tripPlaceId]
                return place ? (
                  <VisitedStopRow
                    key={s.stop.id}
                    place={place}
                    arrival={s.arrival}
                    onUnvisit={async () => {
                      try {
                        await setStopVisited({tripId, stopId: s.stop.id, isVisited: false}).unwrap()
                      } catch (err) {
                        setActionError(getErrorMessage(err))
                      }
                    }}
                  />
                ) : null
              })}
            </div>
          )}
        </div>
      )}
```

- [ ] **Step 9: Typecheck + build**

Run: `cd frontend && npm run build`
Expected: PASS. Watch for: `computeReorder` is no longer imported here (moved into `reorder.ts`); `totalTravelSeconds` is still referenced (zero-visited branch); `Fragment`/`TravelLeg`/`ItineraryStopCard` imports intact.

- [ ] **Step 10: Manual verification on the dev app**

Run: `cd frontend && npm run dev`, open a Trip's Itinerary with ≥3 Stops, and confirm:
1. **Zero visited** → summary reads `เดินทางรวม`, no "มาแล้ว" drawer (identical to before).
2. **Tick a Stop** → it leaves the list into the collapsed `✓ มาแล้ว (n)` drawer; summary flips to `เหลือเดินทาง` and drops by that Stop's Leg; a `จากจุดที่เพิ่งไป` lead line appears above the new first Stop. In the Network tab only a `PATCH …/stops/{id}` fires — **no** `GET …/itinerary` / Routes / Weather (ADR-042).
3. **Expand the drawer → un-tick** → the Stop returns to its place in the active list; summary recomputes.
4. **Drag-reorder** the remaining list with some Stops visited → order persists after the loader; visited Stops keep their positions; times recompute via the drop refetch.
5. **Tick every Stop** → list shows `✓ เที่ยวครบทุกจุดแล้ว`; summary reads `เหลือเดินทาง 0 นาที`.

- [ ] **Step 11: Commit**

```bash
git add frontend/src/pages/trips/components/ItineraryTab.tsx
git commit -m "feat(trips): move visited Stops to มาแล้ว drawer + เหลือเดินทาง figure (closes #24)"
```

---

## Self-Review

**1. Spec coverage** (`2026-07-12-trip-visited-remaining-travel-design.md`):
- §2 remaining-travel calc, no cascade coupling → Task 1 (incl. the `computeSchedule` invariance test). ✓
- §3.1 partition, §3.2 summary label swap, §3.3 SortableContext=remaining + lead Leg + all-visited empty state, §3.5 drawer, §3.6 `VisitedStopRow`, §3.4 reorder → Tasks 3 & 4. ✓
- §3.4/ADR-048 reorder pinning → Task 2 (`reorderKeepingVisited`) + Task 4 Step 4. ✓
- §4 CSS → Task 3 Step 3. ✓
- §5 tests: pure units covered (Tasks 1-2); the spec's "component test" is delivered as build + the Step-10 manual checklist, matching the repo's no-component-harness reality (ADR-043) — noted explicitly, not silently dropped. ✓
- §6 rollout (no migration; PATCH-only on tick; reorder still works) → Task 4 Step 10 checklist. ✓
- Non-goals (no re-cascade, no backend, no map change, no flag/weather suppression) → Global Constraints + Task 1 invariance test. ✓

**2. Placeholder scan:** no TBD/TODO; every code step shows complete code; every command has an expected result. ✓

**3. Type consistency:** `sumTravelSeconds(stops, {excludeVisited})` and `remainingTravelSeconds` (Task 1) match Task 4 Step 3/6 usage; `reorderKeepingVisited(fullIds, visitedIds, activeId, overId)` (Task 2) matches Task 4 Step 4; `VisitedStopRow({place, arrival, onUnvisit})` (Task 3) matches Task 4 Step 8; `TravelLeg`'s new optional `note` (Task 3) matches Task 4 Step 7. `ChevronDownIcon`/`CheckIcon` exist (`TripFormIcons`/`FlagIcons`). ✓

---

## Deferred (Phase 2 — out of scope)

Dimming visited map pins; a true "compress the day to what's left" re-cascade (would revisit ADR-008); persisting the drawer open/closed state per Day; a Playwright e2e smoke for the drawer (extend the existing reorder smoke); and exact leading-Leg attribution when a visited Stop sits *between* two remaining Stops (MVP shows each remaining Stop's stored `legToReach`).
