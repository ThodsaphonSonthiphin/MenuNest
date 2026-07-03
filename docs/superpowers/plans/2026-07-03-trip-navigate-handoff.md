# Trip → Google Maps Navigate Hand-off Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two buttons to the Trip itinerary that open Google Maps for turn-by-turn navigation — a whole-day route pill and a per-Stop navigate icon.

**Architecture:** Frontend-only. All logic lives in a new pure util (`navUrl.ts`, fully unit-tested with Vitest); the React components (`ItineraryTab`, `ItineraryStopCard`) stay thin — they derive data already on screen, call the builders, and open the returned URL via a native `<a target="_blank">`. No backend, no API key, no Maps Platform billing (a deep link is a plain URL, not a REST call).

**Tech Stack:** React 19, TypeScript ~6, Vite 8, Vitest 4, RTK Query, `@microsoft/applicationinsights-web`. Google Maps *Maps URLs* directions deep link.

## Global Constraints

Copied verbatim from `docs/superpowers/specs/2026-07-03-trip-navigate-handoff-design.md` and ADR-011. Every task's requirements implicitly include these.

- **Frontend-only** — no backend, no DB, no data-model change.
- **Deep-link base:** `https://www.google.com/maps/dir/?api=1&…`. `api=1` and `destination` are **required**; append **`dir_action=navigate`** to both link types.
- **Origin is always omitted** (⇒ Google Maps uses current location).
- **Travel-mode map:** `Drive`→`driving`, `Walk`→`walking`, `Transit`→`transit`. Whole-day uses `Trip.defaultTravelMode`; per-Stop uses that Stop's `travelModeToReach`.
- **Conservative waypoint cap:** `3` when the surface is plausibly mobile (incl. iPad), `9` otherwise. Applies to **waypoints only**. A day fully fits when `usableCount ≤ cap + 1` — **guard with `≤`** (boundary K = 4 mobile / 10 desktop is the off-by-one site).
- **Encoding:** every point's text value is `lat,lng` at **6 decimals** (`toFixed(6)`). Build with `URLSearchParams` (encodes `,`→`%2C`, `|`→`%7C`; Google decodes). Whole-day route = **`lat,lng` only, no place_ids**. Per-Stop adds `destination_place_id` only when the Place has a `googlePlaceId`. `origin_place_id` never used.
- **Usable point:** `Number.isFinite(lat) && Number.isFinite(lng) && !(lat === 0 && lng === 0)`. Order comes from the sequence-sorted `scheduled` array. Collapse **consecutive** duplicate points; preserve non-consecutive revisits.
- **Open synchronously** via `<a href target="_blank" rel="noopener noreferrer">` — never `window.open`, never `await` before opening. Per-Stop control is a **sibling** of `.stop-body` (never nested — button-in-button is invalid and would fire the stop editor).
- **Thai microcopy (exact):** pill `นำทาง`; overflow note `นำทางครอบคลุม {N} จุดแรก — จุดที่เหลือใช้ปุ่มนำทางรายจุด`; mixed-mode note `วันนี้มีหลายโหมดเดินทาง — เส้นทางทั้งวันใช้โหมดเดียว ใช้ปุ่มรายจุดเพื่อโหมดที่ถูก`; per-Stop `aria-label` `นำทาง`; disabled per-Stop `aria-label` `ไม่มีพิกัดสำหรับนำทาง`.
- **Telemetry:** reuse the existing `appInsights` singleton (`shared/telemetry/appInsights.ts`, safe no-op in dev); one `trackEvent('TripNavHandoff')`; **no PII** (no coords, names, or IDs).
- **Styling:** teal tokens only (`--teal`, `--teal-soft`, `--teal-deep`, `--warn`, `--warn-bg`); the global orange Syncfusion theme is untouched.

## File Structure

- **Create** `frontend/src/pages/trips/lib/navUrl.ts` — pure URL builders + surface detection (the only impure fn).
- **Create** `frontend/src/pages/trips/lib/navUrl.test.ts` — Vitest unit tests for the builders + detection.
- **Create** `frontend/src/pages/trips/components/NavIcon.tsx` — shared nav-arrow SVG (pill + per-Stop).
- **Modify** `frontend/src/pages/trips/components/ItineraryStopCard.tsx` — add the per-Stop nav control.
- **Modify** `frontend/src/pages/trips/components/ItineraryTab.tsx` — compute nav data, wire the whole-day pill, the notes, and per-Stop props + telemetry.
- **Modify** `frontend/src/pages/trips/trips-tokens.css` — `.day-stats`, `.btn-day-nav`, `.stop-nav`, `.nav-note`.

