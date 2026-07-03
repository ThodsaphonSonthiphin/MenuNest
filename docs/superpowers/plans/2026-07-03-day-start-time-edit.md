# Day-Start-Time Inline Edit — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user set the active itinerary Day's start time by tapping the `เริ่ม HH:mm` value in the day-summary bar, re-cascading the whole schedule immediately.

**Architecture:** Frontend-only. The backend command, PATCH `/api/trips/{id}/days/{dayId}` endpoint, and the RTK `useSetDayStartTimeMutation` (invalidates `TripItinerary`) already exist and are unused. Extract `BestTimeBar`'s private time converters into a shared, unit-tested `utils/time.ts`; add a `DayStartEditor` component that wraps a Syncfusion `TimePicker` styled to read as bar text; wire it into `ItineraryTab` in place of the static value; neutralize the picker chrome in `trips-tokens.css`.

**Tech Stack:** React 19, TypeScript, `@reduxjs/toolkit` (RTK Query), `@syncfusion/react-calendars` `TimePicker` (v33), Vitest (unit). Working directory for all commands: `frontend/`.

## Global Constraints

- **Frontend only** — do NOT touch backend/DB/API; the whole back half is built (spec §1). Design spec: `docs/superpowers/specs/2026-07-03-day-start-time-edit-design.md`; decisions: ADR-012 (inline tap-to-edit), ADR-013 (commit-on-change), ADR-008 (schedule cascade).
- **Syncfusion `TimePicker` props are non-negotiable:** `format="HH:mm"`, `step={15}`, `editable={false}`, `openOnFocus`, `clearButton={false}`. Verified against the installed package: without `openOnFocus`+`editable={false}` a tap only places a caret (raises the mobile keyboard) and the popup opens only via the clock icon; `clearButton` defaults true and a Day's start time is non-nullable. This is a deliberate divergence from `BestTimeBar` (which sets none).
- **CSS targets `sf-*` classes, NOT ej2 `.e-*`** — the React package emits `sf-timepicker` / `sf-input-group` / `sf-input`. Exact sub-element class names (icons) must be confirmed in browser devtools when wiring.
- **Commit on change, optimistic + revert** — fire the mutation on pick; show the picked value optimistically; on failure revert to the server value and surface via the tab's existing `actionError` line. No confirm control. (ADR-013)
- **Per active Day only** — each `ItineraryDay` owns its own `DayStartTime`; the editor targets the active Day. Guard against a slow request resolving after a day switch (`key` per day + an `isMounted` ref).
- **No `@testing-library/react` in this repo** — automated coverage is Vitest over pure functions; component/interaction behavior is verified by `tsc` + `lint` and (once the backend is available) manual/e2e. Reuse `getErrorMessage` from `frontend/src/shared/utils/getErrorMessage.ts`.
- **Runnable gates:** `npx vitest run`, `npx tsc -b`, `npm run lint`. In-app manual verification and Playwright e2e require a running backend + Azure SQL, currently blocked by the disabled subscription (see `playwright.config.ts` header) — deferred, see "Deferred (Phase 2)".

---

## File Structure

- **Create** `frontend/src/pages/trips/utils/time.ts` — `hmsToDate`, `dateToHms` (moved verbatim from `BestTimeBar`). One responsibility: `"HH:mm:ss"` ↔ local-time `Date` conversion.
- **Create** `frontend/src/pages/trips/utils/time.test.ts` — Vitest unit tests for the converters.
- **Modify** `frontend/src/pages/trips/components/BestTimeBar.tsx` — delete the local converters, import them from `utils/time` (no behavior change).
- **Create** `frontend/src/pages/trips/components/DayStartEditor.tsx` — the inline editor component.
- **Modify** `frontend/src/pages/trips/components/ItineraryTab.tsx` — render `DayStartEditor` in the `.day-summary` bar; clear `actionError` on day change; import `useEffect`.
- **Modify** `frontend/src/pages/trips/trips-tokens.css` — `.day-summary`-scoped `sf-*` overrides + the `.day-start-edit` affordance.

