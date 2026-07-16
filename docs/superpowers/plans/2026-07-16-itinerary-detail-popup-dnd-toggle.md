# Itinerary Compact Card + Detail Popup + DnD Toggle — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Slim the itinerary Stop card down to `arrival-rail | name + one-line summary | chevron`, move all detail into a tap-to-open bottom sheet, and gate drag-and-drop behind an explicit "จัดลำดับ / เสร็จ" reorder-mode toggle (issue [#34](https://github.com/ThodsaphonSonthiphin/MenuNest/issues/34)).

**Architecture:** Frontend-only. A new pure `lib/stopSummary.ts` builds the card's forecast-forward summary line (the only unit-testable piece — the SPA has no component test harness). `ItineraryStopCard` becomes compact and takes a `reorderMode` prop that swaps its trailing chevron for the existing drag handle. A new `StopDetailSheet` (a body-portaled Syncfusion `Dialog`, same mechanism as `StopEditorDialog`) holds the moved detail — times, dwell, forecast-forward weather panels, timing flag, and actions (นำทาง / รีวิว / แก้ไข / มาแล้ว). `ItineraryTab` owns `reorderMode` + `detailStopId` local state, a toolbar toggle, and a hint banner. The dnd-kit `DndContext` / sensors / `SortableContext` are left exactly as they are (ADR-043) — only the visible activator (the handle) is gated.

**Tech Stack:** React 18 + TypeScript, Redux Toolkit (RTK Query), @dnd-kit, Syncfusion React `Dialog` (`@syncfusion/react-popups`), vitest (node env), CSS in `trips-tokens.css` / `TripDetailPage.css`.

**Design source:** `docs/mocks/issue-34-itinerary-detail-popup-and-dnd-toggle.md` (confirmed).

## Global Constraints

- **Frontend-only.** No backend / API / MCP / EF changes. Reorder persistence (`useReorderStopsMutation`) and the visited write path (`useSetStopVisitedMutation`) are unchanged.
- **Every commit must leave the FULL suite green.** `frontend/.husky/pre-commit` runs backend `dotnet build` + `dotnet test` (Release) AND frontend `tsc --noEmit` + `npm run build` on every commit. Never `--no-verify`. Because a new required prop on a shared component breaks its caller's compile, the component contract change and its caller update must land in the **same** commit (Task 3).
- **Stage narrowly.** Always `git add <explicit paths>`; never `git add -A` / `git add .`. Never stage `daily-state.md` or `AGENTS.md`.
- **No component/visual test harness.** `frontend/vite.config.ts` runs vitest in `environment: 'node'` — no jsdom/RTL. Only pure `lib/*.test.ts` are unit-testable. Card layout, the sheet, the toggle, and all CSS are verified by the build gate **plus interactive verification** (Task 4). Do not write React-rendering tests — they cannot run here.
- **No emoji for new UI chrome** — use SVG icon components. Weather uses the existing Google condition SVG (`iconBaseUri`), not emoji. The pre-existing `catEmoji(place.category)` prefix on the stop name is out of scope for #34 — leave it as-is.
- **Commit messages reference the ticket:** this is partial/multi-commit work → use `(#34)` in the subject (no auto-close). Keep conventional-commit style `type(scope): summary`.
- **Palette (existing tokens):** teal `--teal` `#0e8f9e` / `--teal-deep` `#0b7a87` / `--teal-soft` `#e3f5f6`; ink `--ink` `#0f172a`; now `--now`/`--now-bg`, arr `--arr`/`--arr-bg`, arr-rain `--arr-rain`/`--arr-rain-bg`; warn `--warn-ink`/`--warn-bg`, bad `--bad`/`--bad-bg`; visited `--visited`/`--visited-bg`; muted `--muted`. The sheet is body-portaled and cannot see `--trp-*`/`--teal` tokens — define any colors it needs **locally** on `.stop-detail-sheet` (mirror the existing `.stop-editor-dialog` block in `TripDetailPage.css`).

## Implementer decisions (locked from the design doc's "implementer's call" points)

1. **New `StopDetailSheet.tsx`**, not a read-mode branch of `StopEditorDialog` — the layouts differ too much; reuse is at the Syncfusion `Dialog` mechanism level (body portal + `.sf-dlg-*` restyle), not the form.
2. **`reorderMode` + `detailStopId` are local `useState` in `ItineraryTab`** (session-scoped, not persisted, not in Redux). Simplest correct approach.
3. **Remove the card's leading visited checkbox**; fold "ทำเครื่องหมายว่ามาแล้ว" into the sheet as an action (mockup choice — maximises card compactness). Only non-visited stops render as cards anyway (`remaining` list), so the inline checkbox is the only affected affordance; the "มาแล้ว" done-drawer + unvisit path are untouched.
4. **`WeatherChip` is repurposed forecast-forward** (condition label bold + rain% primary, temp small/muted) and reused inside the sheet's two weather panels. The card no longer renders weather chips — it uses the pure summary instead.
5. **Extract `FlagNote` to `components/FlagNote.tsx`** so the sheet reuses it; the card drops its private copy in Task 3.
6. **Add `ChevronRightIcon`** to `TripFormIcons.tsx` for the card's trailing chevron.