Relevant reference types (already in `frontend/src/shared/api/api.ts`, do not change):
```ts
export type TravelMode = 'Drive' | 'Walk' | 'Transit'
export interface TripDto { id: string; name: string; destination: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode }
export interface TripPlaceDto { id: string; tripId: string; googlePlaceId: string | null; name: string; lat: number; lng: number; /* …+ address, category, times, notes */ }
export interface StopDto { id: string; tripPlaceId: string; sequence: number; dwellMinutes: number; travelModeToReach: TravelMode; legToReach: LegDto | null }
```

---

### Task 1: Pure nav-URL builders (`navUrl.ts`)

**Files:**
- Create: `frontend/src/pages/trips/lib/navUrl.ts`
- Test: `frontend/src/pages/trips/lib/navUrl.test.ts`

**Interfaces:**
- Consumes: `TravelMode` from `../../../shared/api/api`.
- Produces:
  - `interface NavPoint { lat: number; lng: number; placeId?: string | null }`
  - `interface DayNav { url: string; coveredCount: number; overflow: boolean }`
  - `travelModeToGmaps(mode: TravelMode): 'driving' | 'walking' | 'transit'`
  - `buildStopNavUrl(place: {lat: number; lng: number; googlePlaceId?: string | null}, mode: TravelMode): string | null`
  - `buildDayNavUrl(points: NavPoint[], cap: number, mode: TravelMode): DayNav | null`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/pages/trips/lib/navUrl.test.ts`:
```ts
import {describe, it, expect} from 'vitest'
import {travelModeToGmaps, buildStopNavUrl, buildDayNavUrl} from './navUrl'
import type {NavPoint} from './navUrl'

// Helpers to read a built URL without asserting brittle encoded strings.
const q = (url: string) => new URL(url).searchParams
const pt = (lat: number, lng: number, placeId?: string | null): NavPoint => ({lat, lng, placeId})

describe('travelModeToGmaps', () => {
  it('maps every TravelMode to its Google value', () => {
    expect(travelModeToGmaps('Drive')).toBe('driving')
    expect(travelModeToGmaps('Walk')).toBe('walking')
    expect(travelModeToGmaps('Transit')).toBe('transit')
  })
})

describe('buildStopNavUrl', () => {
  it('builds a single-destination link with place_id + dir_action=navigate', () => {
    const url = buildStopNavUrl({lat: 19.04, lng: 99.63, googlePlaceId: 'ChIJabc'}, 'Walk')!
    expect(url.startsWith('https://www.google.com/maps/dir/?')).toBe(true)
    const p = q(url)
    expect(p.get('api')).toBe('1')
    expect(p.get('destination')).toBe('19.040000,99.630000')
    expect(p.get('destination_place_id')).toBe('ChIJabc')
    expect(p.get('travelmode')).toBe('walking')
    expect(p.get('dir_action')).toBe('navigate')
    expect(p.has('origin')).toBe(false)
  })

  it('omits destination_place_id when the Place has no googlePlaceId', () => {
    const url = buildStopNavUrl({lat: 19.04, lng: 99.63, googlePlaceId: null}, 'Drive')!
    expect(q(url).has('destination_place_id')).toBe(false)
    expect(q(url).get('travelmode')).toBe('driving')
  })

  it('returns null for unusable coords (NaN and Null Island)', () => {
    expect(buildStopNavUrl({lat: NaN, lng: 99, googlePlaceId: null}, 'Drive')).toBeNull()
    expect(buildStopNavUrl({lat: 0, lng: 0, googlePlaceId: null}, 'Drive')).toBeNull()
  })
})