---

### Task 1: Extract & unit-test the time converters

**Files:**
- Create: `frontend/src/pages/trips/utils/time.ts`
- Test: `frontend/src/pages/trips/utils/time.test.ts`
- Modify: `frontend/src/pages/trips/components/BestTimeBar.tsx:9-26` (remove local converters), `:1-3` (add import)

**Interfaces:**
- Produces: `hmsToDate(hms: string | null): Date | null`, `dateToHms(date: Date | null): string | null` — exported from `../utils/time`.
- Consumes: nothing (leaf util).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/trips/utils/time.test.ts`:

```ts
// frontend/src/pages/trips/utils/time.test.ts
import {describe, it, expect} from 'vitest'
import {hmsToDate, dateToHms} from './time'

describe('hmsToDate', () => {
  it('parses "HH:mm:ss" into a local-time Date', () => {
    const d = hmsToDate('09:00:00')!
    expect(d.getHours()).toBe(9)
    expect(d.getMinutes()).toBe(0)
    expect(d.getSeconds()).toBe(0)
  })
  it('parses a single-digit / padded value', () => {
    const d = hmsToDate('08:05:00')!
    expect(d.getHours()).toBe(8)
    expect(d.getMinutes()).toBe(5)
  })
  it('returns null for null', () => {
    expect(hmsToDate(null)).toBeNull()
  })
})

