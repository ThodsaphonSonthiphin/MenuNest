# Trip Planner Timing-Flag Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single orange "timing flag" on the itinerary with self-explanatory, severity-coloured flags that state the problem and a fix in Thai words, consistently on the stop list and the map.

**Architecture:** The domain layer (`useSchedule`) computes at most one `TimingFlag` per stop — a typed `{reason, severity, …render data}` object — by the priority `overflow > closed > off-window`. A co-located wording map (`timingFlag.ts`) turns a flag into Thai reason/fix strings. `ItineraryStopCard` renders a coloured `.flag-note` banner (red = problem, amber = suggestion, none when well-timed); `useDayRoute`/`TripMap` colour the map pin from the same flag's severity and expose the reason via `aria-label`.

**Tech Stack:** React + TypeScript, Vitest (unit only), Redux Toolkit Query, `@vis.gl/react-google-maps`, plain CSS tokens (`trips-tokens.css`).

**Design source of truth:** [spec](../specs/2026-07-03-timing-flag-redesign-design.md) · ADR-[019](../../adr/019-timing-flag-explains-reason-and-fix.md)/[020](../../adr/020-three-distinct-timing-flag-types-single-most-severe.md)/[021](../../adr/021-timing-flag-severity-colour-and-no-positive-state.md)/[022](../../adr/022-map-pins-reflect-flag-severity.md) · [mock](../../mocks/trip-timing-flag-redesign-mock.html)

## Global Constraints

