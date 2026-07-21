# Discover Hourly Weather Strip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a display-only, horizontally-scrolling hourly weather strip in the Discover ("ไปไหนดี") place-detail sheet, auto-loaded when a Place is selected (issue #47).

**Architecture:** Frontend-only. Reuse the coordinate-based hourly forecast plumbing shipped for trips in #46 (`useGetHourlyForecastQuery` → `POST /api/trips/weather/hourly`). Share the one drift-prone pure helper (`hourlyRolloverLabel`) between the trips planner and a new Discover-local `DiscoverHourly` component; keep each page's cell markup + CSS local (ADR-124). No backend, DTO, migration, or Google billing change.

**Tech Stack:** React 19 + TypeScript, Redux Toolkit Query, Vite, Vitest (node env — no DOM/component harness).

## Global Constraints

- **Frontend only** — no backend endpoint / use-case / DTO field / EF entity / migration / Google SKU (reuse #46).
- **No new Google cost** — reuse the existing `forecast/hours` walk (ADR-119); one live call per selected Place, cached 10 min (`keepUnusedDataFor: 600`).
- **No component test harness** — vitest runs `environment: 'node'`; only pure `lib/*.ts` gets unit tests. Rendering/layout is verified by `tsc -b` + `vite build` + **interactive** testing (CLAUDE.md).
- **Icons are inline-SVG / Google weather icons — never emoji** (CLAUDE.md, no-emoji rule).
- **Commit messages reference the ticket** — subject ends `(#47)` (partial) or `(closes #47)` on the last commit (CLAUDE.md).
- **Stage narrowly** — `git add <explicit paths>`, never `-A`/`.`; never stage `daily-state.md` or `AGENTS.md`.
- **Pre-commit hook runs the FULL suite** (backend build+test + frontend `tsc` + `build`) on every commit; expect ~40s and leave the whole suite green.
- **All commands run from** `frontend/` unless noted.

## File Structure

- `frontend/src/pages/trips/lib/weather.ts` — **modify**: add pure `hourlyRolloverLabel`.
- `frontend/src/pages/trips/lib/weather.test.ts` — **create**: unit tests for the helper.
- `frontend/src/pages/trips/components/HourlyPlanner.tsx` — **modify**: import the shared helper, drop the inline copy (behaviour-preserving).
- `frontend/src/pages/discover/components/DiscoverHourly.tsx` — **create**: the display-only strip (query + horizon filter + cells + loading/empty states).
- `frontend/src/pages/discover/components/PlaceSheet.tsx` — **modify**: render `<DiscoverHourly>` between the badges and the reviews section.
- `frontend/src/pages/discover/DiscoverPage.css` — **modify**: add `.disc-wx*` / `.disc-hr*` styles.

---

### Task 1: Shared rollover-label helper (pure, TDD) + adopt in HourlyPlanner

**Files:**
- Modify: `frontend/src/pages/trips/lib/weather.ts`
- Test: `frontend/src/pages/trips/lib/weather.test.ts` (create)
- Modify: `frontend/src/pages/trips/components/HourlyPlanner.tsx:11`, `:92-101`, `:116`

**Interfaces:**
- Produces: `hourlyRolloverLabel(dateStr: string, anchorDateStr: string): string` — both args are `'YYYY-MM-DD'`; returns `'พรุ่งนี้'` when `dateStr` is exactly one day after `anchorDateStr`, else a short Thai `weekday day month` label.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/trips/lib/weather.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {hourlyRolloverLabel} from './weather'

describe('hourlyRolloverLabel', () => {
  it('labels the day after the anchor as พรุ่งนี้', () => {
    expect(hourlyRolloverLabel('2026-07-22', '2026-07-21')).toBe('พรุ่งนี้')
  })
  it('labels a further-out day with its date, not พรุ่งนี้', () => {
    const label = hourlyRolloverLabel('2026-07-23', '2026-07-21')
    expect(label).not.toBe('พรุ่งนี้')
    expect(label).toContain('23')
  })
  it('does not call the anchor day itself พรุ่งนี้', () => {
    expect(hourlyRolloverLabel('2026-07-21', '2026-07-21')).not.toBe('พรุ่งนี้')
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm run test -- src/pages/trips/lib/weather.test.ts`
Expected: FAIL — `hourlyRolloverLabel` is not exported from `./weather`.

- [ ] **Step 3: Implement the helper**

Append to `frontend/src/pages/trips/lib/weather.ts`:

```ts
/** Rollover-divider label for an hourly-strip cell whose date differs from the previous cell's.
 *  `dateStr` / `anchorDateStr` are 'YYYY-MM-DD'. One day after the anchor -> 'พรุ่งนี้'; otherwise a
 *  short Thai weekday+date label. Shared by the trips planner (#46) and the Discover strip (#47). */
export function hourlyRolloverLabel(dateStr: string, anchorDateStr: string): string {
  const deltaDays = Math.round((Date.parse(dateStr) - Date.parse(anchorDateStr)) / 86_400_000)
  return deltaDays === 1
    ? 'พรุ่งนี้'
    : new Date(`${dateStr}T00:00:00`).toLocaleDateString('th-TH', {weekday: 'short', day: 'numeric', month: 'short'})
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npm run test -- src/pages/trips/lib/weather.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Adopt the helper in HourlyPlanner (behaviour-preserving)**

In `frontend/src/pages/trips/components/HourlyPlanner.tsx`:

Line 11 — add the import:
```tsx
import {iconUrl, hourlyRolloverLabel} from '../lib/weather'
```

Lines 92-101 — delete the inline `rolloverLabel` closure, keeping only `todayDate`:
```tsx
  const todayDate = day.date.slice(0, 10)
```

Line 116 — call the shared helper with `todayDate` as the anchor:
```tsx
              {isRollover && <span className="sd-hr-div">{hourlyRolloverLabel(hDate, todayDate)}</span>}
```

- [ ] **Step 6: Type-check + build**

Run: `npm run build`
Expected: `tsc -b` and `vite build` both succeed (no unused-var / type errors from the removed closure).

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/trips/lib/weather.ts frontend/src/pages/trips/lib/weather.test.ts frontend/src/pages/trips/components/HourlyPlanner.tsx
git commit -m "refactor(trips): extract shared hourlyRolloverLabel helper for reuse in Discover (#47)"
```

---

### Task 2: DiscoverHourly component + wire into PlaceSheet + CSS

**Files:**
- Create: `frontend/src/pages/discover/components/DiscoverHourly.tsx`
- Modify: `frontend/src/pages/discover/components/PlaceSheet.tsx:1-6` (import), `:44-45` (render)
- Modify: `frontend/src/pages/discover/DiscoverPage.css` (append `.disc-wx*` block)

**Interfaces:**
- Consumes: `useGetHourlyForecastQuery({lat, lng, hours})` and `HourlyReadingDto` from `shared/api/api`; `iconUrl(iconBaseUri, isDark)` + `hourlyRolloverLabel(dateStr, anchorDateStr)` from `trips/lib/weather`; `withinHorizon(targetMs, nowMs)` from `trips/lib/retiming`; `DiscoverPlaceView` from `../lib/discoverFilter` (carries `lat`, `lng`, `name`, ...).
- Produces: `DiscoverHourly({place: DiscoverPlaceView})` React component.

> No unit tests in this task — the SPA has no component/DOM harness. Verification is `tsc -b` + `vite build` here, then interactive in Task 3.

- [ ] **Step 1: Create the component**

Create `frontend/src/pages/discover/components/DiscoverHourly.tsx`:

```tsx
// frontend/src/pages/discover/components/DiscoverHourly.tsx
// Issue #47: display-only hourly forecast strip in the Discover place-detail sheet.
// Reuses the coordinate-based hourly query + helpers shipped for trips (#46). No retiming (ADR-124).
import {useGetHourlyForecastQuery} from '../../../shared/api/api'
import {iconUrl, hourlyRolloverLabel} from '../../trips/lib/weather'
import {withinHorizon} from '../../trips/lib/retiming'
import type {DiscoverPlaceView} from '../lib/discoverFilter'

const WINDOW_HOURS = 48

export function DiscoverHourly({place}: {place: DiscoverPlaceView}) {
  const {data: allHours = [], isLoading} = useGetHourlyForecastQuery({
    lat: place.lat, lng: place.lng, hours: WINDOW_HOURS,
  })
  // Drop past hours + anything beyond the 10-day forecast horizon — same guard as the trips planner.
  const hours = allHours.filter((h) => withinHorizon(Date.parse(h.displayLocal), Date.now()))

  if (isLoading) {
    return (
      <section className="disc-wx">
        <div className="disc-sec-lab">อากาศรายชั่วโมง</div>
        <div className="disc-wx-strip" aria-hidden="true">
          {Array.from({length: 6}).map((_, i) => <div key={i} className="disc-wx-sk" />)}
        </div>
      </section>
    )
  }
  if (hours.length === 0) {
    return (
      <section className="disc-wx">
        <div className="disc-sec-lab">อากาศรายชั่วโมง</div>
        <p className="disc-wx-empty">ไม่มีข้อมูลอากาศรายชั่วโมง</p>
      </section>
    )
  }

  const anchorDate = hours[0].displayLocal.slice(0, 10)
  const now = hours[0]
  return (
    <section className="disc-wx">
      <div className="disc-wx-head">
        <span className="disc-sec-lab">อากาศรายชั่วโมง</span>
        <span className="disc-wx-now">
          {now.iconBaseUri && <img src={iconUrl(now.iconBaseUri, false)} alt="" width={15} height={15} />}
          ตอนนี้ <b>{now.tempC != null ? `${Math.round(now.tempC)}°` : '—'}</b>
          {now.feelsLikeC != null && <span className="fl">รู้สึก {Math.round(now.feelsLikeC)}°</span>}
        </span>
      </div>
      <div className="disc-wx-strip">
        {hours.map((h, i) => {
          const hDate = h.displayLocal.slice(0, 10)
          const isRollover = i > 0 && hours[i - 1].displayLocal.slice(0, 10) !== hDate
          return (
            <div key={h.displayLocal} className={`disc-hr ${h.isDaytime ? 'day' : 'night'}`}>
              {isRollover && <span className="disc-hr-div">{hourlyRolloverLabel(hDate, anchorDate)}</span>}
              <span className="disc-hr-time">{i === 0 ? 'ตอนนี้' : h.displayLocal.slice(11, 16)}</span>
              {h.iconBaseUri && <img src={iconUrl(h.iconBaseUri, false)} alt={h.conditionType ?? ''} width={22} height={22} />}
              {h.tempC != null && <span className="disc-hr-temp">{Math.round(h.tempC)}°</span>}
              {h.feelsLikeC != null && <span className="disc-hr-feels">รู้สึก {Math.round(h.feelsLikeC)}°</span>}
              <span className={`disc-hr-rain${h.rainPct ? '' : ' dry'}`}>{h.rainPct ? `ฝน ${h.rainPct}%` : 'แห้ง'}</span>
            </div>
          )
        })}
      </div>
    </section>
  )
}
```

- [ ] **Step 2: Add the CSS**

Append to `frontend/src/pages/discover/DiscoverPage.css`:

```css
/* ── Hourly weather strip in the place-detail sheet (issue #47) ── */
.disc-wx { margin: 2px 0 14px; }
.disc-wx-head { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; margin-bottom: 6px; }
.disc-wx-now { display: inline-flex; align-items: center; gap: 5px; font-size: 11.5px; font-weight: 700; color: #475569; }
.disc-wx-now img { width: 15px; height: 15px; }
.disc-wx-now b { font-size: 13px; font-weight: 800; color: var(--ink); }
.disc-wx-now .fl { color: var(--muted); font-weight: 700; }

.disc-wx-strip { display: flex; gap: 6px; overflow-x: auto; padding: 2px 2px 8px; scroll-snap-type: x proximity; }
.disc-wx-strip::-webkit-scrollbar { height: 5px; }
.disc-wx-strip::-webkit-scrollbar-thumb { background: #d5dde5; border-radius: 999px; }

.disc-hr { flex: none; scroll-snap-align: start; min-width: 58px; display: flex; flex-direction: column;
  align-items: center; gap: 2px; border: 1px solid var(--border); border-radius: 12px; padding: 7px 6px 8px; background: #fffdf5; }
.disc-hr.night { background: #f3f5fc; border-color: #e4e8f6; }
.disc-hr-div { align-self: stretch; text-align: center; font-size: 9px; font-weight: 800; white-space: nowrap;
  color: var(--teal-deep); background: var(--teal-soft); border-radius: 999px; padding: 1px 7px; margin-bottom: 2px; }
.disc-hr-time { font-size: 10.5px; font-weight: 700; color: var(--muted); }
.disc-hr:first-child .disc-hr-time { color: var(--teal-deep); }
.disc-hr img { width: 22px; height: 22px; }
.disc-hr-temp { font-size: 14px; font-weight: 800; color: var(--ink); letter-spacing: -0.01em; }
.disc-hr-feels { font-size: 9px; color: var(--muted); line-height: 1.1; }
.disc-hr-rain { font-size: 9px; font-weight: 700; color: #1863b7; }
.disc-hr-rain.dry { color: #cbd5e1; }

.disc-wx-sk { flex: none; width: 58px; height: 82px; border-radius: 12px;
  background: linear-gradient(100deg, #eef2f6 30%, #f6f9fb 50%, #eef2f6 70%); background-size: 200% 100%;
  animation: disc-wx-sh 1.1s linear infinite; }
@keyframes disc-wx-sh { to { background-position: -200% 0; } }
.disc-wx-empty { margin: 0; padding: 12px; text-align: center; font-size: 12px; color: var(--muted);
  background: var(--page); border: 1px solid var(--border); border-radius: 12px; }
```

- [ ] **Step 3: Wire it into PlaceSheet**

In `frontend/src/pages/discover/components/PlaceSheet.tsx`:

Add the import (after the existing imports, e.g. after line 6):
```tsx
import {DiscoverHourly} from './DiscoverHourly'
```

Render it right after the badges block closes (`</div>` at line 44), before the reviews block (`{place.reviewLinks.length > 0 && (` at line 45):
```tsx
      </div>
      <DiscoverHourly place={place} />
      {place.reviewLinks.length > 0 && (
```

- [ ] **Step 4: Type-check + build**

Run: `npm run build`
Expected: `tsc -b` and `vite build` both succeed — no type errors, `DiscoverHourly` resolves, cross-imports from `trips/*` resolve.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/discover/components/DiscoverHourly.tsx frontend/src/pages/discover/components/PlaceSheet.tsx frontend/src/pages/discover/DiscoverPage.css
git commit -m "feat(discover): show hourly weather strip in the place-detail sheet (closes #47)"
```

---

### Task 3: Interactive verification (mandatory before push)

**Files:** none unless a bug is found.

The SPA has no rendering/layout test coverage, prod deploys on push to `main`, and a fully-broken render passes every automated gate (CLAUDE.md). So verify in a running, authenticated app **before pushing**. Discover needs a signed-in user with **saved Places that have coordinates**.

- [ ] **Step 1: Launch the app**

Run: `npm run dev` (from `frontend/`) and open the served URL; sign in. (Or use the project `run` skill.)

- [ ] **Step 2: Verify the Discover strip**

Go to Discover ("ไปไหนดี") → select a saved Place (tap a pin or a list row). Confirm:
- the "อากาศรายชั่วโมง" section appears **between the badges and รีวิว**, without an extra tap;
- the strip **scrolls horizontally**; the **first cell reads "ตอนนี้"**;
- each cell shows time · Google weather icon · temp · "รู้สึก N°" · rain% (0% → faint "แห้ง");
- **daytime cells** are warm-tinted, **night cells** cool-tinted;
- a **"พรุ่งนี้"** (or dated) divider appears where the calendar date rolls over;
- while loading, a **skeleton** shows and the rest of the sheet (reviews/note/actions) is not blocked;
- the top-right "ตอนนี้ N° · รู้สึก N°" summary matches the first cell.

- [ ] **Step 3: Verify #46 is unchanged**

Open a Trip → a Stop's detail sheet → "ดูอุณหภูมิรายชั่วโมง". Confirm the trips hourly planner still works: cells tappable, coolest-daytime/nighttime quick actions, apply-preview / "ปรับเลย", day/night tint, "พรุ่งนี้" divider.

- [ ] **Step 4: No map regression**

Confirm the Discover map is not covered by any overlay / black screen when the sheet opens (CLAUDE.md #36).

- [ ] **Step 5: Push**

Only after Steps 2-4 pass. Parallel sessions renumber ADRs (this feature already hit one): fetch + rebase first, re-check ADRs 122-125 for collision, then push.

```bash
git fetch main
git rebase main/main
git push main HEAD:main
```

---

## Self-Review

**Spec coverage:**
- Auto-visible, no toggle (ADR-123) → Task 2 (rendered unconditionally in PlaceSheet; RTK Query fires on mount).
- Display-only, no retiming (ADR-124) → Task 2 (inert `<div>` cells, no `onPick`).
- Full cell temp+feels+rain (ADR-125) → Task 2 Step 1.
- 48h window + `withinHorizon` guard → Task 2 Step 1 (`WINDOW_HOURS = 48`, filter).
- Loading skeleton + empty "ไม่มีข้อมูล" (ADR-030/031) → Task 2 Step 1.
- Day/night tint + "พรุ่งนี้" rollover → Task 1 (helper) + Task 2 (cells/CSS).
- Reuse #46 infra, no new backend/SKU → no backend task exists; query/endpoint/DTO reused as-is.
- Shared pure logic, cell page-local (ADR-124) → Task 1 (helper + adopt) + Task 2 (local cell).
- Interactive verification mandatory (CLAUDE.md) → Task 3.

**Placeholder scan:** none — every code/CSS/command step is concrete.

**Type consistency:** `hourlyRolloverLabel(dateStr, anchorDateStr)` defined in Task 1, called identically in Task 1 (HourlyPlanner) and Task 2 (DiscoverHourly). `HourlyReadingDto` fields used (`displayLocal, isDaytime, tempC, feelsLikeC, conditionType, iconBaseUri, rainPct`) match `shared/api/api.ts:558-563`. `iconUrl(iconBaseUri, isDark)` and `withinHorizon(targetMs, nowMs)` signatures match source.