describe('dateToHms', () => {
  it('formats a Date into zero-padded "HH:mm:ss"', () => {
    const d = new Date()
    d.setHours(8, 5, 0, 0)
    expect(dateToHms(d)).toBe('08:05:00')
  })
  it('returns null for null', () => {
    expect(dateToHms(null)).toBeNull()
  })
  it('round-trips with hmsToDate', () => {
    expect(dateToHms(hmsToDate('22:30:00'))).toBe('22:30:00')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/pages/trips/utils/time.test.ts`
Expected: FAIL — `Failed to resolve import "./time"` (module does not exist yet).

- [ ] **Step 3: Create the util (moved verbatim from BestTimeBar)**

Create `frontend/src/pages/trips/utils/time.ts`:

```ts
// frontend/src/pages/trips/utils/time.ts
/**
 * Convert a stored "HH:mm:ss" string to a Date (local-time, today's date as base).
 * Avoids TZ-shift issues by using setHours/setMinutes/setSeconds.
 */
export function hmsToDate(hms: string | null): Date | null {
  if (!hms) return null
  const [h, m, s] = hms.slice(0, 8).split(':').map(Number)
  const d = new Date()
  d.setHours(h ?? 0, m ?? 0, s ?? 0, 0)
  return d
}

/** Convert a Date back to "HH:mm:ss" using local-time getters. */
export function dateToHms(date: Date | null): string | null {
  if (!date) return null
  const hh = String(date.getHours()).padStart(2, '0')
  const mm = String(date.getMinutes()).padStart(2, '0')
  const ss = String(date.getSeconds()).padStart(2, '0')
  return `${hh}:${mm}:${ss}`
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/pages/trips/utils/time.test.ts`
Expected: PASS (6 tests).

- [ ] **Step 5: Refactor BestTimeBar to import the shared util**

In `frontend/src/pages/trips/components/BestTimeBar.tsx`, delete the local `hmsToDate` (lines 9-15) and `dateToHms` (lines 20-26) plus their doc-comment block (lines 5-8, 17-19), and add the import below the existing Syncfusion imports (after line 3):

```tsx
import {hmsToDate, dateToHms} from '../utils/time'
```

The rest of `BestTimeBar` (its `handleStartChange` / `handleEndChange` using `hmsToDate`/`dateToHms`) is unchanged.

- [ ] **Step 6: Verify the whole unit suite + typecheck + lint**

Run: `npx vitest run`
Expected: PASS (existing `useSchedule.test.ts` + new `time.test.ts`).
Run: `npx tsc -b`
Expected: no type errors.
Run: `npm run lint`
Expected: no lint errors.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/trips/utils/time.ts frontend/src/pages/trips/utils/time.test.ts frontend/src/pages/trips/components/BestTimeBar.tsx
git commit -m "refactor(trips): extract hms<->Date converters into utils/time with unit tests"
```

---

### Task 2: DayStartEditor component

**Files:**
- Create: `frontend/src/pages/trips/components/DayStartEditor.tsx`

**Interfaces:**
- Consumes: `hmsToDate`, `dateToHms` from `../utils/time` (Task 1); `useSetDayStartTimeMutation` from `../../../shared/api/api`; `getErrorMessage` from `../../../shared/utils/getErrorMessage`.
- Produces: `DayStartEditor` (default-less named export) with props `{ tripId: string; dayId: string; dayStartTime: string; onError: (msg: string | null) => void }`.

- [ ] **Step 1: Create the component**

Create `frontend/src/pages/trips/components/DayStartEditor.tsx`:

```tsx
// frontend/src/pages/trips/components/DayStartEditor.tsx
import {useEffect, useRef, useState} from 'react'
import {TimePicker} from '@syncfusion/react-calendars'
import type {TimePickerChangeEvent} from '@syncfusion/react-calendars'
import {useSetDayStartTimeMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {hmsToDate, dateToHms} from '../utils/time'

/**
 * The active Day's start time, rendered as the `เริ่ม HH:mm` value in the
 * day-summary bar and editable in place. Tapping the value opens a Syncfusion
 * TimePicker (editable=false + openOnFocus make it read as a label that opens on
 * tap); picking a time commits immediately (ADR-013) and the schedule re-cascades
 * via TripItinerary invalidation. The picked value shows optimistically and reverts
 * on failure. Parent passes key={dayId} so each Day gets a fresh instance.
 */
export function DayStartEditor({
  tripId,
  dayId,
  dayStartTime,
  onError,
}: {
  tripId: string
  dayId: string
  dayStartTime: string // "HH:mm:ss"
  onError: (msg: string | null) => void
}) {
  const [value, setValue] = useState<string>(dayStartTime)
  const [setDayStart] = useSetDayStartTimeMutation()

  // True while mounted — guards the async resolution from touching parent state
  // after a day switch unmounts this instance (key={dayId}).
  const mounted = useRef(true)
  useEffect(() => () => {
    mounted.current = false
  }, [])

  // Re-sync the displayed value to the server value after a refetch. Between a
  // pick and the refetch the local value is optimistic.
  useEffect(() => {
    setValue(dayStartTime)
  }, [dayStartTime])

  const handleChange = async (e: TimePickerChangeEvent) => {
    const hms = dateToHms(e.value)
    if (!hms || hms === value) return // ignore a cleared / unchanged pick
    setValue(hms) // optimistic
    try {
      await setDayStart({tripId, dayId, startTime: hms}).unwrap()
      if (mounted.current) onError(null)
    } catch (err) {
      if (mounted.current) {
        setValue(dayStartTime) // revert to server value
        onError(getErrorMessage(err))
      }
    }
  }

  return (
    <span className="day-start-edit">
      เริ่ม{' '}
      <TimePicker
        className="day-start-picker"
        value={hmsToDate(value)}
        onChange={handleChange}
        format="HH:mm"
        step={15}
        editable={false}
        openOnFocus
        clearButton={false}
      />
    </span>
  )
}
```

- [ ] **Step 2: Typecheck the new component**

Run: `npx tsc -b`
Expected: no type errors. (If `TimePickerChangeEvent` / any prop name mismatches the installed `@syncfusion/react-calendars` types, fix by matching `BestTimeBar.tsx`'s imports and the package's `timepicker/types.d.ts` — the props `editable`, `openOnFocus`, `clearButton`, `value`, `onChange`, `format`, `step` are all present in v33.)

- [ ] **Step 3: Lint**

Run: `npm run lint`
Expected: no lint errors (no unused imports; hooks rules satisfied — both effects and the ref are unconditional).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/trips/components/DayStartEditor.tsx
git commit -m "feat(trips): add DayStartEditor (inline TimePicker, commit-on-change, optimistic revert)"
```

---

### Task 3: Wire into ItineraryTab + neutralize the picker chrome

**Files:**
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx:2` (import `useEffect`), `:18` (import `DayStartEditor`), after `:113` (clear-error effect), `:146-149` (render editor)
- Modify: `frontend/src/pages/trips/trips-tokens.css` (append `.day-summary` picker overrides)

**Interfaces:**
- Consumes: `DayStartEditor` (Task 2); the existing `actionError`/`setActionError` state ([ItineraryTab.tsx:96](../../../frontend/src/pages/trips/components/ItineraryTab.tsx#L96)) and `.trips-field-error` line ([:158](../../../frontend/src/pages/trips/components/ItineraryTab.tsx#L158)).
- Produces: nothing downstream.

- [ ] **Step 1: Add the imports**

In `frontend/src/pages/trips/components/ItineraryTab.tsx`, change line 2:

```tsx
import {useState, useEffect} from 'react'
```

and add after the `StopEditorDialog` import (line 18):

```tsx
import {DayStartEditor} from './DayStartEditor'
```

- [ ] **Step 2: Clear stale error on day change**

Immediately after the `useSchedule(...)` call (line 113) and before `const trip = ...` (line 115) — i.e. while still above the `if (!dayList.length)` early return so hook order stays stable — add:

```tsx
  // Clear any stale start-time error when the active day changes, so a failure
  // on one Day never surfaces against another.
  useEffect(() => {
    setActionError(null)
  }, [dayId])
```

- [ ] **Step 3: Replace the static value with the editor**

Replace the `เริ่ม` span (lines 146-149):

```tsx
        <span>
          เริ่ม <b>{resolvedDay.dayStartTime.slice(0, 5)}</b>
        </span>
```

with:

```tsx
        <DayStartEditor
          key={resolvedDayId}
          tripId={tripId}
          dayId={resolvedDayId}
          dayStartTime={resolvedDay.dayStartTime}
          onError={setActionError}
        />
```

The surrounding `<div className="day-summary">` and the `เสร็จ` / `เดินทางรวม` spans are left untouched.

- [ ] **Step 4: Add the CSS (starting point — verify class names in devtools)**

Append to `frontend/src/pages/trips/trips-tokens.css`:

```css
/* ── Editable day-start value in the summary bar (ADR-012 / ADR-013) ── */
.day-start-edit { display: inline-flex; align-items: center; gap: 6px; }

/* Neutralize the Syncfusion TimePicker chrome so it reads as bar text at rest.
   The React package (@syncfusion/react-calendars v33) emits sf-* classes, NOT
   the legacy ej2 .e-*. Confirm exact sub-element classes in devtools. */
.day-summary .day-start-picker.sf-input-group,
.day-summary .day-start-picker {
  width: auto;
  display: inline-flex;
  margin: 0;
  padding: 2px 6px;
  border: 1px solid rgba(255, 255, 255, 0.18);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  transition: border-color 0.12s ease, background 0.12s ease;
}
/* remove the animated focus underline */
.day-summary .day-start-picker.sf-input-group::before,
.day-summary .day-start-picker.sf-input-group::after { display: none; }

.day-summary .day-start-picker:hover,
.day-summary .day-start-picker:focus-within {
  border-color: var(--teal);
  background: rgba(14, 143, 158, 0.18);
}

.day-summary .day-start-picker .sf-input {
  width: 5ch;
  min-width: 0;
  padding: 0;
  border: 0;
  background: transparent;
  color: #fff;
  font-weight: 700;
  font-size: 12px;
  font-family: 'Spline Sans Mono', ui-monospace, monospace;
  text-align: left;
  cursor: pointer;
}
/* clock icon tint (confirm the icon's sf-* class in devtools) */
.day-summary .day-start-picker .sf-time-icon,
.day-summary .day-start-picker .sf-input-group-icon { color: #9fb0c4; }
```

- [ ] **Step 5: Typecheck, lint, unit suite**

Run: `npx tsc -b`
Expected: no type errors.
Run: `npm run lint`
Expected: no lint errors.
Run: `npx vitest run`
Expected: PASS (all existing + `time.test.ts`).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): edit day start time inline on the summary bar"
```

- [ ] **Step 7: Manual verification (when a backend is available)**

Requires a running API + itinerary data (blocked while the subscription is disabled — see Global Constraints). When available, with `npm run dev`:
1. Open a trip → Itinerary tab. Confirm `เริ่ม HH:mm` shows a faint bordered chip + clock cue; `เสร็จ` / `เดินทางรวม` stay plain text. (mock: `docs/mocks/day-start-edit-mock.html` panel A)
2. Tap the value → the TimePicker popup opens on tap (no keyboard, no caret). Pick a different time.
3. Confirm every stop's arrival, `เสร็จ`, and `เดินทางรวม` shift (re-cascade). (panel C)
4. Switch to another day, then back — the value reflects each day's own start time.
5. Adjust the `sf-*` CSS selectors in devtools if any override does not bite; re-commit CSS-only if changed.

---

## Deferred (Phase 2) — Playwright e2e

An end-to-end test ("tap start value → first stop arrival + `เสร็จ` shift") is **deferred**, not built now, because:
- There is **no `trips` mock-route harness** — only `health` has `e2e/helpers/mockRoutes/*`; `budget`/`pomodoro` tests `test.skip()` when unmocked. Covering this feature needs new trips mocks for `getItinerary`, `listTripPlaces`, `listTrips`, and a stateful `setDayStartTime` PATCH that returns a re-cascaded itinerary on refetch.
- `playwright.config.ts` runs **no backend webServer** and its header documents that authed flows are deferred until a deployable env exists (subscription disabled).

Building that harness is disproportionate to a single inline-edit control (owner pattern: defer complex extras). When a trips e2e harness is first needed, add `frontend/e2e/helpers/mockRoutes/tripRoutes.ts` + `frontend/e2e/trips.day-start.spec.ts` following the `episodeRoutes.ts` pattern, asserting the first stop's arrival text changes after a pick. The pure conversion logic is already covered by `utils/time.test.ts`; the component's optimistic/guard logic is covered by `tsc` + `lint` + the manual script above.

---

## Self-Review

**Spec coverage:**
- §4.1 shared `utils/time.ts` + tests → Task 1. ✓
- §4.2 `DayStartEditor` (props, optimistic state, mounted guard, no-op) → Task 2. ✓
- §4.3 `ItineraryTab` wiring (`key`, `onError`, clear-on-day-change) → Task 3 Steps 1-3. ✓
- §4.4 `sf-*` chrome-neutralizing + width CSS → Task 3 Step 4. ✓
- §5 interaction (commit-on-change, invalidation re-cascade) → Task 2 `handleChange`. ✓
- §6 edge cases: overnight (no constraint — nothing added, correct); day-switch (`key` + `mounted` guard + clear-error effect); rapid picks (last-write-wins, not debounced — matches other trips mutations); empty/loading (editor only renders after the `dayList` guard). ✓
- §7 testing: vitest (Task 1) + tsc/lint gates + manual script (Task 3 Step 7); e2e deferred with rationale. ✓
- §8 scope: honored — no bulk/auto-optimize/drag work. ✓

**Placeholder scan:** No TBD/TODO. Every code step shows complete code; every command shows expected output. The only "verify in devtools" note (CSS class names) is an intentional, spec-acknowledged in-app check (§4.4), not a placeholder — the CSS is complete and functional as written.

**Type consistency:** `hmsToDate`/`dateToHms` signatures identical across Task 1 (definition), `BestTimeBar` (Task 1 Step 5), and `DayStartEditor` (Task 2). `DayStartEditor` prop shape `{tripId, dayId, dayStartTime, onError}` identical between Task 2 (definition) and Task 3 (usage). `useSetDayStartTimeMutation` arg `{tripId, dayId, startTime}` matches `api.ts:1308`. `onError` type `(msg: string | null) => void` matches `setActionError` (a `useState<string | null>` setter).