## File Structure

**Create**
- `frontend/src/pages/trips/lib/stopSummary.ts` — pure builder for the card summary line (weather-forecast-forward label, dwell text, flag dot+label).
- `frontend/src/pages/trips/lib/stopSummary.test.ts` — vitest coverage for the above.
- `frontend/src/pages/trips/components/FlagNote.tsx` — the timing-flag reason banner, extracted from the card so the sheet can reuse it.
- `frontend/src/pages/trips/components/StopDetailSheet.tsx` — the bottom-sheet detail popup.

**Modify**
- `frontend/src/pages/trips/components/TripFormIcons.tsx` — add `ChevronRightIcon`.
- `frontend/src/pages/trips/components/WeatherChip.tsx` — forecast-forward rendering.
- `frontend/src/pages/trips/components/ItineraryStopCard.tsx` — compact rewrite: `reorderMode` + `onOpenDetail` props, chevron/handle swap, summary line, drop the moved-out pieces.
- `frontend/src/pages/trips/components/ItineraryTab.tsx` — `reorderMode`/`detailStopId` state, toolbar toggle + hint banner, mount `StopDetailSheet`, new card props.
- `frontend/src/pages/trips/trips-tokens.css` — compact card variant, summary line, chevron, reorder toolbar/pill/hint.
- `frontend/src/pages/trips/TripDetailPage.css` — `.stop-detail-sheet` bottom-sheet styling + forecast-forward weather panels (portal-scoped, mirror `.stop-editor-dialog`).

---

### Task 1: Pure card-summary builder (`lib/stopSummary.ts`)

The one piece with real logic, so it gets real vitest coverage. Everything else is component/CSS (Tasks 2–4, verified interactively in Task 4).

**Files:**
- Create: `frontend/src/pages/trips/lib/stopSummary.ts`
- Test: `frontend/src/pages/trips/lib/stopSummary.test.ts`

**Interfaces:**
- Consumes: `WeatherReadingDto` (`{stopId, hasData, conditionType, iconBaseUri, tempC, rainPct, description}`) from `../../../shared/api/api`; `StopFlag`/`FlagSeverity` from `../hooks/useSchedule`; `flagText` from `../timingFlag`; `isRainy` from `./weather`; `formatDurationMinutes` from `../utils/time`.
- Produces:
  ```ts
  export interface StopSummary {
    weather: {iconBaseUri: string | null; label: string} | null
    dwellText: string
    flag: {severity: FlagSeverity; label: string} | null
  }
  export function buildStopSummary(args: {
    arrivalReading?: WeatherReadingDto
    dwellMinutes: number
    flag: StopFlag
  }): StopSummary
  ```
  Task 2's `ItineraryStopCard` renders this.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/trips/lib/stopSummary.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {buildStopSummary} from './stopSummary'
import type {WeatherReadingDto} from '../../../shared/api/api'
import type {TimingFlag} from '../hooks/useSchedule'

const reading = (over: Partial<WeatherReadingDto>): WeatherReadingDto => ({
  stopId: 's1',
  hasData: true,
  conditionType: null,
  iconBaseUri: 'https://maps.gstatic.com/weather/icon',
  tempC: 30,
  rainPct: 10,
  description: 'มีเมฆบางส่วน',
  ...over,
})