- **Frontend-only.** No backend, DB, or migration changes.
- **Icons:** never emoji. The three flag icons are **custom inline-SVG components** following the existing [NavIcon.tsx](../../../frontend/src/pages/trips/components/NavIcon.tsx) precedent (resolves spec §12: a Syncfusion moon glyph is not assured; custom SVG is this feature's norm). Existing category emoji (`catEmoji`) and the `⏱`/dwell glyph are pre-existing and out of scope.
- **No i18n library exists.** All new Thai wording lives in one co-located label map (`timingFlag.ts`), mirroring [placeCategory.ts](../../../frontend/src/pages/trips/placeCategory.ts). Copy is **verbatim from spec §5**.
- **Colour = severity only** (red `problem` = closed/overflow; amber `suggestion` = off-window). Words carry meaning. **No positive/green state.**
- **Never interpolate the raw severity enum into a `className`** — the enum values (`problem`/`suggestion`) differ from CSS class names (`bad`/`warn` on cards; `problem`/`amber` on pins). Use the explicit lookup maps defined below.
- **Build-red window (expected):** Task 1 changes the `StopFlag` type; `npx tsc --noEmit` will report errors in flag consumers (`ItineraryStopCard`, `ItineraryTab`, `useDayRoute`, `TripMap`) until Task 5 lands. Each task's own **Vitest** run is green throughout (Vitest transpiles per-file without full typecheck). The full typecheck goes green at the end of Task 5.

---

## File Structure

| File | Responsibility |
|---|---|
| `hooks/useSchedule.ts` *(modify)* | Flag domain: types, per-reason evaluators (`offWindowFlag`, `closedFlag`), `composeFlags` (single-most-severe), hook wiring |
| `hooks/useSchedule.test.ts` *(modify)* | Unit tests for the domain |
| `timingFlag.ts` *(create)* | `flagText(flag)` → Thai `{reasonLine, fixLine}` wording map |
| `timingFlag.test.ts` *(create)* | Unit tests for the wording map |
| `components/FlagIcons.tsx` *(create)* | `LockIcon` / `ClockIcon` / `MoonIcon` inline-SVG components |
| `trips-tokens.css` *(modify)* | `--bad`/`--warn-ink` tokens, `.stop-card.bad`, `.flag-note*`, `.route-pin.problem*`; remove dead `.chip.good`/`.chip.warn`/`--good*` |
| `components/ItineraryStopCard.tsx` *(modify)* | Render `.flag-note`; drop `bestLabel`/`overnight`/`✓⚠`/`+1วัน` |
| `components/ItineraryTab.tsx` *(modify)* | Drop `bestLabel()` + `bestLabel`/`overnight` props |
| `hooks/useDayRoute.ts` *(modify)* | `RouteStop.severity` + `flagNote` from the flag |
| `components/TripMap.tsx` *(modify)* | Pin class per severity + `aria-label` |

---

### Task 1: Flag domain model, evaluators & composition

**Files:**
- Modify: `frontend/src/pages/trips/hooks/useSchedule.ts` (whole file — replace types/`flagStop`/hook; add `closedFlag`, `offWindowFlag`, `composeFlags`; keep `isOpenAt`/`dayOfWeek`/`toMin`/`fromMin` intact)
- Test: `frontend/src/pages/trips/hooks/useSchedule.test.ts`

**Interfaces:**
- Produces:
  - `type FlagReason = 'overflow' | 'closed' | 'off-window'`
  - `type FlagSeverity = 'problem' | 'suggestion'`
  - `type ClosedKind = 'before-open' | 'on-break' | 'after-close' | 'all-day'`
  - `interface TimingFlag { reason; severity; closedKind?; reopenAt?; bestStart?; bestEnd?; windowDir?: 'before'|'after'; arrival? }`
  - `type StopFlag = TimingFlag | null`
  - `interface ScheduledStop { stop; arrival; depart; overnight; arrivedAfterMidnight }`
  - `type ScheduledStopWithFlag = ScheduledStop & { flag: StopFlag }`
  - `offWindowFlag(place, arrival): TimingFlag | null`
  - `closedFlag(openingHoursJson, dow, minutes): TimingFlag | null`
  - `composeFlags(scheduled, placesById, dow): ScheduledStopWithFlag[]`
  - `useSchedule(day, placesById)` → `{ scheduled: ScheduledStopWithFlag[], dayEnd, totalTravelSeconds }`
- **Removed:** `type StopFlag = 'green'|'amber'` and `flagStop` (renamed to `offWindowFlag` with a new return shape).

- [ ] **Step 1: Replace the test file with the new domain tests (write failing tests first)**

Overwrite `frontend/src/pages/trips/hooks/useSchedule.test.ts` with:

```ts
// frontend/src/pages/trips/hooks/useSchedule.test.ts
import {describe, it, expect} from 'vitest'
import {
  computeSchedule, dayOfWeek, isOpenAt,
  offWindowFlag, closedFlag, composeFlags,
} from './useSchedule'
import type {ItineraryDayDto, TripPlaceDto} from '../../../shared/api/api'

const stop = (id: string, seq: number, dwell: number, legSec: number | null) => ({
  id, tripPlaceId: `p${id}`, sequence: seq, dwellMinutes: dwell,
  travelModeToReach: 'Drive' as const, legToReach: legSec == null ? null : {seconds: legSec, meters: 1000},
})

const place = (over: Partial<TripPlaceDto> = {}): TripPlaceDto => ({
  id: 'p', tripId: 't', googlePlaceId: null, name: 'x', lat: 0, lng: 0, address: null,
  category: 'See', priceLevel: null, photoUrl: null, bestTimeStart: null, bestTimeEnd: null,
  openingHoursJson: null, feeNote: null, notes: null, ...over,
})
const hours = (periods: unknown) => JSON.stringify({periods})

describe('computeSchedule', () => {
  it('cascades arrival = prev depart + leg; depart = arrival + dwell', () => {
    const day: ItineraryDayDto = {
      id: 'd1', date: '2026-11-14', dayStartTime: '09:00:00',
      stops: [stop('1', 0, 60, null), stop('2', 1, 45, 25 * 60), stop('3', 2, 90, 30 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].arrival).toBe('09:00'); expect(s[0].depart).toBe('10:00')
    expect(s[1].arrival).toBe('10:25'); expect(s[1].depart).toBe('11:10')
    expect(s[2].arrival).toBe('11:40'); expect(s[2].depart).toBe('13:10')
  })
})

describe('dayOfWeek', () => {
  it('maps yyyy-MM-dd to 0=Sunday..6=Saturday (UTC, timezone-stable)', () => {
    expect(dayOfWeek('2026-11-14')).toBe(6) // Saturday
    expect(dayOfWeek('2026-11-15')).toBe(0) // Sunday
  })
})

describe('isOpenAt', () => {
  it('returns null when hours are unknown (no JSON / empty periods / malformed)', () => {
    expect(isOpenAt(null, 1, 600)).toBeNull()
    expect(isOpenAt(undefined, 1, 600)).toBeNull()
    expect(isOpenAt(hours([]), 1, 600)).toBeNull()
    expect(isOpenAt('{not json', 1, 600)).toBeNull()
  })
  it('evaluates a same-day open period', () => {
    const j = hours([{open: {day: 1, hour: 9, minute: 0}, close: {day: 1, hour: 17, minute: 0}}])
    expect(isOpenAt(j, 1, 10 * 60)).toBe(true)
    expect(isOpenAt(j, 1, 8 * 60)).toBe(false)
    expect(isOpenAt(j, 1, 17 * 60)).toBe(false)
    expect(isOpenAt(j, 2, 10 * 60)).toBe(false)
  })
  it('handles an overnight period crossing midnight', () => {
    const j = hours([{open: {day: 5, hour: 18, minute: 0}, close: {day: 6, hour: 2, minute: 0}}])
    expect(isOpenAt(j, 5, 23 * 60)).toBe(true)
    expect(isOpenAt(j, 6, 60)).toBe(true)
    expect(isOpenAt(j, 6, 3 * 60)).toBe(false)
  })
  it('treats an open period with no close as open all that day', () => {
    const j = hours([{open: {day: 0, hour: 0, minute: 0}}])
    expect(isOpenAt(j, 0, 12 * 60)).toBe(true)
    expect(isOpenAt(j, 1, 12 * 60)).toBe(false)
  })
})

describe('computeSchedule overnight', () => {
  it('marks overnight and arrivedAfterMidnight correctly', () => {
    const day: ItineraryDayDto = {
      id: 'd1', date: '2026-11-14', dayStartTime: '22:00:00',
      stops: [stop('1', 0, 120, null), stop('2', 1, 60, 30 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].overnight).toBe(true)                // depart == 1440
    expect(s[0].arrivedAfterMidnight).toBe(false)    // arrival 22:00 < 1440
    expect(s[1].overnight).toBe(true)
    expect(s[1].arrivedAfterMidnight).toBe(true)     // arrival > 1440
  })
  it('does not mark overnight for normal day stops', () => {
    const day: ItineraryDayDto = {
      id: 'd2', date: '2026-11-14', dayStartTime: '09:00:00',
      stops: [stop('1', 0, 60, null), stop('2', 1, 45, 25 * 60)],
    }
    const s = computeSchedule(day)
    expect(s[0].overnight).toBe(false); expect(s[0].arrivedAfterMidnight).toBe(false)
    expect(s[1].overnight).toBe(false); expect(s[1].arrivedAfterMidnight).toBe(false)
  })
})

describe('offWindowFlag', () => {
  it('null inside window (bounds inclusive)', () => {
    const p = place({bestTimeStart: '08:00:00', bestTimeEnd: '10:00:00'})
    expect(offWindowFlag(p, '09:00')).toBeNull()
    expect(offWindowFlag(p, '08:00')).toBeNull()
    expect(offWindowFlag(p, '10:00')).toBeNull()
  })
  it('after window → suggestion, windowDir after', () => {
    const p = place({bestTimeStart: '12:00:00', bestTimeEnd: '13:00:00'})
    expect(offWindowFlag(p, '14:41')).toMatchObject({
      reason: 'off-window', severity: 'suggestion', windowDir: 'after', bestStart: '12:00', bestEnd: '13:00',
    })
  })
  it('before window → windowDir before', () => {
    const p = place({bestTimeStart: '17:30:00', bestTimeEnd: '18:30:00'})
    expect(offWindowFlag(p, '13:50')).toMatchObject({windowDir: 'before'})
  })
  it('null when no window set', () => {
    expect(offWindowFlag(place(), '13:50')).toBeNull()
  })
})

describe('closedFlag', () => {
  it('before-open: opens later today, not opened yet', () => {
    const j = hours([{open: {day: 1, hour: 10}, close: {day: 1, hour: 18}}])
    expect(closedFlag(j, 1, 9 * 60)).toMatchObject({reason: 'closed', severity: 'problem', closedKind: 'before-open', reopenAt: '10:00'})
  })
  it('on-break: split hours, arrive during the gap', () => {
    const j = hours([{open: {day: 1, hour: 11}, close: {day: 1, hour: 14}}, {open: {day: 1, hour: 17}, close: {day: 1, hour: 22}}])
    expect(closedFlag(j, 1, 15 * 60)).toMatchObject({closedKind: 'on-break', reopenAt: '17:00'})
  })
  it('after-close: opened earlier, now past last close', () => {
    const j = hours([{open: {day: 1, hour: 9}, close: {day: 1, hour: 17}}])
    const f = closedFlag(j, 1, 18 * 60)
    expect(f).toMatchObject({closedKind: 'after-close'})
    expect(f?.reopenAt).toBeUndefined()
  })
  it('all-day: no period this weekday', () => {
    const j = hours([{open: {day: 2, hour: 9}, close: {day: 2, hour: 17}}]) // only Tuesday
    expect(closedFlag(j, 1, 12 * 60)).toMatchObject({closedKind: 'all-day'})
  })
  it('null when hours unknown', () => {
    expect(closedFlag(null, 1, 12 * 60)).toBeNull()
  })
})

describe('composeFlags', () => {
  it('overflow fires once, on the first stop reached after midnight', () => {
    const day: ItineraryDayDto = {
      id: 'd', date: '2026-11-14', dayStartTime: '22:00:00',
      stops: [stop('1', 0, 120, null), stop('2', 1, 60, 30 * 60), stop('3', 2, 60, 30 * 60)],
    }
    const composed = composeFlags(computeSchedule(day), {}, dayOfWeek(day.date))
    expect(composed.filter(c => c.flag?.reason === 'overflow')).toHaveLength(1)
    expect(composed[1].flag).toMatchObject({reason: 'overflow', severity: 'problem', arrival: '00:30'})
    expect(composed[2].flag).toBeNull()
  })
  it('no overflow when only the departure crosses midnight', () => {
    const day: ItineraryDayDto = {
      id: 'd', date: '2026-11-14', dayStartTime: '23:00:00',
      stops: [stop('1', 0, 90, null)], // arrival 23:00, depart 00:30
    }
    const composed = composeFlags(computeSchedule(day), {}, dayOfWeek(day.date))
    expect(composed.some(c => c.flag?.reason === 'overflow')).toBe(false)
  })
  it('closed outranks off-window on the same stop', () => {
    const p = place({
      bestTimeStart: '12:00:00', bestTimeEnd: '13:00:00',
      openingHoursJson: hours([{open: {day: 6, hour: 10}, close: {day: 6, hour: 11}}]), // Sat 10–11
    })
    const day: ItineraryDayDto = {id: 'd', date: '2026-11-14', dayStartTime: '14:00:00', stops: [stop('1', 0, 30, null)]}
    const composed = composeFlags(computeSchedule(day), {p1: p}, dayOfWeek(day.date))
    expect(composed[0].flag?.reason).toBe('closed')
  })
  it('null flag for a well-timed open stop with no window', () => {
    const p = place({openingHoursJson: hours([{open: {day: 6, hour: 8}, close: {day: 6, hour: 20}}])})
    const day: ItineraryDayDto = {id: 'd', date: '2026-11-14', dayStartTime: '10:00:00', stops: [stop('1', 0, 30, null)]}
    const composed = composeFlags(computeSchedule(day), {p1: p}, dayOfWeek(day.date))
    expect(composed[0].flag).toBeNull()
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/pages/trips/hooks/useSchedule.test.ts`
Expected: FAIL — `offWindowFlag`, `closedFlag`, `composeFlags` are not exported (and `flagStop` no longer imported).

- [ ] **Step 3: Rewrite `useSchedule.ts`**

Overwrite `frontend/src/pages/trips/hooks/useSchedule.ts` with (keeps `isOpenAt` body byte-for-byte unchanged):

```ts
// frontend/src/pages/trips/hooks/useSchedule.ts
import {useMemo} from 'react'
import type {ItineraryDayDto, StopDto, TripPlaceDto} from '../../../shared/api/api'

export interface ScheduledStop {
  stop: StopDto
  arrival: string
  depart: string
  overnight: boolean
  arrivedAfterMidnight: boolean // raw arrival >= 1440 (this stop is reached after midnight)
}

export type FlagReason = 'overflow' | 'closed' | 'off-window'
export type FlagSeverity = 'problem' | 'suggestion'
export type ClosedKind = 'before-open' | 'on-break' | 'after-close' | 'all-day'

export interface TimingFlag {
  reason: FlagReason
  severity: FlagSeverity
  closedKind?: ClosedKind
  reopenAt?: string             // 'HH:MM' — before-open | on-break
  bestStart?: string            // 'HH:MM' — off-window
  bestEnd?: string              // 'HH:MM' — off-window
  windowDir?: 'before' | 'after'
  arrival?: string              // 'HH:MM' (post-midnight) — overflow
}
export type StopFlag = TimingFlag | null
export type ScheduledStopWithFlag = ScheduledStop & {flag: StopFlag}

const toMin = (hhmm: string) => { const [h, m] = hhmm.slice(0, 5).split(':').map(Number); return h * 60 + m }
const fromMin = (min: number) => `${String(Math.floor((min % 1440) / 60)).padStart(2, '0')}:${String(min % 60).padStart(2, '0')}`

/** Weekday (0 = Sunday … 6 = Saturday) for a 'yyyy-MM-dd' date — matches Places API day numbering. */
export function dayOfWeek(isoDate: string): number {
  const [y, m, d] = isoDate.slice(0, 10).split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay()
}

interface OhPoint { day: number; hour: number; minute?: number }
interface OhPeriod { open?: OhPoint; close?: OhPoint }
const pointMin = (p: OhPoint) => p.hour * 60 + (p.minute ?? 0)

/**
 * Is the place open at `minutes` past midnight on weekday `dow`, per the Places API
 * `regularOpeningHours` snapshot? Returns null when hours are unknown.
 */
export function isOpenAt(openingHoursJson: string | null | undefined, dow: number, minutes: number): boolean | null {
  if (!openingHoursJson) return null
  let periods: OhPeriod[] | undefined
  try { periods = (JSON.parse(openingHoursJson) as {periods?: OhPeriod[]}).periods } catch { return null }
  if (!periods || periods.length === 0) return null // 24h / always-open / unknown
  for (const p of periods) {
    if (!p.open) continue
    const openMin = p.open.hour * 60 + (p.open.minute ?? 0)
    if (!p.close) { if (p.open.day === dow) return true; continue }
    const closeMin = p.close.hour * 60 + (p.close.minute ?? 0)
    if (p.open.day === p.close.day) {
      if (p.open.day === dow && minutes >= openMin && minutes < closeMin) return true
    } else {
      if (p.open.day === dow && minutes >= openMin) return true
      if (p.close.day === dow && minutes < closeMin) return true
    }
  }
  return false
}

function parsePeriods(json: string | null | undefined): OhPeriod[] | null {
  if (!json) return null
  try {
    const periods = (JSON.parse(json) as {periods?: OhPeriod[]}).periods
    return periods && periods.length ? periods : null
  } catch { return null }
}

/**
 * Classify why a place is closed at `minutes` on weekday `dow` — call only when
 * isOpenAt(...) === false. Returns null when hours are unknown.
 */
export function closedFlag(openingHoursJson: string | null | undefined, dow: number, minutes: number): TimingFlag | null {
  const periods = parsePeriods(openingHoursJson)
  if (!periods) return null
  let reopen: number | null = null // earliest open later today
  let openedEarlier = false        // opened OR closed earlier today (incl. overnight close)
  for (const p of periods) {
    if (p.open && p.open.day === dow) {
      const om = pointMin(p.open)
      if (om > minutes) reopen = reopen === null ? om : Math.min(reopen, om)
      else openedEarlier = true
    }
    if (p.close && p.close.day === dow && pointMin(p.close) <= minutes) openedEarlier = true
  }
  const closedKind: ClosedKind =
    reopen !== null ? (openedEarlier ? 'on-break' : 'before-open')
                    : (openedEarlier ? 'after-close' : 'all-day')
  return {reason: 'closed', severity: 'problem', closedKind, reopenAt: reopen !== null ? fromMin(reopen) : undefined}
}

/** Flag when arrival is outside the place's best-time window; null when inside (bounds inclusive) or no window set. */
export function offWindowFlag(place: TripPlaceDto, arrival: string): TimingFlag | null {
  if (!place.bestTimeStart || !place.bestTimeEnd) return null
  const a = toMin(arrival)
  const start = toMin(place.bestTimeStart)
  const end = toMin(place.bestTimeEnd)
  if (a >= start && a <= end) return null
  return {
    reason: 'off-window',
    severity: 'suggestion',
    bestStart: place.bestTimeStart.slice(0, 5),
    bestEnd: place.bestTimeEnd.slice(0, 5),
    windowDir: a < start ? 'before' : 'after',
  }
}

/** Forward cascade: arrival[0] = dayStart; depart = arrival + dwell; arrival[i+1] = depart + leg (ADR-008). */
export function computeSchedule(day: ItineraryDayDto): ScheduledStop[] {
  const result: ScheduledStop[] = []
  let cursor = toMin(day.dayStartTime)
  for (const stop of [...day.stops].sort((a, b) => a.sequence - b.sequence)) {
    const arrival = cursor + (stop.legToReach ? Math.round(stop.legToReach.seconds / 60) : 0)
    const depart = arrival + stop.dwellMinutes
    result.push({
      stop, arrival: fromMin(arrival), depart: fromMin(depart),
      overnight: arrival >= 1440 || depart >= 1440,
      arrivedAfterMidnight: arrival >= 1440,
    })
    cursor = depart
  }
  return result
}

/**
 * Select one most-severe flag per stop: overflow (once, on the first stop reached
 * after midnight) > closed > off-window; null when none.
 */
export function composeFlags(
  scheduled: ScheduledStop[],
  placesById: Record<string, TripPlaceDto>,
  dow: number,
): ScheduledStopWithFlag[] {
  let overflowShown = false
  return scheduled.map(s => {
    let flag: StopFlag = null
    if (!overflowShown && s.arrivedAfterMidnight) {
      flag = {reason: 'overflow', severity: 'problem', arrival: s.arrival}
      overflowShown = true
    } else {
      const place = placesById[s.stop.tripPlaceId]
      if (place) {
        const arr = toMin(s.arrival)
        if (isOpenAt(place.openingHoursJson, dow, arr) === false) flag = closedFlag(place.openingHoursJson, dow, arr)
        if (!flag) flag = offWindowFlag(place, s.arrival)
      }
    }
    return {...s, flag}
  })
}

export function useSchedule(day: ItineraryDayDto, placesById: Record<string, TripPlaceDto>) {
  return useMemo(() => {
    const scheduled = composeFlags(computeSchedule(day), placesById, dayOfWeek(day.date))
    const totalTravelSeconds = day.stops.reduce((sum, st) => sum + (st.legToReach?.seconds ?? 0), 0)
    const dayEnd = scheduled.length ? scheduled[scheduled.length - 1].depart : day.dayStartTime.slice(0, 5)
    return {scheduled, dayEnd, totalTravelSeconds}
  }, [day, placesById])
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/pages/trips/hooks/useSchedule.test.ts`
Expected: PASS (all describes green).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/hooks/useSchedule.ts frontend/src/pages/trips/hooks/useSchedule.test.ts
git commit -m "feat(trips): typed timing-flag domain (reason+severity, single-most-severe)"
```

---

### Task 2: Thai wording map (`timingFlag.ts`)

**Files:**
- Create: `frontend/src/pages/trips/timingFlag.ts`
- Test: `frontend/src/pages/trips/timingFlag.test.ts`

**Interfaces:**
- Consumes: `TimingFlag` from `./hooks/useSchedule`
- Produces: `flagText(flag: TimingFlag): { reasonLine: string; fixLine: string }`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/trips/timingFlag.test.ts`:

```ts
// frontend/src/pages/trips/timingFlag.test.ts
import {describe, it, expect} from 'vitest'
import {flagText} from './timingFlag'
import type {TimingFlag} from './hooks/useSchedule'

describe('flagText', () => {
  it('overflow', () => {
    const f: TimingFlag = {reason: 'overflow', severity: 'problem', arrival: '00:20'}
    expect(flagText(f)).toEqual({reasonLine: 'แผนวันนี้ยาวข้ามเที่ยงคืน (ถึง 00:20)', fixLine: 'ตัดจุดแวะออก หรือเริ่มวันให้เร็วขึ้น'})
  })
  it('closed before-open', () => {
    const f: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'before-open', reopenAt: '10:00'}
    expect(flagText(f)).toEqual({reasonLine: 'ยังไม่เปิดตอนไปถึง · เปิด 10:00', fixLine: 'เลื่อนสตอปนี้ไปช่วงสาย'})
  })
  it('closed on-break', () => {
    const f: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'on-break', reopenAt: '17:00'}
    expect(flagText(f)).toEqual({reasonLine: 'ปิดพักช่วงนี้ · เปิดอีกที 17:00', fixLine: 'เลี่ยงช่วงพักกลางวัน'})
  })
  it('closed after-close', () => {
    const f: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'after-close'}
    expect(flagText(f)).toEqual({reasonLine: 'ร้านปิดแล้วตอนไปถึง', fixLine: 'เลื่อนสตอปนี้ให้เร็วขึ้น'})
  })
  it('closed all-day', () => {
    const f: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'all-day'}
    expect(flagText(f)).toEqual({reasonLine: 'ร้านปิดทั้งวันนี้', fixLine: 'ย้ายไปวันอื่น หรือเอาออก'})
  })
  it('off-window after', () => {
    const f: TimingFlag = {reason: 'off-window', severity: 'suggestion', windowDir: 'after', bestStart: '12:00', bestEnd: '13:00'}
    expect(flagText(f)).toEqual({reasonLine: 'ไปถึงหลังช่วงแนะนำ · ช่วงเหมาะ 12:00–13:00', fixLine: 'เลื่อนสตอปนี้ขึ้นก่อนหน้า'})
  })
  it('off-window before', () => {
    const f: TimingFlag = {reason: 'off-window', severity: 'suggestion', windowDir: 'before', bestStart: '17:30', bestEnd: '18:30'}
    expect(flagText(f)).toEqual({reasonLine: 'ไปถึงก่อนช่วงแนะนำ · ช่วงเหมาะ 17:30–18:30', fixLine: 'เลื่อนสตอปนี้ไปช่วงหลัง'})
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/timingFlag.test.ts`
Expected: FAIL — cannot find module `./timingFlag`.

- [ ] **Step 3: Create `timingFlag.ts`**

```ts
// frontend/src/pages/trips/timingFlag.ts
// Single source of truth for a timing flag's Thai reason + suggested-fix wording.
// Data-only (no JSX) — mirrors placeCategory.ts. Copy is verbatim from the design
// spec §5. The em-dash between best-window times is EN DASH (–, U+2013).
import type {TimingFlag} from './hooks/useSchedule'

export function flagText(flag: TimingFlag): {reasonLine: string; fixLine: string} {
  switch (flag.reason) {
    case 'overflow':
      return {
        reasonLine: `แผนวันนี้ยาวข้ามเที่ยงคืน (ถึง ${flag.arrival})`,
        fixLine: 'ตัดจุดแวะออก หรือเริ่มวันให้เร็วขึ้น',
      }
    case 'off-window':
      return {
        reasonLine: `${flag.windowDir === 'before' ? 'ไปถึงก่อนช่วงแนะนำ' : 'ไปถึงหลังช่วงแนะนำ'} · ช่วงเหมาะ ${flag.bestStart}–${flag.bestEnd}`,
        fixLine: flag.windowDir === 'before' ? 'เลื่อนสตอปนี้ไปช่วงหลัง' : 'เลื่อนสตอปนี้ขึ้นก่อนหน้า',
      }
    case 'closed':
      switch (flag.closedKind) {
        case 'before-open': return {reasonLine: `ยังไม่เปิดตอนไปถึง · เปิด ${flag.reopenAt}`, fixLine: 'เลื่อนสตอปนี้ไปช่วงสาย'}
        case 'on-break':    return {reasonLine: `ปิดพักช่วงนี้ · เปิดอีกที ${flag.reopenAt}`, fixLine: 'เลี่ยงช่วงพักกลางวัน'}
        case 'after-close': return {reasonLine: 'ร้านปิดแล้วตอนไปถึง', fixLine: 'เลื่อนสตอปนี้ให้เร็วขึ้น'}
        default:            return {reasonLine: 'ร้านปิดทั้งวันนี้', fixLine: 'ย้ายไปวันอื่น หรือเอาออก'}
      }
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run src/pages/trips/timingFlag.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/timingFlag.ts frontend/src/pages/trips/timingFlag.test.ts
git commit -m "feat(trips): timing-flag Thai wording map"
```

---

### Task 3: Flag icons + CSS tokens/classes

**Files:**
- Create: `frontend/src/pages/trips/components/FlagIcons.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Produces: `LockIcon`, `ClockIcon`, `MoonIcon` (each `() => JSX.Element`); CSS classes `.stop-card.bad`, `.stop-card.bad .stop-rail`, `.flag-note`, `.flag-note.bad`, `.flag-note .fix`; tokens `--bad`, `--bad-bg`, `--warn-ink`.

- [ ] **Step 1: Create the icon components**

Create `frontend/src/pages/trips/components/FlagIcons.tsx` (follows the `NavIcon` precedent — `currentColor`, CSS-sized, `aria-hidden`):

```tsx
// frontend/src/pages/trips/components/FlagIcons.tsx
// Timing-flag reason icons (stroke SVGs, sized by .flag-note svg). Custom SVG per
// the NavIcon precedent — never emoji.
export function LockIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true" focusable="false">
      <rect x="5" y="11" width="14" height="9" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" />
    </svg>
  )
}
export function ClockIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true" focusable="false">
      <circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" />
    </svg>
  )
}
export function MoonIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true" focusable="false">
      <path d="M21 12.8A9 9 0 1 1 11.2 3 7 7 0 0 0 21 12.8z" />
    </svg>
  )
}
```

- [ ] **Step 2: Add the red + amber-ink tokens**

In `frontend/src/pages/trips/trips-tokens.css`, inside the `.trips-page, .trip-detail` block, replace the `--warn` line (currently line 20-21) region so the tokens read:

```css
  --warn:      #b4791f;
  --warn-bg:   #fff4e0;
  --warn-ink:  #7a5310;   /* darker amber for small text — passes AA on --warn-bg */
  --bad:       #b42318;   /* problem severity text — ~5.75:1 on --bad-bg */
  --bad-bg:    #fdeceb;