describe('buildDayNavUrl', () => {
  it('returns null for empty or all-unusable input', () => {
    expect(buildDayNavUrl([], 3, 'Drive')).toBeNull()
    expect(buildDayNavUrl([pt(0, 0), pt(NaN, 1)], 3, 'Drive')).toBeNull()
  })

  it('handles a single usable stop: destination only, no waypoints', () => {
    const r = buildDayNavUrl([pt(19.06, 99.65)], 3, 'Drive')!
    expect(r.coveredCount).toBe(1)
    expect(r.overflow).toBe(false)
    const p = q(r.url)
    expect(p.get('destination')).toBe('19.060000,99.650000')
    expect(p.has('waypoints')).toBe(false)
  })

  it('covers a full day within the cap (3 stops, cap 3): waypoints=first 2, dest=last', () => {
    const r = buildDayNavUrl([pt(19.01, 99.6), pt(19.04, 99.63), pt(19.06, 99.65)], 3, 'Drive')!
    expect(r.coveredCount).toBe(3)
    expect(r.overflow).toBe(false)
    const p = q(r.url)
    expect(p.get('waypoints')).toBe('19.010000,99.600000|19.040000,99.630000')
    expect(p.get('destination')).toBe('19.060000,99.650000')
    expect(p.get('travelmode')).toBe('driving')
    expect(p.get('dir_action')).toBe('navigate')
  })

  it('fits exactly at the boundary K = cap+1 with no overflow (4 stops, cap 3)', () => {
    const r = buildDayNavUrl([pt(1, 1), pt(2, 2), pt(3, 3), pt(4, 4)], 3, 'Drive')!
    expect(r.coveredCount).toBe(4)
    expect(r.overflow).toBe(false)
    expect(q(r.url).get('waypoints')!.split('|')).toHaveLength(3)
  })

  it('truncates when over the cap (5 stops, cap 3): cover first 4, overflow true', () => {
    const r = buildDayNavUrl([pt(1, 1), pt(2, 2), pt(3, 3), pt(4, 4), pt(5, 5)], 3, 'Drive')!
    expect(r.coveredCount).toBe(4)
    expect(r.overflow).toBe(true)
    const p = q(r.url)
    expect(p.get('waypoints')).toBe('1.000000,1.000000|2.000000,2.000000|3.000000,3.000000')
    expect(p.get('destination')).toBe('4.000000,4.000000')
  })

  it('desktop cap 9: 10 stops fit, 11 overflow', () => {
    const many = (n: number) => Array.from({length: n}, (_, i) => pt(i + 1, i + 1))
    expect(buildDayNavUrl(many(10), 9, 'Drive')!.overflow).toBe(false)
    expect(buildDayNavUrl(many(10), 9, 'Drive')!.coveredCount).toBe(10)
    expect(buildDayNavUrl(many(11), 9, 'Drive')!.overflow).toBe(true)
    expect(buildDayNavUrl(many(11), 9, 'Drive')!.coveredCount).toBe(10)
  })

  it('collapses consecutive duplicates (same placeId, and same coord) but preserves non-consecutive revisits', () => {
    const dupPlace = buildDayNavUrl([pt(1, 1, 'A'), pt(1, 1, 'A'), pt(2, 2, 'B')], 3, 'Drive')!
    expect(dupPlace.coveredCount).toBe(2)
    const dupCoord = buildDayNavUrl([pt(1, 1), pt(1, 1), pt(2, 2)], 3, 'Drive')!
    expect(dupCoord.coveredCount).toBe(2)
    const revisit = buildDayNavUrl([pt(1, 1, 'A'), pt(2, 2, 'B'), pt(1, 1, 'A')], 3, 'Drive')!
    expect(revisit.coveredCount).toBe(3)
  })

  it('never sends place_ids on the whole-day route and stays under 2048 chars', () => {
    const many = Array.from({length: 10}, (_, i) => pt(i + 1, i + 1, 'ChIJ' + 'x'.repeat(120)))
    const r = buildDayNavUrl(many, 9, 'Drive')!
    expect(q(r.url).has('waypoint_place_ids')).toBe(false)
    expect(q(r.url).has('destination_place_id')).toBe(false)
    expect(r.url.length).toBeLessThan(2048)
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npm test -- navUrl`
Expected: FAIL — `Failed to resolve import "./navUrl"` / functions undefined.

- [ ] **Step 3: Write the implementation**

Create `frontend/src/pages/trips/lib/navUrl.ts`:
```ts
// frontend/src/pages/trips/lib/navUrl.ts
//
// Pure builders for Google Maps "Maps URLs" directions deep links (the Trip
// navigate hand-off). No React, no RTK, no window — callers open the returned
// URL string. See ADR-011 and
// docs/superpowers/specs/2026-07-03-trip-navigate-handoff-design.md.
import type {TravelMode} from '../../../shared/api/api'

const DIR_BASE = 'https://www.google.com/maps/dir/'

export interface NavPoint {
  lat: number
  lng: number
  placeId?: string | null
}

export interface DayNav {
  url: string
  coveredCount: number
  overflow: boolean
}

const GMAPS_MODE: Record<TravelMode, 'driving' | 'walking' | 'transit'> = {
  Drive: 'driving',
  Walk: 'walking',
  Transit: 'transit',
}

export function travelModeToGmaps(mode: TravelMode): 'driving' | 'walking' | 'transit' {
  return GMAPS_MODE[mode]
}

const isUsable = (p: {lat: number; lng: number}): boolean =>
  Number.isFinite(p.lat) && Number.isFinite(p.lng) && !(p.lat === 0 && p.lng === 0)

const coord = (p: {lat: number; lng: number}): string =>
  `${p.lat.toFixed(6)},${p.lng.toFixed(6)}`

/** Collapse consecutive duplicate points (same placeId, else same 6-dp coord). */
function dedupeConsecutive(points: NavPoint[]): NavPoint[] {
  const out: NavPoint[] = []
  for (const p of points) {
    const prev = out[out.length - 1]
    if (prev) {
      const samePlace = !!p.placeId && !!prev.placeId && p.placeId === prev.placeId
      if (samePlace || coord(p) === coord(prev)) continue
    }
    out.push(p)
  }
  return out
}

/** Single-destination link to one Place. null when coords are unusable. */
export function buildStopNavUrl(
  place: {lat: number; lng: number; googlePlaceId?: string | null},
  mode: TravelMode,
): string | null {
  if (!isUsable(place)) return null
  const params = new URLSearchParams({api: '1', destination: coord(place)})
  if (place.googlePlaceId) params.set('destination_place_id', place.googlePlaceId)
  params.set('travelmode', travelModeToGmaps(mode))
  params.set('dir_action', 'navigate')
  return `${DIR_BASE}?${params.toString()}`
}

/**
 * Whole-day route from the device's current location (origin omitted) through
 * the day's Stops in order. Filters unusable points, collapses consecutive
 * dupes, applies the waypoint cap, and encodes lat,lng only (no place_ids —
 * positional-alignment + URL-length safety). null when no usable point remains.
 */
export function buildDayNavUrl(points: NavPoint[], cap: number, mode: TravelMode): DayNav | null {
  const usable = dedupeConsecutive(points.filter(isUsable))
  if (usable.length === 0) return null

  const fit = cap + 1
  const overflow = usable.length > fit
  const covered = overflow ? fit : usable.length
  const included = usable.slice(0, covered)

  const destination = included[included.length - 1]
  const waypoints = included.slice(0, -1)

  const params = new URLSearchParams({api: '1', destination: coord(destination)})
  if (waypoints.length > 0) params.set('waypoints', waypoints.map(coord).join('|'))
  params.set('travelmode', travelModeToGmaps(mode))
  params.set('dir_action', 'navigate')

  return {url: `${DIR_BASE}?${params.toString()}`, coveredCount: covered, overflow}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npm test -- navUrl`
Expected: PASS — all `navUrl.test.ts` cases green.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/navUrl.ts frontend/src/pages/trips/lib/navUrl.test.ts
git commit -m "feat(trips): pure Google Maps navigate-URL builders"
```

---

### Task 2: Surface detection & waypoint cap

**Files:**
- Modify: `frontend/src/pages/trips/lib/navUrl.ts`
- Test: `frontend/src/pages/trips/lib/navUrl.test.ts`

**Interfaces:**
- Produces:
  - `isMobileSurface(): boolean`
  - `getWaypointCap(): number` — `3` when mobile (incl. iPad), else `9`.

- [ ] **Step 1: Write the failing tests**

Append to `frontend/src/pages/trips/lib/navUrl.test.ts`:
```ts
import {vi, afterEach} from 'vitest'
import {isMobileSurface, getWaypointCap} from './navUrl'

afterEach(() => vi.unstubAllGlobals())

const stubNav = (n: Partial<Navigator> & {userAgentData?: {mobile?: boolean}}) =>
  vi.stubGlobal('navigator', n)

describe('isMobileSurface / getWaypointCap', () => {
  it('trusts userAgentData.mobile when present (true → mobile)', () => {
    stubNav({userAgentData: {mobile: true}, userAgent: 'irrelevant'})
    expect(isMobileSurface()).toBe(true)
    expect(getWaypointCap()).toBe(3)
  })

  it('trusts userAgentData.mobile when present (false → desktop, even with mobile-looking UA)', () => {
    stubNav({userAgentData: {mobile: false}, userAgent: 'Mozilla/5.0 (iPhone)'})
    expect(isMobileSurface()).toBe(false)
    expect(getWaypointCap()).toBe(9)
  })

  it('falls back to a UA regex for Android / iPhone', () => {
    stubNav({userAgent: 'Mozilla/5.0 (Linux; Android 14) Mobile'})
    expect(getWaypointCap()).toBe(3)
    stubNav({userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0)'})
    expect(getWaypointCap()).toBe(3)
  })

  it('detects iPadOS reporting a desktop Macintosh UA (MacIntel + touch)', () => {
    stubNav({userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', platform: 'MacIntel', maxTouchPoints: 5})
    expect(getWaypointCap()).toBe(3)
  })

  it('treats a real desktop as desktop (cap 9)', () => {
    stubNav({userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)', platform: 'Win32', maxTouchPoints: 0})
    expect(getWaypointCap()).toBe(9)
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npm test -- navUrl`
Expected: FAIL — `isMobileSurface`/`getWaypointCap` not exported.

- [ ] **Step 3: Add the implementation**

Append to `frontend/src/pages/trips/lib/navUrl.ts`:
```ts
/**
 * True when the surface is plausibly a phone or the iPad (which reports a
 * desktop UA). The link may open the native Maps app (waypoint cap 3) OR a
 * mobile browser (cap 3), and Google silently drops waypoints past the cap —
 * so we treat any plausibly-mobile surface conservatively. Impure (reads
 * navigator); kept out of the pure builders.
 */
export function isMobileSurface(): boolean {
  if (typeof navigator === 'undefined') return false
  const nav = navigator as Navigator & {userAgentData?: {mobile?: boolean}}
  if (typeof nav.userAgentData?.mobile === 'boolean') return nav.userAgentData.mobile
  if (/Android|iPhone|iPad|iPod|Mobile/i.test(nav.userAgent || '')) return true
  // iPadOS 13+ reports a desktop "Macintosh" UA with a touch screen.
  if (nav.platform === 'MacIntel' && nav.maxTouchPoints > 1) return true
  return false
}

/** Waypoint cap: 3 on any plausibly-mobile surface, 9 otherwise. */
export function getWaypointCap(): number {
  return isMobileSurface() ? 3 : 9
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npm test -- navUrl`
Expected: PASS — detection + builder cases all green.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/navUrl.ts frontend/src/pages/trips/lib/navUrl.test.ts
git commit -m "feat(trips): conservative mobile/desktop waypoint-cap detection"
```

---

### Task 3: Per-Stop navigate button

**Files:**
- Create: `frontend/src/pages/trips/components/NavIcon.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryStopCard.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx:161-186` (the stop-list map + `ItineraryStopCard` call)
- Modify: `frontend/src/pages/trips/trips-tokens.css` (add `.stop-nav`)

**Interfaces:**
- Consumes: `buildStopNavUrl` (Task 1), `appInsights` (`shared/telemetry/appInsights.ts`).
- Produces:
  - `NavIcon` component (default export-free named export `NavIcon`), reused by Task 4.
  - `ItineraryStopCard` gains two props: `navUrl: string | null`, `onNavigate?: () => void`.

- [ ] **Step 1: Create the shared icon component**

Create `frontend/src/pages/trips/components/NavIcon.tsx`:
```tsx
// frontend/src/pages/trips/components/NavIcon.tsx
// Google-Maps-style navigation arrow. Colour comes from `currentColor`; size
// from the parent's CSS (.btn-day-nav svg / .stop-nav svg). Shared by the
// whole-day pill and the per-Stop button.
export function NavIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" focusable="false">
      <path d="M21.71 11.29l-9-9a1 1 0 0 0-1.42 0l-9 9a1 1 0 0 0 0 1.42l9 9a1 1 0 0 0 1.42 0l9-9a1 1 0 0 0 0-1.42zM14 14.5V12h-4v3H8v-4a1 1 0 0 1 1-1h5V7.5l3.5 3.5z" />
    </svg>
  )
}
```

- [ ] **Step 2: Add the `.stop-nav` styles**

In `frontend/src/pages/trips/trips-tokens.css`, add after the `.stop-reorder` block (around line 155):
```css
/* ── Navigate hand-off — per-Stop icon (ADR-011) ── */
.stop-nav {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 44px; /* ≥44px touch target */
  border: 0;
  border-left: 1px solid var(--border);
  background: transparent;
  color: var(--teal);
  cursor: pointer;
  text-decoration: none;
  transition: background 0.12s ease, color 0.12s ease;
}
.stop-nav:hover { background: var(--teal-soft); color: var(--teal-deep); }
.stop-nav[aria-disabled='true'] { color: #cbd5e1; cursor: default; pointer-events: none; }
.stop-nav:focus-visible { outline: 2px solid var(--teal-deep); outline-offset: -2px; }
.stop-nav svg { width: 17px; height: 17px; }
```

- [ ] **Step 3: Add the nav control to `ItineraryStopCard`**

In `frontend/src/pages/trips/components/ItineraryStopCard.tsx`, add the import at the top (after the existing type imports):
```tsx
import {NavIcon} from './NavIcon'
```

Add the two props to the destructured params and the type (after `overnight = false,` and `overnight?: boolean`):
```tsx
  navUrl,
  onNavigate,
```
```tsx
  navUrl: string | null
  onNavigate?: () => void
```

Insert the nav control as a **sibling** of `.stop-body`, immediately before `<div className="stop-reorder">`:
```tsx
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
```

- [ ] **Step 4: Wire per-Stop data + telemetry in `ItineraryTab`**

In `frontend/src/pages/trips/components/ItineraryTab.tsx`, add imports near the existing ones (after line 18):
```tsx
import {buildStopNavUrl} from '../lib/navUrl'
import {appInsights} from '../../../shared/telemetry/appInsights'
```

Inside the `scheduled.map((s, i) => { … })` block (currently starting line 161), after `const place = placesById[s.stop.tripPlaceId]`, add:
```tsx
          const stopNav = place ? buildStopNavUrl(place, s.stop.travelModeToReach) : null
```

Add the two new props to the `<ItineraryStopCard … />` call (inside the `{place && (…)}` guard):
```tsx
                  navUrl={stopNav}
                  onNavigate={() =>
                    appInsights.trackEvent(
                      {name: 'TripNavHandoff'},
                      {scope: 'stop', travelMode: s.stop.travelModeToReach, hasPlaceId: !!place.googlePlaceId},
                    )
                  }
```

- [ ] **Step 5: Typecheck, lint, and run the unit tests**

Run: `cd frontend && npm run build && npm run lint && npm test`
Expected: `tsc -b` and `vite build` succeed (no type errors); eslint clean; all Vitest suites pass.

- [ ] **Step 6: Manual verification**

Run: `cd frontend && npm run dev`, open a Trip that has itinerary Stops, switch to the itinerary tab.
Expected: each stop-card shows a teal navigate arrow between the card body and the ▲▼ arrows. Clicking it opens a new tab to `https://www.google.com/maps/dir/?api=1&destination=<lat,lng>…&travelmode=<mode>&dir_action=navigate`; clicking the arrow does **not** open the stop editor. A stop with no coordinates shows the arrow greyed out (not clickable).

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/trips/components/NavIcon.tsx frontend/src/pages/trips/components/ItineraryStopCard.tsx frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): per-Stop navigate button opens Google Maps"
```

---

### Task 4: Whole-day route pill + overflow / mixed-mode notes

**Files:**
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx` (imports, derived nav data, `.day-summary` JSX, notes)
- Modify: `frontend/src/pages/trips/trips-tokens.css` (`.day-stats`, `.btn-day-nav`, `.nav-note`)

**Interfaces:**
- Consumes: `buildDayNavUrl`, `getWaypointCap`, `NavPoint` (Task 1/2), `NavIcon` (Task 3), `appInsights`.

- [ ] **Step 1: Add the pill + note styles**

In `frontend/src/pages/trips/trips-tokens.css`, replace the existing `.day-summary` block (lines 59–71) so the stats sit in a left group and add the pill/note styles. Existing block:
```css
/* ── Day summary bar ── */
.day-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  background: var(--ink);
  color: #9fb0c4;
  border-radius: 12px;
  padding: 11px 15px;
  font-size: 12px;
}
.day-summary b { color: #fff; font-weight: 700; }
```
Replace with:
```css
/* ── Day summary bar ── */
.day-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  background: var(--ink);
  color: #9fb0c4;
  border-radius: 12px;
  padding: 11px 15px;
  font-size: 12px;
}
.day-summary b { color: #fff; font-weight: 700; }

/* ── Navigate hand-off — whole-day pill + inline notes (ADR-011) ── */
.day-stats {
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
  flex-wrap: wrap;
}
.btn-day-nav {
  flex: none;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: var(--teal);
  color: #fff;
  border: 0;
  border-radius: 999px;
  padding: 7px 14px;
  font: inherit;
  font-size: 12px;
  font-weight: 700;
  text-decoration: none;
  cursor: pointer;
  box-shadow: 0 2px 8px rgba(14, 143, 158, 0.45);
  transition: background 0.12s ease;
}
.btn-day-nav:hover { background: #13a3b4; }
.btn-day-nav:focus-visible { outline: 2px solid #fff; outline-offset: 2px; }
.btn-day-nav svg { width: 14px; height: 14px; }

.nav-note {
  display: flex;
  align-items: flex-start;
  gap: 7px;
  background: var(--warn-bg);
  color: var(--warn);
  border-radius: 10px;
  padding: 8px 12px;
  margin: 0;
  font-size: 11px;
  font-weight: 600;
  line-height: 1.45;
}
```

- [ ] **Step 2: Import the day builders and `useMemo`**

In `frontend/src/pages/trips/components/ItineraryTab.tsx`:

Change the React import (line 2) from:
```tsx
import {useState} from 'react'
```
to:
```tsx
import {useMemo, useState} from 'react'
```

Extend the `navUrl` import added in Task 3 to include the day builders, and add `NavIcon`:
```tsx
import {buildDayNavUrl, buildStopNavUrl, getWaypointCap} from '../lib/navUrl'
import {NavIcon} from './NavIcon'
```

- [ ] **Step 3: Derive the whole-day nav data**

In `ItineraryTab`, after the line `const trip = trips?.find((t) => t.id === tripId)` (line 115), add:
```tsx
  const cap = useMemo(getWaypointCap, [])
  const dayMode = trip?.defaultTravelMode ?? 'Drive'
  const navPoints = scheduled
    .map((s) => placesById[s.stop.tripPlaceId])
    .filter((p): p is TripPlaceDto => !!p)
    .map((p) => ({lat: p.lat, lng: p.lng, placeId: p.googlePlaceId}))
  const dayNav = buildDayNavUrl(navPoints, cap, dayMode)
  const mixedMode = scheduled.slice(1).some((s) => s.stop.travelModeToReach !== dayMode)
```

- [ ] **Step 4: Restructure `.day-summary` and add the pill**

Replace the current `.day-summary` block (lines 146–156) — three bare spans — with a left stats group plus the pill:
```tsx
      <div className="day-summary">
        <div className="day-stats">
          <span>
            เริ่ม <b>{resolvedDay.dayStartTime.slice(0, 5)}</b>
          </span>
          <span>
            เสร็จ <b>{dayEnd}</b>
          </span>
          <span>
            เดินทางรวม <b>{Math.round(totalTravelSeconds / 60)} น.</b>
          </span>
        </div>
        {dayNav && (
          <a
            className="btn-day-nav"
            href={dayNav.url}
            target="_blank"
            rel="noopener noreferrer"
            onClick={() =>
              appInsights.trackEvent(
                {name: 'TripNavHandoff'},
                {
                  scope: 'day',
                  travelMode: dayMode,
                  stopCount: navPoints.length,
                  coveredCount: dayNav.coveredCount,
                  overflow: dayNav.overflow,
                  mixedMode,
                },
              )
            }
          >
            <NavIcon /> นำทาง
          </a>
        )}
      </div>

      {dayNav?.overflow && (
        <p className="nav-note">
          นำทางครอบคลุม {dayNav.coveredCount} จุดแรก — จุดที่เหลือใช้ปุ่มนำทางรายจุด
        </p>
      )}
      {dayNav && mixedMode && (
        <p className="nav-note">
          วันนี้มีหลายโหมดเดินทาง — เส้นทางทั้งวันใช้โหมดเดียว ใช้ปุ่มรายจุดเพื่อโหมดที่ถูก
        </p>
      )}
```

- [ ] **Step 5: Typecheck, lint, and run the unit tests**

Run: `cd frontend && npm run build && npm run lint && npm test`
Expected: `tsc -b` + `vite build` succeed; eslint clean; all Vitest suites pass.

- [ ] **Step 6: Manual verification**

Run: `cd frontend && npm run dev`, open a Trip with a multi-Stop day.
Expected:
- The dark day-summary bar shows a teal **นำทาง** pill on the right; clicking it opens a new tab to `…/maps/dir/?api=1&destination=…&waypoints=…&travelmode=<trip default>&dir_action=navigate`, routing from current location through the day in the on-screen order.
- A day with more Stops than the cap (add 5+ Stops on a phone-width viewport / mobile UA) shows the amber overflow note; a day whose Stops use more than one travel mode shows the mixed-mode note.
- A day with zero usable Stops shows no pill.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): whole-day route pill + overflow/mixed-mode notes"
```

---

## Self-Review

**1. Spec coverage:**

| Spec section | Task(s) |
|--------------|---------|
| §3 Deep-link contract (params, examples) | 1 (`buildStopNavUrl`, `buildDayNavUrl`) |
| §4 Travel-mode mapping | 1 (`travelModeToGmaps`) |
| §5 Cap & overflow math + surface detection | 1 (`buildDayNavUrl`), 2 (`getWaypointCap`) |
| §6 Usability, ordering, dedup, overnight | 1 (`isUsable`, `dedupeConsecutive`), 4 (order from `scheduled`) |
| §7 place_id strategy | 1 (day = coords only; stop += `destination_place_id`) |
| §8 Module design | 1, 2 |
| §9 Component integration + open via `<a>` | 3 (per-Stop), 4 (pill) |
| §10 Notes, states, microcopy | 4 (overflow + mixed-mode notes, hide-at-0), 3 (disabled per-Stop) |
| §11 Accessibility & styling | 3 (`.stop-nav`, aria-labels, 44px, focus), 4 (`.btn-day-nav`, `.nav-note`) |
| §12 Telemetry | 3 (stop event), 4 (day event) |
| §13 Edge-case matrix | 1 tests (0/1/at-cap/over-cap, null/coords, dedup), 2 tests (iPad/desktop), 3/4 (missing place, hide-at-0, disabled) |
| §14 Testing plan | 1, 2 (Vitest); 3, 4 (tsc/lint + manual) |
| §15 Out of scope | Not built (splitting, bicycling, geolocation) — intentional |

No gaps. Overnight/sequence-gap handling is covered structurally: nav points come from `scheduled` (already sequence-sorted, overnight-flag-agnostic), so no extra code is needed.

**2. Placeholder scan:** No `TODO`/`TBD`/"handle edge cases"/"similar to Task N". Every code step shows complete code; every test step shows real assertions.

**3. Type consistency:** `NavPoint`/`DayNav` defined in Task 1 and consumed unchanged in Task 4. `buildStopNavUrl`/`buildDayNavUrl`/`travelModeToGmaps`/`getWaypointCap` names identical across tasks and tests. `ItineraryStopCard` props `navUrl: string | null` + `onNavigate?: () => void` defined in Task 3 and supplied in Task 3 Step 4. `appInsights.trackEvent({name}, {props})` two-arg form used identically in Tasks 3 and 4. `TripPlaceDto` type guard matches the existing import in `ItineraryTab.tsx:11`.