describe('buildStopSummary', () => {
  it('leads with the arrival-forecast description and passes the icon through', () => {
    const s = buildStopSummary({arrivalReading: reading({}), dwellMinutes: 90, flag: null})
    expect(s.weather).toEqual({iconBaseUri: 'https://maps.gstatic.com/weather/icon', label: 'มีเมฆบางส่วน'})
  })

  it('appends rain% when rain is at/above the rainy threshold', () => {
    const s = buildStopSummary({arrivalReading: reading({description: 'ฝนตก', rainPct: 60}), dwellMinutes: 60, flag: null})
    expect(s.weather?.label).toBe('ฝนตก 60%')
  })

  it('omits rain% below the rainy threshold', () => {
    const s = buildStopSummary({arrivalReading: reading({description: 'แดดจัด', rainPct: 20}), dwellMinutes: 60, flag: null})
    expect(s.weather?.label).toBe('แดดจัด')
  })

  it('returns null weather when the reading has no data', () => {
    const s = buildStopSummary({arrivalReading: reading({hasData: false}), dwellMinutes: 60, flag: null})
    expect(s.weather).toBeNull()
  })

  it('returns null weather when no reading is supplied', () => {
    const s = buildStopSummary({dwellMinutes: 60, flag: null})
    expect(s.weather).toBeNull()
  })

  it('formats the dwell text with the shared helper', () => {
    const s = buildStopSummary({dwellMinutes: 90, flag: null})
    expect(s.dwellText).toBe('อยู่ 1 ชม. 30 น.')
  })

  it('surfaces a timing flag as its severity + reason line', () => {
    const flag: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'on-break', reopenAt: '13:00'}
    const s = buildStopSummary({dwellMinutes: 60, flag})
    expect(s.flag).toEqual({severity: 'problem', label: 'ปิดพักช่วงนี้ · เปิดอีกที 13:00'})
  })

  it('has null flag when there is no flag', () => {
    const s = buildStopSummary({dwellMinutes: 60, flag: null})
    expect(s.flag).toBeNull()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/lib/stopSummary.test.ts`
Expected: FAIL — `Failed to resolve import "./stopSummary"` / `buildStopSummary is not a function`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/pages/trips/lib/stopSummary.ts`:

```ts
// frontend/src/pages/trips/lib/stopSummary.ts
// Pure builder for the compact itinerary card's one-line summary (issue #34).
// Weather is forecast-forward: the condition description leads and rain% is appended
// only when significant; temperature is intentionally omitted from the card (it lives,
// muted, in the detail sheet). Extracted to lib/ so it gets real vitest coverage — the
// SPA has no component test harness (see CLAUDE.md).
import type {WeatherReadingDto} from '../../../shared/api/api'
import type {FlagSeverity, StopFlag} from '../hooks/useSchedule'
import {flagText} from '../timingFlag'
import {isRainy} from './weather'
import {formatDurationMinutes} from '../utils/time'

export interface StopSummary {
  /** Arrival-forecast weather, or null when there is no usable reading. */
  weather: {iconBaseUri: string | null; label: string} | null
  /** e.g. "อยู่ 1 ชม. 30 น." */
  dwellText: string
  /** Timing-flag severity (drives the dot colour) + short reason label, or null. */
  flag: {severity: FlagSeverity; label: string} | null
}

export function buildStopSummary({
  arrivalReading,
  dwellMinutes,
  flag,
}: {
  arrivalReading?: WeatherReadingDto
  dwellMinutes: number
  flag: StopFlag
}): StopSummary {
  let weather: StopSummary['weather'] = null
  if (arrivalReading?.hasData && (arrivalReading.description || arrivalReading.iconBaseUri)) {
    const desc = arrivalReading.description ?? ''
    const rainy = isRainy(arrivalReading.rainPct) && arrivalReading.rainPct != null
    const label = rainy ? (desc ? `${desc} ${arrivalReading.rainPct}%` : `ฝน ${arrivalReading.rainPct}%`) : desc
    weather = {iconBaseUri: arrivalReading.iconBaseUri ?? null, label}
  }
  return {
    weather,
    dwellText: `อยู่ ${formatDurationMinutes(dwellMinutes)}`,
    flag: flag ? {severity: flag.severity, label: flagText(flag).reasonLine} : null,
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run src/pages/trips/lib/stopSummary.test.ts`
Expected: PASS — 8 passing.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/stopSummary.ts frontend/src/pages/trips/lib/stopSummary.test.ts
git commit -m "feat(trips): pure stop-summary builder for compact itinerary card (#34)"
```
(The pre-commit hook runs the full backend+frontend suite; expect ~40s.)

---

### Task 2: Detail sheet + forecast-forward weather + FlagNote extract

Builds the new sheet and its dependencies. Nothing here is mounted yet, so the app is unchanged at runtime; `WeatherChip`'s new look shows transiently on the *old* card until Task 3 removes it. All green on `tsc`/`build`.

**Files:**
- Modify: `frontend/src/pages/trips/components/TripFormIcons.tsx` (add `ChevronRightIcon`)
- Modify: `frontend/src/pages/trips/components/WeatherChip.tsx`
- Create: `frontend/src/pages/trips/components/FlagNote.tsx`
- Create: `frontend/src/pages/trips/components/StopDetailSheet.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.css`

**Interfaces:**
- Consumes: `catColor`/`catLabel` from `../placeCategory`; `formatDurationMinutes` from `../utils/time`; `reviewLabel`/`reviewHost` from `../lib/reviewLinks`; `flagText` + reason→icon mapping (recreated in `FlagNote.tsx`); `WeatherChip`; `NavIcon`; `ReviewIcon`; `CheckIcon`.
- Produces: `FlagNote({flag})`, `ChevronRightIcon({className})`, `StopDetailSheet(props)` (prop shape below). Task 3's `ItineraryTab` mounts `StopDetailSheet` and renders the compact card with `ChevronRightIcon`.

- [ ] **Step 1: Add `ChevronRightIcon` to `TripFormIcons.tsx`**

Append a new export alongside the existing chevrons (match the existing `IconProps` pattern used by `ChevronUpIcon`/`ChevronDownIcon`):

```tsx
export function ChevronRightIcon({className}: IconProps) {
  return (
    <svg className={className} width="16" height="16" viewBox="0 0 24 24" fill="none"
      stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M9 18l6-6-6-6" />
    </svg>
  )
}
```

- [ ] **Step 2: Rewrite `WeatherChip.tsx` forecast-forward**

Replace the `data`-state return (the block from `const r = reading!` to the end of the function) so the **condition label leads and is bold, rain% is primary, and temperature is last and muted**. Keep the props, the `kind` label, and the `loading`/`nodata` branches exactly as they are:

```tsx
  const r = reading! // state === 'data' ⇒ reading is present and hasData
  const rainy = kind === 'arr' && isRainy(r.rainPct)
  return (
    <span className={`chip wx ${kind}${rainy ? ' rainy' : ''}`}>
      <span className="lab">{LABEL[kind]}</span>
      {r.iconBaseUri && <img src={iconUrl(r.iconBaseUri, isDark)} alt={r.description ?? ''} width={22} height={22} />}
      {r.description && <span className="cond">{r.description}</span>}
      {r.rainPct != null && (
        <span className="r"><RainDropIcon />{r.rainPct}%</span>
      )}
      {r.tempC != null && <span className="t">{Math.round(r.tempC)}°</span>}
    </span>
  )
```

- [ ] **Step 3: Create `FlagNote.tsx`**

Create `frontend/src/pages/trips/components/FlagNote.tsx`:

```tsx
// frontend/src/pages/trips/components/FlagNote.tsx
// The timing-flag reason banner, shared by the itinerary detail sheet (issue #34).
// Extracted from ItineraryStopCard; identical rendering.
import type {FlagReason, TimingFlag} from '../hooks/useSchedule'
import {flagText} from '../timingFlag'
import {ClockIcon, LockIcon, MoonIcon} from './FlagIcons'

// Reason → icon component. `typeof LockIcon` avoids naming the JSX namespace.
const REASON_ICON: Record<FlagReason, typeof LockIcon> = {
  overflow: MoonIcon,
  closed: LockIcon,
  'off-window': ClockIcon,
}

export function FlagNote({flag}: {flag: TimingFlag}) {
  const Icon = REASON_ICON[flag.reason]
  const {reasonLine, fixLine} = flagText(flag)
  return (
    <div className={`flag-note${flag.severity === 'problem' ? ' bad' : ''}`}>
      <Icon />
      <span><b>{reasonLine}</b> <span className="fix">{fixLine}</span></span>
    </div>
  )
}
```

- [ ] **Step 4: Create `StopDetailSheet.tsx`**

Create `frontend/src/pages/trips/components/StopDetailSheet.tsx`:

```tsx
// frontend/src/pages/trips/components/StopDetailSheet.tsx
// Tap-a-card detail popup for the itinerary (issue #34). A body-portaled Syncfusion
// Dialog styled as a bottom sheet (same mechanism as StopEditorDialog). Holds the
// detail moved off the now-compact card: times, dwell, forecast-forward weather,
// timing flag, and the นำทาง / รีวิว / แก้ไข / มาแล้ว actions.
import {Dialog} from '@syncfusion/react-popups'
import type {TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {StopFlag} from '../hooks/useSchedule'
import {catColor, catLabel} from '../placeCategory'
import {formatDurationMinutes} from '../utils/time'
import {reviewHost, reviewLabel} from '../lib/reviewLinks'
import {WeatherChip} from './WeatherChip'
import {FlagNote} from './FlagNote'
import {NavIcon} from './NavIcon'
import {ReviewIcon} from './ReviewIcon'
import {CheckIcon} from './FlagIcons'

export function StopDetailSheet({
  place,
  arrival,
  depart,
  dwell,
  flag,
  dayNumber,
  ordinal,
  navUrl,
  nowReading,
  arrivalReading,
  weatherLoading = false,
  onEdit,
  onNavigate,
  onToggleVisited,
  onClose,
}: {
  place: TripPlaceDto
  arrival: string
  depart: string
  dwell: number
  flag: StopFlag
  dayNumber: number
  ordinal: number
  navUrl: string | null
  nowReading?: WeatherReadingDto
  arrivalReading?: WeatherReadingDto
  weatherLoading?: boolean
  onEdit: () => void
  onNavigate?: () => void
  onToggleVisited: (next: boolean) => void
  onClose: () => void
}) {
  const links = place.reviewLinks ?? []

  const header = (
    <div className="sd-head">
      <div className="sd-title">{place.name}</div>
      <div className="sd-meta">
        <span className="sd-cat">
          <span className="sd-cat-dot" style={{background: catColor(place.category)}} />
          {catLabel(place.category)}
        </span>
        <span className="sd-crumb">วัน {dayNumber} · จุดที่ {ordinal}</span>
      </div>
    </div>
  )

  return (
    <Dialog
      open
      onClose={onClose}
      modal
      className="stop-detail-sheet"
      header={header}
      position="CenterBottom"
      style={{width: 'min(480px, 100vw)'}}
    >
      <div className="stop-detail" data-testid="stop-detail-sheet">
        <div className="sd-times">
          <div className="sd-time-col">
            <div className="sd-time-lab">ถึง</div>
            <div className="sd-time-val">{arrival}</div>
          </div>
          <div className="sd-time-arrow">→</div>
          <div className="sd-time-col">
            <div className="sd-time-lab">ออก</div>
            <div className="sd-time-val">{depart}</div>
          </div>
          <div className="sd-dwell"><span className="sd-dwell-lab">อยู่</span> {formatDurationMinutes(dwell)}</div>
        </div>

        <div className="sd-weather">
          <WeatherChip kind="now" reading={nowReading} isLoading={weatherLoading} />
          <WeatherChip kind="arr" reading={arrivalReading} isLoading={weatherLoading} />
        </div>

        {flag && <FlagNote flag={flag} />}

        {links.length > 0 && (
          <div className="sd-reviews">
            <div className="sd-sec-lab">รีวิว</div>
            {links.map((l, i) => (
              <a key={l.url + i} className="sd-review" href={l.url} target="_blank" rel="noopener noreferrer">
                <ReviewIcon />
                <span className="sd-review-label">{reviewLabel(l, i)}</span>
                <span className="sd-review-host">{reviewHost(l.url)}</span>
              </a>
            ))}
          </div>
        )}

        <div className="sd-actions">
          {navUrl ? (
            <a
              className="sd-act primary"
              href={navUrl}
              target="_blank"
              rel="noopener noreferrer"
              onClick={() => onNavigate?.()}
            >
              <NavIcon /> นำทาง
            </a>
          ) : (
            <span className="sd-act primary disabled" role="img" aria-disabled="true" aria-label="ไม่มีพิกัดสำหรับนำทาง">
              <NavIcon /> นำทาง
            </span>
          )}
          <button type="button" className="sd-act" onClick={onEdit}>แก้ไข</button>
          <button type="button" className="sd-act visited" onClick={() => onToggleVisited(true)}>
            <CheckIcon /> มาแล้ว
          </button>
        </div>
      </div>
    </Dialog>
  )
}
```

> Note (Syncfusion): `position={{X:'center', Y:'bottom'}}` pins the dialog to the bottom. If the installed `@syncfusion/react-popups` version ignores `position`, rely on the `.sf-dlg-container:has(...)` CSS override in Step 5. Final placement confirmed interactively in Task 4.

- [ ] **Step 5: Add sheet + weather-panel CSS to `TripDetailPage.css`**

Append after the existing `.stop-editor-dialog` block. Colors are defined **locally** (portal is outside `.trip-detail`), mirroring `.stop-editor-dialog`:

```css
/* ============================================================
   Stop detail sheet (issue #34) — body-portaled bottom sheet.
   Portal sits OUTSIDE .trip-detail, so colors are local (like .stop-editor-dialog).
   ============================================================ */
.stop-detail-sheet {
  --sd-ink: #0f172a;
  --sd-muted: #64748b;
  --sd-teal: #0e8f9e;
  --sd-teal-deep: #0b7a87;
  --sd-teal-soft: #e3f5f6;
  --sd-border: #e2e8f0;
  border-radius: 20px 20px 0 0;
  overflow: hidden;
  box-shadow: 0 -12px 40px rgba(2, 32, 27, 0.22);
  color: var(--sd-ink);
}
.stop-detail-sheet .sf-dlg-header-content { padding: 18px 20px 0; border-bottom: 0; }
.stop-detail-sheet .sf-dlg-header { width: 100%; }
.stop-detail-sheet .sd-head { padding-right: 30px; }
.stop-detail-sheet .sd-title { font-size: 19px; font-weight: 700; line-height: 1.25; color: var(--sd-ink); }
.stop-detail-sheet .sd-meta { display: flex; align-items: center; gap: 10px; margin-top: 8px; }
.stop-detail-sheet .sd-cat {
  display: inline-flex; align-items: center; gap: 6px; padding: 4px 11px 4px 9px;
  background: #f1f5f9; border-radius: 999px; font-size: 12px; font-weight: 600; color: #475569;
}
.stop-detail-sheet .sd-cat-dot { width: 9px; height: 9px; border-radius: 50%; }
.stop-detail-sheet .sd-crumb { font-size: 12.5px; font-weight: 500; color: var(--sd-muted); }
.stop-detail-sheet .sf-dlg-content { padding: 0 20px 20px; }

.stop-detail-sheet .stop-detail { display: flex; flex-direction: column; gap: 16px; margin-top: 14px; }

.stop-detail-sheet .sd-times {
  display: flex; align-items: center; gap: 12px;
  padding: 12px 14px; background: #f8fafc; border: 1px solid var(--sd-border); border-radius: 12px;
}
.stop-detail-sheet .sd-time-col { text-align: center; }
.stop-detail-sheet .sd-time-lab { font-size: 11px; font-weight: 600; color: var(--sd-muted); }
.stop-detail-sheet .sd-time-val { font-family: 'Spline Sans Mono', ui-monospace, monospace; font-size: 20px; font-weight: 700; color: var(--sd-ink); }
.stop-detail-sheet .sd-time-arrow { color: var(--sd-muted); font-size: 16px; }
.stop-detail-sheet .sd-dwell { margin-left: auto; font-size: 13px; font-weight: 700; color: var(--sd-teal-deep); }
.stop-detail-sheet .sd-dwell-lab { font-weight: 600; color: var(--sd-muted); }

.stop-detail-sheet .sd-weather { display: flex; gap: 10px; }
.stop-detail-sheet .sd-weather .chip.wx {
  flex: 1; flex-direction: column; align-items: flex-start; gap: 4px;
  padding: 10px 12px; border-radius: 12px; font-size: 13px;
}
.stop-detail-sheet .sd-weather .chip.wx .lab { font-size: 10px; }
.stop-detail-sheet .sd-weather .chip.wx img { width: 30px; height: 30px; }
.stop-detail-sheet .sd-weather .chip.wx .cond { font-weight: 700; font-size: 14px; }
.stop-detail-sheet .sd-weather .chip.wx .r { font-weight: 700; }
.stop-detail-sheet .sd-weather .chip.wx .t { font-weight: 500; opacity: 0.6; font-size: 12px; }

.stop-detail-sheet .sd-sec-lab { font-size: 12px; font-weight: 700; color: var(--sd-muted); margin-bottom: 6px; }
.stop-detail-sheet .sd-review {
  display: flex; align-items: center; gap: 8px; padding: 9px 11px; margin-bottom: 6px;
  background: #fdeaf1; color: #c2255c; border-radius: 10px; text-decoration: none; font-size: 13px; font-weight: 600;
}
.stop-detail-sheet .sd-review svg { width: 15px; height: 15px; flex: none; }
.stop-detail-sheet .sd-review-label { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.stop-detail-sheet .sd-review-host { font-size: 11px; font-weight: 500; opacity: 0.7; }

.stop-detail-sheet .sd-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.stop-detail-sheet .sd-act {
  display: inline-flex; align-items: center; justify-content: center; gap: 6px;
  padding: 10px 16px; border-radius: 10px; border: 1px solid var(--sd-border);
  background: #fff; color: var(--sd-ink); font-size: 13px; font-weight: 700; cursor: pointer;
  font-family: inherit; text-decoration: none;
}
.stop-detail-sheet .sd-act svg { width: 16px; height: 16px; }
.stop-detail-sheet .sd-act.primary { flex: 1; background: var(--sd-teal); border-color: var(--sd-teal); color: #fff; }
.stop-detail-sheet .sd-act.primary.disabled { background: #cbd5e1; border-color: #cbd5e1; cursor: default; pointer-events: none; }
.stop-detail-sheet .sd-act.visited { color: #15803d; border-color: #cfe9d7; background: #e7f6ec; }

.sf-dlg-container:has(> .stop-detail-sheet) { align-items: flex-end; }
```

- [ ] **Step 6: Verify compile**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: both succeed. `StopDetailSheet` is not mounted yet, so no runtime change beyond the transient forecast-forward look on the current card's chips.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/trips/components/TripFormIcons.tsx frontend/src/pages/trips/components/WeatherChip.tsx frontend/src/pages/trips/components/FlagNote.tsx frontend/src/pages/trips/components/StopDetailSheet.tsx frontend/src/pages/trips/TripDetailPage.css
git commit -m "feat(trips): StopDetailSheet + forecast-forward weather + FlagNote extract (#34)"
```

---

### Task 3: Compact card + reorder toggle wiring (atomic card↔tab change)

The card's contract changes and its only caller (`ItineraryTab`) must move together — a split would fail the full-suite pre-commit gate. One commit.

**Files:**
- Modify: `frontend/src/pages/trips/components/ItineraryStopCard.tsx` (full rewrite — small)
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: `buildStopSummary`/`StopSummary` (Task 1); `iconUrl` from `../lib/weather`; `GripIcon`, `ChevronRightIcon` (Task 2); `catEmoji`; `StopDetailSheet` (Task 2); existing `buildStopNavUrl`; `stopWeather[id]` = `{now?, arrival?, nowLoading?, arrivalLoading?}`.
- Produces (new card contract): `ItineraryStopCard({id, place, arrival, dwell, flag, arrivalReading?, reorderMode?, onOpenDetail?})`. Removed: `depart`, `onEdit`, `navUrl`, `onNavigate`, `nowReading`, `weatherLoading`, `isVisited`, `onToggleVisited`.

- [ ] **Step 1: Rewrite `ItineraryStopCard.tsx`**

Replace the whole file with:

```tsx
// frontend/src/pages/trips/components/ItineraryStopCard.tsx
import {useSortable} from '@dnd-kit/sortable'
import {CSS} from '@dnd-kit/utilities'
import type {TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {StopFlag} from '../hooks/useSchedule'
import {catEmoji} from '../placeCategory'
import {buildStopSummary, type StopSummary} from '../lib/stopSummary'
import {iconUrl} from '../lib/weather'
import {GripIcon, ChevronRightIcon} from './TripFormIcons'

function StopSummaryLine({summary}: {summary: StopSummary}) {
  return (
    <div className="stop-summary">
      {summary.weather && (
        <span className="sum-wx">
          {summary.weather.iconBaseUri && (
            <img src={iconUrl(summary.weather.iconBaseUri, false)} alt="" width={15} height={15} />
          )}
          {summary.weather.label}
        </span>
      )}
      <span className="sum-dwell">{summary.dwellText}</span>
      {summary.flag && (
        <span className={`sum-flag ${summary.flag.severity === 'problem' ? 'bad' : 'warn'}`}>
          <span className="sum-flag-dot" />
          {summary.flag.label}
        </span>
      )}
    </div>
  )
}

export function ItineraryStopCard({
  id,
  place,
  arrival,
  dwell,
  flag,
  arrivalReading,
  reorderMode = false,
  onOpenDetail,
}: {
  id: string
  place: TripPlaceDto
  arrival: string
  dwell: number
  flag: StopFlag
  arrivalReading?: WeatherReadingDto
  reorderMode?: boolean
  onOpenDetail?: () => void
}) {
  const {attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging} =
    useSortable({id})
  const style = {transform: CSS.Transform.toString(transform), transition}

  const summary = buildStopSummary({arrivalReading, dwellMinutes: dwell, flag})
  const cardFlag = flag ? (flag.severity === 'problem' ? ' bad' : ' warn') : ''

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`stop-card compact${cardFlag}${isDragging ? ' dragging' : ''}`}
      data-testid="itin-stop-card"
      data-stop-id={id}
    >
      <div className="stop-rail">
        <div className="stop-arr">{arrival}</div>
      </div>

      {reorderMode ? (
        <div className="stop-body static">
          <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
          <StopSummaryLine summary={summary} />
        </div>
      ) : (
        <button className="stop-body" onClick={onOpenDetail} aria-label={`ดูรายละเอียด: ${place.name}`}>
          <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
          <StopSummaryLine summary={summary} />
        </button>
      )}

      {reorderMode ? (
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
      ) : (
        <span className="stop-chevron" aria-hidden="true">
          <ChevronRightIcon />
        </span>
      )}
    </div>
  )
}
```

- [ ] **Step 2: `ItineraryTab.tsx` — add the import** (next to `StopEditorDialog` import, ~line 35):

```tsx
import {StopDetailSheet} from './StopDetailSheet'
```

- [ ] **Step 3: `ItineraryTab.tsx` — reorder + detail state** (after `const [doneOpen, setDoneOpen] = useState(false)`, ~line 127):

```tsx
  const [reorderMode, setReorderMode] = useState(false)
  const [detailStopId, setDetailStopId] = useState<string | null>(null)
  const toggleReorder = () => {
    setReorderMode((v) => !v)
    setDetailStopId(null) // entering/leaving reorder closes any open detail (design §2)
  }
```

- [ ] **Step 4: `ItineraryTab.tsx` — derive detail stop** (after `const leadLeg = ...`, ~line 217, before `return (`):

```tsx
  const detailStop = detailStopId ? scheduled.find((x) => x.stop.id === detailStopId) ?? null : null
  const detailPlace = detailStop ? placesById[detailStop.stop.tripPlaceId] : undefined
```

- [ ] **Step 5: `ItineraryTab.tsx` — toolbar toggle + hint** (immediately before `<DndContext`, after the `{actionError && ...}` line, ~328):

```tsx
      {remaining.length > 0 && (
        <div className="stop-toolbar">
          <span className="stop-count">จุดแวะ · {remaining.length} จุด</span>
          {remaining.length >= 2 && (
            <button
              type="button"
              className={`reorder-toggle${reorderMode ? ' on' : ''}`}
              aria-pressed={reorderMode}
              onClick={toggleReorder}
            >
              {reorderMode ? 'เสร็จ' : 'จัดลำดับ'}
            </button>
          )}
        </div>
      )}
      {reorderMode && (
        <p className="reorder-hint">โหมดจัดลำดับ — ลากที่จับด้านขวาเพื่อย้ายจุดแวะ</p>
      )}
```

- [ ] **Step 6: `ItineraryTab.tsx` — new compact card props** (replace the whole `<ItineraryStopCard ... />`, lines ~362-388):

```tsx
                    <ItineraryStopCard
                      id={s.stop.id}
                      place={place}
                      arrival={s.arrival}
                      dwell={s.stop.dwellMinutes}
                      flag={s.flag}
                      arrivalReading={stopWeather[s.stop.id]?.arrival}
                      reorderMode={reorderMode}
                      onOpenDetail={() => setDetailStopId(s.stop.id)}
                    />
```

- [ ] **Step 7: `ItineraryTab.tsx` — mount the detail sheet** (immediately before `{editorStopId && (`, ~line 453):

```tsx
      {detailStop && detailPlace && (
        <StopDetailSheet
          place={detailPlace}
          arrival={detailStop.arrival}
          depart={detailStop.depart}
          dwell={detailStop.stop.dwellMinutes}
          flag={detailStop.flag}
          dayNumber={dayList.findIndex((d) => d.id === resolvedDayId) + 1}
          ordinal={scheduled.indexOf(detailStop) + 1}
          navUrl={buildStopNavUrl(detailPlace, detailStop.stop.travelModeToReach)}
          nowReading={stopWeather[detailStop.stop.id]?.now}
          arrivalReading={stopWeather[detailStop.stop.id]?.arrival}
          weatherLoading={(stopWeather[detailStop.stop.id]?.nowLoading ?? false) || (stopWeather[detailStop.stop.id]?.arrivalLoading ?? false)}
          onEdit={() => {
            setDetailStopId(null)
            dispatch(setStopEditor(detailStop.stop.id))
          }}
          onNavigate={() =>
            appInsights.trackEvent(
              {name: 'TripNavHandoff'},
              {scope: 'stop', travelMode: detailStop.stop.travelModeToReach, hasPlaceId: !!detailPlace.googlePlaceId},
            )
          }
          onToggleVisited={async (next) => {
            try {
              await setStopVisited({tripId, stopId: detailStop.stop.id, isVisited: next}).unwrap()
              setDetailStopId(null)
            } catch (err) {
              setActionError(getErrorMessage(err))
            }
          }}
          onClose={() => setDetailStopId(null)}
        />
      )}
```

- [ ] **Step 8: Add card/toolbar CSS to `trips-tokens.css`** (end of the stop-card section, ~line 300):

```css
/* ── Issue #34: compact card + reorder toggle ── */
.stop-card.compact .stop-rail { width: 52px; }
.stop-card.compact .stop-arr { font-size: 14px; }
.stop-card.compact .stop-body { padding: 10px 12px; }
.stop-body.static { cursor: default; }

.stop-summary {
  display: flex; flex-wrap: wrap; align-items: center; gap: 6px;
  margin-top: 3px; font-size: 11.5px; color: var(--muted); min-width: 0;
}
.stop-summary > span { display: inline-flex; align-items: center; gap: 3px; min-width: 0; }
.stop-summary > span + span::before { content: "·"; margin-right: 6px; color: #cbd5e1; }
.stop-summary .sum-wx { color: var(--arr); font-weight: 600; }
.stop-summary .sum-wx img { width: 15px; height: 15px; }
.stop-summary .sum-flag { font-weight: 700; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.stop-summary .sum-flag.warn { color: var(--warn-ink); }
.stop-summary .sum-flag.bad { color: var(--bad); }
.stop-summary .sum-flag-dot { width: 7px; height: 7px; border-radius: 50%; flex: none; }
.stop-summary .sum-flag.warn .sum-flag-dot { background: #d9a441; }
.stop-summary .sum-flag.bad .sum-flag-dot { background: var(--bad); }

.stop-chevron { flex: none; display: flex; align-items: center; justify-content: center; width: 36px; color: #cbd5e1; }
.stop-chevron svg { width: 18px; height: 18px; }

.stop-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.stop-count { font-size: 12px; font-weight: 700; color: var(--muted); }
.reorder-toggle {
  border: 1px solid var(--teal); background: transparent; color: var(--teal-deep);
  border-radius: 999px; padding: 5px 14px; font-size: 12px; font-weight: 700; cursor: pointer;
  font-family: inherit; transition: background 0.12s ease, color 0.12s ease;
}
.reorder-toggle:hover { background: var(--teal-soft); }
.reorder-toggle.on { background: var(--teal); color: #fff; border-color: var(--teal); }
.reorder-hint {
  margin: 0; padding: 7px 11px; background: var(--teal-soft); color: var(--teal-deep);
  border-radius: 9px; font-size: 11.5px; font-weight: 600;
}
```

- [ ] **Step 9: Verify compile + unit suite**

Run: `cd frontend && npx tsc --noEmit && npx vitest run && npm run build`
Expected: type-check clean (no unused imports left in `ItineraryStopCard.tsx`), vitest green, build succeeds.

- [ ] **Step 10: Commit**

```bash
git add frontend/src/pages/trips/components/ItineraryStopCard.tsx frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): compact stop card + tap-to-detail + reorder-mode toggle (#34)"
```

---

### Task 4: Verification + cleanup

- [ ] **Step 1: Automatable gates**
  - [ ] `cd frontend && npx tsc --noEmit && npx vitest run && npm run build` — all green.
  - [ ] `grep -rn "stop-drag-handle" frontend/e2e` — update any spec that asserts the handle without first entering reorder mode.
  - [ ] No dead code / unused exports (old card no longer imports moved-out helpers; `WeatherChip` used only by the sheet).

- [ ] **Step 2: Interactive checklist (seeded/authed env, Chrome DevTools ~390px)** — card compactness, summary (no temp), flag dot colour; sheet header/times/dwell/weather-forecast-forward/flag/reviews/actions (นำทาง/แก้ไข/มาแล้ว); reorder toggle off→chevron+detail, on→handle+hint+no-detail+drag reorders; toggling closes detail.

- [ ] **Step 3: Commit any fixes** — `git add <changed files>; git commit -m "fix(trips): <what verification surfaced> (#34)"`.

---

## Self-Review

- **Spec coverage:** §1 compact card + popup (Tasks 1/2/3); §2 reorder toggle (Task 3, dnd wiring untouched); §3 weather forecast-forward (Tasks 1/2). Checklist files covered; `tripsSlice.ts` optional → not used (local state). Component tests → Task 4 interactive (no component harness). Out of scope respected. ✅
- **Placeholders:** none. ✅
- **Type consistency:** `buildStopSummary`/`StopSummary`, card contract vs call site, `StopDetailSheet` props vs mount, `stopWeather[id]` accessors — all consistent. ✅