```

Then **remove** the now-dead positive tokens (currently line 18-19):

```css
  --good:      #1f9d76;
  --good-bg:   #eafaf3;
```

- [ ] **Step 3: Verify `--good` has no other consumer before it is gone**

Run: `cd frontend && grep -rn "\-\-good" src/`
Expected: **no matches** (only `.chip.good` used them, removed in Step 5). If any other match appears, keep the tokens and only remove `.chip.good`.

- [ ] **Step 4: Add the card + flag-note styles; remove the dead chips**

In `frontend/src/pages/trips/trips-tokens.css`, after `.stop-card.warn { … }` (line 130) add:

```css
.stop-card.bad { background: #fffbfb; border-color: #f4d0cc; }
```

After `.stop-card.warn .stop-rail { … }` (line 144) add:

```css
.stop-card.bad .stop-rail { background: #fbe3e0; }
```

**Remove** `.chip.good` and `.chip.warn` (lines 224-225) — both are dead once the ✓/⚠ and +1วัน chips go. **Keep `.chip` and `.chip.dwell`.** Then add the flag-note block after the chips block:

```css
/* ── Timing-flag reason line (one inset banner below the chips) ── */
.flag-note {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin-top: 8px;
  padding: 7px 10px;
  background: var(--warn-bg);   /* amber = suggestion (base) */
  color: var(--warn-ink);
  border-radius: 9px;
  font-size: 11.5px;
  line-height: 1.45;
}
.flag-note svg { width: 14px; height: 14px; flex: none; margin-top: 1px; }
.flag-note b { font-weight: 700; }
.flag-note .fix { color: #8a5e14; font-weight: 600; }
.flag-note .fix::before { content: "— "; color: #c9a24a; }
.flag-note.bad { background: var(--bad-bg); color: var(--bad); }  /* red = problem */
.flag-note.bad .fix { color: #8f2318; }
.flag-note.bad .fix::before { color: #d08a84; }
```

- [ ] **Step 5: Typecheck the new component compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | grep -E "FlagIcons|trips-tokens"`
Expected: no output for `FlagIcons` (CSS is not typechecked). Pre-existing consumer errors elsewhere are expected until Task 5.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/FlagIcons.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): flag icons + red/amber-ink tokens and flag-note styles"
```

---

### Task 4: Itinerary list rendering (`ItineraryStopCard` + `ItineraryTab`)

**Files:**
- Modify: `frontend/src/pages/trips/components/ItineraryStopCard.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`

**Interfaces:**
- Consumes: `StopFlag`, `FlagSeverity` from `../hooks/useSchedule`; `flagText` from `../timingFlag`; `LockIcon`/`ClockIcon`/`MoonIcon` from `./FlagIcons`
- Produces: `ItineraryStopCard` prop `flag: StopFlag`; **removes** props `bestLabel` and `overnight`.

- [ ] **Step 1: Rewrite `ItineraryStopCard.tsx`**

Overwrite `frontend/src/pages/trips/components/ItineraryStopCard.tsx`:

```tsx
// frontend/src/pages/trips/components/ItineraryStopCard.tsx
import type {TripPlaceDto} from '../../../shared/api/api'
import type {FlagReason, FlagSeverity, StopFlag, TimingFlag} from '../hooks/useSchedule'
import {catEmoji} from '../placeCategory'
import {flagText} from '../timingFlag'
import {NavIcon} from './NavIcon'
import {ClockIcon, LockIcon, MoonIcon} from './FlagIcons'

const REASON_ICON: Record<FlagReason, () => JSX.Element> = {
  overflow: MoonIcon,
  closed: LockIcon,
  'off-window': ClockIcon,
}
// Severity enum → CSS class. NEVER interpolate the raw severity string.
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
  place,
  arrival,
  depart,
  dwell,
  flag,
  onEdit,
  onUp,
  onDown,
  canUp,
  canDown,
  navUrl,
  onNavigate,
}: {
  place: TripPlaceDto
  arrival: string
  depart: string
  dwell: number
  flag: StopFlag
  onEdit: () => void
  onUp: () => void
  onDown: () => void
  canUp: boolean
  canDown: boolean
  navUrl: string | null
  onNavigate?: () => void
}) {
  return (
    <div className={`stop-card${flag ? ' ' + CARD_CLASS[flag.severity] : ''}`}>
      <div className="stop-rail">
        <div className="stop-arr">{arrival}</div>
        <div className="stop-dep">→{depart}</div>
      </div>
      <button className="stop-body" onClick={onEdit}>
        <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
        <div className="stop-chips">
          <span className="chip dwell">⏱ อยู่ {dwell} น.</span>
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
      <div className="stop-reorder">
        <button disabled={!canUp} onClick={onUp} aria-label="ขึ้น">▲</button>
        <button disabled={!canDown} onClick={onDown} aria-label="ลง">▼</button>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Update `ItineraryTab.tsx` — remove `bestLabel` and the retired props**

Delete the `bestLabel` function (lines 24-27):

```tsx
function bestLabel(p: TripPlaceDto): string | null {
  if (!p.bestTimeStart || !p.bestTimeEnd) return null
  return `ช่วงดี ${p.bestTimeStart.slice(0, 5)}–${p.bestTimeEnd.slice(0, 5)}`
}
```

In the `<ItineraryStopCard … />` render (lines 231-251), remove the `bestLabel` and `overnight` props so the call reads:

```tsx
                <ItineraryStopCard
                  place={place}
                  arrival={s.arrival}
                  depart={s.depart}
                  dwell={s.stop.dwellMinutes}
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
                />
```

If `TripPlaceDto` is now an unused import in `ItineraryTab.tsx`, leave it — it is still used by `AddStopPicker`'s `places: TripPlaceDto[]` prop (line 40). (Confirm with `npx tsc --noEmit`; remove only if flagged unused.)

- [ ] **Step 3: Typecheck the list files compile against the new flag type**

Run: `cd frontend && npx tsc --noEmit 2>&1 | grep -E "ItineraryStopCard|ItineraryTab"`
Expected: **no output** for these two files. (Errors in `useDayRoute`/`TripMap` remain until Task 5.)

- [ ] **Step 4: Run the full unit test suite (no regressions)**

Run: `cd frontend && npx vitest run src/pages/trips/`
Expected: PASS (hook + wording tests green; there are no card render tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/ItineraryStopCard.tsx frontend/src/pages/trips/components/ItineraryTab.tsx
git commit -m "feat(trips): render timing-flag reason line on the stop card"
```

---

### Task 5: Map pins reflect severity (`useDayRoute` + `TripMap`)

**Files:**
- Modify: `frontend/src/pages/trips/hooks/useDayRoute.ts`
- Modify: `frontend/src/pages/trips/components/TripMap.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: `FlagSeverity` from `./useSchedule`; `flagText` from `../timingFlag`
- Produces: `RouteStop.severity: FlagSeverity | null`, `RouteStop.flagNote: string | null` (replaces `RouteStop.amber`)

- [ ] **Step 1: Update `useDayRoute.ts` — severity + aria text on `RouteStop`**

Add imports at the top (after the existing imports):

```ts
import type {FlagSeverity} from './useSchedule'
import {flagText} from '../timingFlag'
```

Replace the `amber` field in the `RouteStop` interface (line 20):

```ts
  severity: FlagSeverity | null // pin colour: problem=red, suggestion=amber, null=teal
  flagNote: string | null       // reason line for the marker's accessible name
```

Replace the object built in the `route` map (lines 59-67) so the returned object ends with:

```ts
          return {
            id: s.stop.id,
            lat: p.lat,
            lng: p.lng,
            name: p.name,
            arrival: s.arrival,
            order: i + 1,
            severity: s.flag?.severity ?? null,
            flagNote: s.flag ? flagText(s.flag).reasonLine : null,
          }
```

- [ ] **Step 2: Update `TripMap.tsx` — pin class per severity + aria-label**

Add a lookup near the top of the module (after imports, before the component):

```tsx
import type {FlagSeverity} from '../hooks/useSchedule'
// Severity enum → route-pin CSS modifier. NEVER interpolate the raw severity string
// (`.route-pin.suggestion` does not exist).
const PIN_CLASS: Record<FlagSeverity, string> = {problem: 'problem', suggestion: 'amber'}
```

Replace the pin wrapper (line 151) `<div className={`route-pin${r.amber ? ' amber' : ''}`}>` with:

```tsx
                  <div
                    className={`route-pin${r.severity ? ' ' + PIN_CLASS[r.severity] : ''}`}
                    aria-label={r.flagNote ? `${r.name} — ${r.flagNote}` : undefined}
                  >
```

- [ ] **Step 3: Add the red map-pin CSS**

In `frontend/src/pages/trips/trips-tokens.css`, after the `.route-pin.amber .route-dot { … }` block (lines 280-283) add:

```css
.route-pin.problem .route-callout { color: var(--bad); }
.route-pin.problem .route-dot {
  background: #d64027;
  box-shadow: 0 4px 12px rgba(214, 64, 39, 0.5);
}
```

- [ ] **Step 4: Full typecheck now passes**

Run: `cd frontend && npx tsc --noEmit`
Expected: **no errors** — every flag consumer is migrated. (If `RouteStop.amber` is referenced anywhere else, `grep -rn "\.amber" src/pages/trips` and migrate it.)

- [ ] **Step 5: Full unit suite + build**

Run: `cd frontend && npx vitest run && npm run build`
Expected: tests PASS; production build succeeds.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/hooks/useDayRoute.ts frontend/src/pages/trips/components/TripMap.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): map pins reflect flag severity (teal/amber/red) with accessible names"
```

---

### Task 6: Manual verification against the mock

**Files:** none (verification only).

- [ ] **Step 1: Run the app and open a trip's itinerary**

Run: `cd frontend && npm run dev` — open a trip with a day whose stops exercise the states.

- [ ] **Step 2: Verify each state matches the mock**

Check against [the mock](../../mocks/trip-timing-flag-redesign-mock.html):
- A closed-at-arrival stop → **red** card + lock icon + `ยังไม่เปิด… / ปิดพัก… / ร้านปิดแล้ว… / ร้านปิดทั้งวันนี้` (as appropriate) + fix line.
- An off-window stop → **amber** card + clock icon + `ไปถึงก่อน/หลังช่วงแนะนำ…` + fix line.
- A past-midnight day → the **first** post-midnight stop shows **one** red moon `แผนวันนี้ยาวข้ามเที่ยงคืน…`; later post-midnight stops do **not** repeat it; **no `+1วัน` chip** anywhere.
- A well-timed stop → **no** flag line (only the dwell chip); no green `✓`.
- The map pins are teal / amber / red matching the list; a flagged pin exposes its reason via the accessible name (inspect the `aria-label` in devtools).

- [ ] **Step 3: Commit any copy/spacing tweaks discovered, if needed**

```bash
git add -A && git commit -m "fix(trips): timing-flag polish from manual verification"
```

(Skip the commit if nothing changed.)

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:
- §4 model → Task 1 · §4.1 evaluators (off-window, closed 4-kinds, next-open) → Task 1 · §4.2 composition + overflow-once → Task 1 · §5 wording → Task 2 · §6 icons → Task 3 · §7 card + retire chips/props/positive-state → Tasks 3-4 · §7.1 CSS/tokens → Task 3 · §8 map + aria → Task 5 · §10 tests → Tasks 1-2 · §11 non-goals respected (no reorder/i18n/callout-text) · §12 icon open-item resolved via NavIcon precedent; overnight-hours edge accepted; `.nav-note` a11y left out of scope.

**2. Placeholder scan** — no `TBD`/`handle edge cases`/`similar to`; every code step carries full code and exact commands.

**3. Type consistency** — `TimingFlag`/`StopFlag`/`FlagSeverity`/`FlagReason`/`ClosedKind` defined in Task 1 are used identically in Tasks 2/4/5; `composeFlags`/`offWindowFlag`/`closedFlag`/`flagText` signatures match across producer and consumer tasks; `RouteStop.severity`/`flagNote` produced in Task 5 Step 1 are consumed in Step 2; the `CARD_CLASS`/`PIN_CLASS` lookups guard the enum-vs-classname trap flagged in the spec.
