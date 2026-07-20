# Weather-Alert Numeric Thresholds Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user type their own UV and feels-like weather-alert thresholds (with an on/off checkbox each) on `/settings`, replacing the fixed preset dropdowns, so a heat warning can fire at the value they actually find uncomfortable.

**Architecture:** Frontend-only. The backend already accepts any value (validator UV 0..15, feels 0..60) and stores the tri-state `null`/`0`/`N` (ADR-091), and the itinerary-card badge reads that same encoding — both stay untouched. New pure helpers translate stored ↔ UI control state; `SettingsPage` swaps two `DropDownList`s for a `Checkbox` + `NumericTextBox` per axis.

**Tech Stack:** React + TypeScript, RTK Query, Syncfusion `@syncfusion/react-inputs` (`NumericTextBox`) + `@syncfusion/react-buttons` (`Checkbox`) @ 33.1.44, Vitest (node env, no component harness).

**Design spec:** `docs/superpowers/specs/2026-07-20-weather-alert-numeric-thresholds-design.md` · **ADRs:** 105, 106

## Global Constraints

- **Frontend only** — no backend/DB/migration/DTO/command/validator change. The tri-state storage contract (`null`=default, `0`=off, `N`=warn at `>=N`, ADR-091) is unchanged.
- **Badge logic untouched** — `frontend/src/pages/trips/lib/weather.ts` (`weatherAlertBadges`, `effectiveThreshold`, `UV_WARN_DEFAULT`, `FEELS_WARN_DEFAULT`), `ItineraryStopCard.tsx`, `stopSummary.ts` must NOT change. Verify with a diff before committing.
- **Components:** `NumericTextBox` from `@syncfusion/react-inputs`, `Checkbox` from `@syncfusion/react-buttons`. There is **no** `Switch` export in 33.1.44 — do not import one.
- **No emoji** in UI; Thai copy for labels. Icons inline-SVG / Syncfusion only.
- **Commit → pre-commit hook runs the FULL suite** (backend build+test, frontend tsc+build+vitest, ~40s). Every commit must leave the whole suite green. Never `--no-verify`.
- **Stage narrowly** — add explicit paths only; never `git add -A`/`.`. Never sweep `daily-state.md` or `AGENTS.md`.
- **Git remote is `main`** (not `origin`): push with `git push main HEAD:main`. `gh` needs `--repo ThodsaphonSonthiphin/MenuNest`.
- **Tracking issue:** before the first commit, run `gh issue view 40 --repo ThodsaphonSonthiphin/MenuNest --json state`. If `OPEN`, use `(#40)` in commit subjects. If `CLOSED`, open a follow-up issue via `gh issue create --repo ThodsaphonSonthiphin/MenuNest` (title: "Weather-alert thresholds: free numeric input + on/off toggle"; body references #40, ADR-105, ADR-106) and use that new number. Record the chosen number as `<ISSUE>` for all commits below.
- **Deploys on push to `main`** (Static Web App). Interactive smoke BEFORE pushing (the SPA has no render/visual test gate).

---

### Task 1: Pure control helpers (`weatherAlertControl.ts`) + unit tests

Creates the new pure module beside the old one. Nothing imports it yet except its test, so the suite stays green while the old `weatherAlertOptions.ts` is still consumed by `SettingsPage`. TDD.

**Files:**
- Create: `frontend/src/pages/settings/weatherAlertControl.ts`
- Test: `frontend/src/pages/settings/weatherAlertControl.test.ts`

**Interfaces:**
- Consumes: nothing (pure).
- Produces (Task 2 relies on these exact names/signatures):
  - `alertControlFromStored(stored: number | null | undefined, dflt: number): {on: boolean; value: number}`
  - `storedFromAlertControl(on: boolean, value: number): number`
  - `clampThreshold(value: number, min: number, max: number): number`
  - consts `UV_MIN=1`, `UV_MAX=15`, `FEELS_MIN=1`, `FEELS_MAX=60`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/settings/weatherAlertControl.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {
  alertControlFromStored,
  storedFromAlertControl,
  clampThreshold,
  UV_MIN, UV_MAX, FEELS_MIN, FEELS_MAX,
} from './weatherAlertControl'

describe('alertControlFromStored', () => {
  it('null -> on at default', () => expect(alertControlFromStored(null, 6)).toEqual({on: true, value: 6}))
  it('undefined -> on at default', () => expect(alertControlFromStored(undefined, 40)).toEqual({on: true, value: 40}))
  it('0 -> off, field shows default', () => expect(alertControlFromStored(0, 40)).toEqual({on: false, value: 40}))
  it('N>0 -> on at N', () => expect(alertControlFromStored(35, 40)).toEqual({on: true, value: 35}))
})

describe('storedFromAlertControl', () => {
  it('on -> the value', () => expect(storedFromAlertControl(true, 35)).toBe(35))
  it('off -> 0', () => expect(storedFromAlertControl(false, 35)).toBe(0))
})

describe('clampThreshold', () => {
  it('below min -> min', () => expect(clampThreshold(0, FEELS_MIN, FEELS_MAX)).toBe(FEELS_MIN))
  it('above max -> max', () => expect(clampThreshold(99, UV_MIN, UV_MAX)).toBe(UV_MAX))
  it('non-finite -> min', () => expect(clampThreshold(NaN, FEELS_MIN, FEELS_MAX)).toBe(FEELS_MIN))
  it('rounds a decimal up', () => expect(clampThreshold(35.6, FEELS_MIN, FEELS_MAX)).toBe(36))
  it('in-range passes through', () => expect(clampThreshold(35, FEELS_MIN, FEELS_MAX)).toBe(35))
  it('bounds are UV 1..15 / feels 1..60', () =>
    expect([UV_MIN, UV_MAX, FEELS_MIN, FEELS_MAX]).toEqual([1, 15, 1, 60]))
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/settings/weatherAlertControl.test.ts`
Expected: FAIL — cannot resolve `./weatherAlertControl` (module does not exist yet).

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/pages/settings/weatherAlertControl.ts`:

```ts
// Pure helpers backing the /settings "เตือนอากาศ" numeric threshold controls (ADR-105).
// Storage is the tri-state on UserSettings (ADR-091): null = built-in default, 0 = off,
// N = warn at >= N. The itinerary-card badge (lib/weather.ts) reads that same encoding
// unchanged; these helpers only translate stored values <-> the on/off + number the UI shows.

// Bounds mirror the server validator (UpdateUserSettingsValidator: UV 0..15, feels 0..60).
// Min is 1 (not 0) when a threshold is ON, because 0 is reserved for "off".
export const UV_MIN = 1
export const UV_MAX = 15
export const FEELS_MIN = 1
export const FEELS_MAX = 60

export interface AlertControl {
  on: boolean
  value: number
}

/** Stored tri-state (null=default, 0=off, N=on@N) -> the on/off + value the UI renders. */
export function alertControlFromStored(stored: number | null | undefined, dflt: number): AlertControl {
  if (stored == null) return {on: true, value: dflt}
  if (stored === 0) return {on: false, value: dflt}
  return {on: true, value: stored}
}

/** On/off + value -> the value the full-snapshot PUT persists (off => 0). */
export function storedFromAlertControl(on: boolean, value: number): number {
  return on ? value : 0
}

/** Clamp a typed value to its axis' integer range so a stored value can never fail server validation. */
export function clampThreshold(value: number, min: number, max: number): number {
  const n = Math.round(Number.isFinite(value) ? value : min)
  return Math.min(max, Math.max(min, n))
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run src/pages/settings/weatherAlertControl.test.ts`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```
git add frontend/src/pages/settings/weatherAlertControl.ts frontend/src/pages/settings/weatherAlertControl.test.ts
git commit -m "feat(settings): add numeric weather-alert control helpers (#<ISSUE>)"
```
(The pre-commit hook runs the full suite — expect ~40s; it must pass.)

---

### Task 2: Swap `/settings` weather controls to Checkbox + NumericTextBox; retire presets

Rewrites the weather section of `SettingsPage`, adds CSS, and deletes the now-unused preset module and its test — all in one commit so `tsc`/`build` never see a dangling import.

**Files:**
- Modify: `frontend/src/pages/settings/SettingsPage.tsx` (full replacement below)
- Modify: `frontend/src/pages/settings/SettingsPage.css` (append rules)
- Delete: `weatherAlertOptions.ts` and `weatherAlertOptions.test.ts` (both under `frontend/src/pages/settings/`)

**Interfaces:**
- Consumes: `alertControlFromStored`, `storedFromAlertControl`, `clampThreshold`, `UV_MIN/MAX`, `FEELS_MIN/MAX` (Task 1); `UV_WARN_DEFAULT`, `FEELS_WARN_DEFAULT` from `../trips/lib/weather` (unchanged); `useCurrentUser`, `useUpdateUserSettingsMutation` (unchanged).
- Produces: no new exports (leaf UI).

- [ ] **Step 1: Replace `SettingsPage.tsx` with the numeric-control version**

Full new content of `frontend/src/pages/settings/SettingsPage.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { DropDownList } from '@syncfusion/react-dropdowns'
import type { ChangeEvent as DDLChangeEvent } from '@syncfusion/react-dropdowns'
import { NumericTextBox } from '@syncfusion/react-inputs'
import { Checkbox } from '@syncfusion/react-buttons'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { useUpdateUserSettingsMutation } from '../../shared/api/api'
import { homeOptions } from './homeOptions'
import {
  alertControlFromStored, storedFromAlertControl, clampThreshold,
  UV_MIN, UV_MAX, FEELS_MIN, FEELS_MAX,
} from './weatherAlertControl'
import { UV_WARN_DEFAULT, FEELS_WARN_DEFAULT } from '../trips/lib/weather'
import './SettingsPage.css'

export function SettingsPage() {
  const { familyId, homePath, uvWarnThreshold, feelsLikeWarnThreshold, isLoadingProfile } = useCurrentUser()
  const [updateSettings, { isLoading }] = useUpdateUserSettingsMutation()
  const [saved, setSaved] = useState(false)

  const options = homeOptions(!!familyId)
  const effective = homePath ?? '/budget'
  const value = options.some((o) => o.path === effective) ? effective : null

  // Local control state — bound to the inputs so toggling OFF (persists 0) still keeps the
  // typed number visible in-session and re-sends it when toggled back ON. Synced from the
  // profile once it has loaded.
  const [uvOn, setUvOn] = useState(true)
  const [uvVal, setUvVal] = useState(UV_WARN_DEFAULT)
  const [feelsOn, setFeelsOn] = useState(true)
  const [feelsVal, setFeelsVal] = useState(FEELS_WARN_DEFAULT)

  useEffect(() => {
    if (isLoadingProfile) return
    const uv = alertControlFromStored(uvWarnThreshold, UV_WARN_DEFAULT)
    const feels = alertControlFromStored(feelsLikeWarnThreshold, FEELS_WARN_DEFAULT)
    setUvOn(uv.on); setUvVal(uv.value)
    setFeelsOn(feels.on); setFeelsVal(feels.value)
  }, [isLoadingProfile, uvWarnThreshold, feelsLikeWarnThreshold])

  // Full-snapshot PUT (ADR-091 full-replace). Guard against saving while the profile is still
  // loading — the thresholds would be stale-default nulls and clobber the user's real values.
  const persist = async (next: { homePath: string | null; uvStored: number; feelsStored: number }) => {
    if (isLoadingProfile) return
    setSaved(false)
    try {
      await updateSettings({
        homePath: next.homePath,
        uvWarnThreshold: next.uvStored,
        feelsLikeWarnThreshold: next.feelsStored,
      }).unwrap()
      setSaved(true)
    } catch {
      // Save failed (network/500): leave "บันทึกแล้ว" hidden. No crash, no error affordance.
    }
  }

  const handleHomeChange = (e: DDLChangeEvent) => {
    void persist({
      homePath: e.value as string,
      uvStored: storedFromAlertControl(uvOn, uvVal),
      feelsStored: storedFromAlertControl(feelsOn, feelsVal),
    })
  }

  return (
    <section className="page page--settings">
      <header className="page__header">
        <h1>การตั้งค่า</h1>
      </header>

      <div className="settings-row">
        <div className="settings-row__label">
          <span className="settings-row__icon" aria-hidden="true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
              <path d="M4 11.5 12 4l8 7.5" />
              <path d="M6 10v9h12v-9" />
              <path d="M10 19v-5h4v5" />
            </svg>
          </span>
          <div>
            <div className="settings-row__title" id="settings-home-label">หน้าแรก (Home page)</div>
            <div className="settings-row__sub">หน้าที่จะเปิดขึ้นมาเมื่อเข้าแอป</div>
          </div>
        </div>

        <DropDownList
          className="settings-home-ddl"
          dataSource={options}
          fields={{ text: 'label', value: 'path' }}
          value={value}
          placeholder="ยังไม่ได้เลือกหน้าแรก"
          aria-labelledby="settings-home-label"
          disabled={isLoadingProfile}
          onChange={handleHomeChange}
        />
      </div>

      <div className="settings-row">
        <div className="settings-row__label">
          <span className="settings-row__icon" aria-hidden="true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="4.2" />
              <path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M19.1 4.9l-1.4 1.4M6.3 17.7l-1.4 1.4" />
            </svg>
          </span>
          <div>
            <div className="settings-row__title" id="settings-weather-label">เตือนอากาศ</div>
            <div className="settings-row__sub">เตือนบนการ์ดเมื่อจุดหมายแดด/ร้อนเกินที่ตั้งไว้</div>
          </div>
        </div>

        <div className="settings-weather-controls">
          <div className="settings-weather-field">
            <label className="settings-weather-field__label" id="settings-uv-label">ดัชนี UV</label>
            <div className="settings-weather-field__row">
              <Checkbox
                checked={uvOn}
                disabled={isLoadingProfile}
                aria-labelledby="settings-uv-label"
                onChange={(e) => {
                  const on = e.value
                  setUvOn(on)
                  void persist({ homePath, uvStored: storedFromAlertControl(on, uvVal), feelsStored: storedFromAlertControl(feelsOn, feelsVal) })
                }}
              />
              <NumericTextBox
                className="settings-weather-num"
                value={uvVal}
                min={UV_MIN}
                max={UV_MAX}
                step={1}
                disabled={isLoadingProfile || !uvOn}
                aria-labelledby="settings-uv-label"
                onChange={(e) => {
                  const v = clampThreshold((e.value as number | null) ?? UV_MIN, UV_MIN, UV_MAX)
                  setUvVal(v)
                  void persist({ homePath, uvStored: storedFromAlertControl(uvOn, v), feelsStored: storedFromAlertControl(feelsOn, feelsVal) })
                }}
              />
            </div>
            <p className="settings-weather-field__hint">≥6 = แดดแรง</p>
          </div>

          <div className="settings-weather-field">
            <label className="settings-weather-field__label" id="settings-feels-label">รู้สึกร้อน (°C)</label>
            <div className="settings-weather-field__row">
              <Checkbox
                checked={feelsOn}
                disabled={isLoadingProfile}
                aria-labelledby="settings-feels-label"
                onChange={(e) => {
                  const on = e.value
                  setFeelsOn(on)
                  void persist({ homePath, uvStored: storedFromAlertControl(uvOn, uvVal), feelsStored: storedFromAlertControl(on, feelsVal) })
                }}
              />
              <NumericTextBox
                className="settings-weather-num"
                value={feelsVal}
                min={FEELS_MIN}
                max={FEELS_MAX}
                step={1}
                disabled={isLoadingProfile || !feelsOn}
                aria-labelledby="settings-feels-label"
                onChange={(e) => {
                  const v = clampThreshold((e.value as number | null) ?? FEELS_MIN, FEELS_MIN, FEELS_MAX)
                  setFeelsVal(v)
                  void persist({ homePath, uvStored: storedFromAlertControl(uvOn, uvVal), feelsStored: storedFromAlertControl(feelsOn, v) })
                }}
              />
            </div>
            <p className="settings-weather-field__hint">แนะนำ ~35–40°</p>
          </div>
        </div>
      </div>

      {saved && !isLoading && <p className="settings-saved">บันทึกแล้ว</p>}
    </section>
  )
}
```

- [ ] **Step 2: Append CSS for the new controls**

Append to `frontend/src/pages/settings/SettingsPage.css` (and remove the now-unused `.settings-weather-ddl` rule if one exists):

```css
.settings-weather-field__row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.settings-weather-num {
  width: 84px;
}
.settings-weather-field__row .settings-weather-num[disabled],
.settings-weather-field__row .e-numeric.e-disabled {
  opacity: 0.5;
}
.settings-weather-field__hint {
  margin: 2px 0 0;
  font-size: 12px;
  color: var(--sd-muted, #6b7280);
}
```

- [ ] **Step 3: Delete the retired preset module + its test**

From the settings folder, remove both files from git (staged for the commit):

```
cd frontend/src/pages/settings
git rm weatherAlertOptions.ts weatherAlertOptions.test.ts
cd -
```

- [ ] **Step 4: Verify the badge logic was NOT touched**

Run: `git status --porcelain frontend/src/pages/trips/`
Expected: **no output** (empty) — `lib/weather.ts`, `ItineraryStopCard.tsx`, `stopSummary.ts` unchanged.

- [ ] **Step 5: Typecheck + build + full frontend test suite**

Run: `cd frontend && npx tsc -b && npm run build && npx vitest run`
Expected: tsc clean (no dangling `weatherAlertOptions` import), build succeeds, all vitest pass (including `weatherAlertControl.test.ts`; the deleted preset test is gone).

- [ ] **Step 6: Commit**

```
git add frontend/src/pages/settings/SettingsPage.tsx frontend/src/pages/settings/SettingsPage.css
git commit -m "feat(settings): free numeric UV/feels-like alert thresholds with on-off checkbox (#<ISSUE>)"
```
(Pre-commit runs the whole suite — backend + frontend — and must pass. The removal from Step 3 is already staged.)

---

### Task 3: Interactive smoke, then push + prod verify

No unit test can catch render/layout (the SPA has no component harness — CLAUDE.md). Verify interactively BEFORE pushing, because push deploys straight to prod.

**Files:** none (verification + release).

- [ ] **Step 1: Run the app locally**

Start backend + frontend per the project's run steps (or `/run`). Sign in and open `/settings`.

- [ ] **Step 2: Smoke the settings controls**

Verify:
- "เตือนอากาศ" shows two rows (ดัชนี UV, รู้สึกร้อน) each with a checkbox + number field + hint.
- Type feels-like `35`, blur → "บันทึกแล้ว" appears; reload the page → the field still shows `35` and the checkbox is on (persisted).
- Uncheck รู้สึกร้อน → saves; reload → checkbox off, field disabled/greyed.
- Re-check → field re-enabled showing the last number; saves.
- UV field behaves the same; values clamp to 1..15 (UV) / 1..60 (feels).
- A family-less account can open `/settings` without redirect.

- [ ] **Step 3: Smoke the itinerary-card badge (unchanged path, confirm still works)**

On a trip whose On-arrival feels-like reading `>=` the number you set (e.g. 35), the itinerary card shows the heat badge; a cooler stop does not; turning the threshold off removes the badge.

- [ ] **Step 4: Push (deploys to prod)**

```
git fetch main
git rebase main/main
git push main HEAD:main
```
(Reconcile with concurrent sessions first — this repo is worked by parallel sessions on `main` + worktrees.)

- [ ] **Step 5: Verify on prod**

After the Static Web App deploy completes, open the prod `/settings`, set a feels-like value, reload, and confirm it persists and the card badge reflects it. If `#<ISSUE>` was a "closes" ref, confirm it auto-closed.

---

## Self-Review

**Spec coverage:** numeric input both axes (Task 2) ✓; on/off toggle via Checkbox (Task 2) ✓; tri-state mapping + clamp helpers (Task 1) ✓; save on blur/Enter via NumericTextBox commit + immediate on checkbox (Task 2) ✓; bounds 1..15 / 1..60 (Task 1) ✓; retire presets (Task 2) ✓; badge logic untouched (Global Constraints + Task 2 Step 4) ✓; unit tests + interactive smoke (Tasks 1 & 3) ✓; frontend-only, deploy-on-push (Task 3) ✓.

**Placeholder scan:** none — every step has concrete code/commands. `<ISSUE>` is a deliberate parameter resolved in Global Constraints before the first commit.

**Type consistency:** `alertControlFromStored`/`storedFromAlertControl`/`clampThreshold` and the four bound consts are defined identically in Task 1 and consumed with the same names/signatures in Task 2. `Checkbox` `onChange` uses `e.value: boolean`; `NumericTextBox` `onChange` uses `e.value: number | null` — both matching the installed 33.1.44 type defs. `persist({homePath, uvStored, feelsStored})` shape is used consistently across all three handlers.
