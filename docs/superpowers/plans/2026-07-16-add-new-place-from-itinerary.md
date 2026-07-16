# Add a new Place (+ TikTok review link) from the itinerary add-stop picker — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** From the itinerary "เลือกจุดแวะ" picker, let the user capture a brand-new Place — attaching TikTok/review links inline — and have it become a Stop on the active Day in one flow.

**Architecture:** Frontend-only. Reuse the existing map **Capture** UI (`AddPlaceMode`) for all three entry paths; add a Review-links editor to the shared preview card; and, when launched from the itinerary, chain the two existing RTK-Query mutations client-side (`addTripPlace` → `addStop`). A Redux flag `addStopForDayId` carries the capture context; the capture surface renders on the desktop right-pane map (itinerary stays on the left) or a mobile full-screen overlay. No backend, EF, migration, or MCP change (ADR-071).

**Tech Stack:** React + TypeScript, Redux Toolkit + RTK Query, `@vis.gl/react-google-maps`, Syncfusion React, Vitest (node env — no DOM/RTL harness), Google Maps Platform (existing key).

**Design spec:** `docs/superpowers/specs/2026-07-16-add-new-place-from-itinerary-design.md`
**Mock:** `docs/mocks/trip-add-new-place-from-itinerary-mock.html`
**Decisions:** ADR-067, ADR-068, ADR-069, ADR-070, ADR-071

## Global Constraints

- **Frontend-only.** No changes to `backend/`, EF configs, migrations, or the MCP server. `addTripPlace` already returns `TripPlaceDto` (with `id`) and accepts `reviewLinks: ReviewLink[]`; `addStop` already exists.
- **Full suite green every commit.** `frontend/.husky/pre-commit` runs backend `dotnet build` + `dotnet test` and frontend `tsc --noEmit` + `npm run build` on every commit. Every commit must build. Never `--no-verify`.
- **Stage narrowly.** `git add <explicit paths>` only — never `git add -A`/`.`. Never stage `daily-state.md` or `AGENTS.md`.
- **Commit messages reference the ticket.** Use `(#36)` on intermediate commits; `(closes #36)` on the final code commit.
- **No emoji for new icons.** Reuse existing inline-SVG icons (`ReviewIcon`, etc.); the "+" on the new-place row is a text glyph, not an emoji.
- **No component/visual test harness** (vitest runs in `environment: 'node'`, no jsdom/RTL — see `CLAUDE.md`). Unit-test only pure logic in `lib/`/slice; verify all rendering/DOM interactively (mobile **and** desktop) plus `npm run build`.
- **Review-link rules:** max `MAX_REVIEW_LINKS` (10); validate with `draftsValid`, serialize with `sanitizeReviewDrafts` (both in `frontend/src/pages/trips/lib/reviewLinks.ts`).
- **New-Stop defaults:** `dwellMinutes: 60`, `travelModeToReach: trip.defaultTravelMode ?? 'Drive'` (mirrors today's `AddStopPicker`).
- **Thai UI copy** stays Thai; code/comments English.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `frontend/src/pages/trips/tripsSlice.ts` | `addStopForDayId` capture-context flag + start/end actions | Modify |
| `frontend/src/pages/trips/tripsSlice.test.ts` | Reducer tests for the new flag | Modify |
| `frontend/src/pages/trips/components/AddPlacePreviewCard.tsx` | Review-links section + `confirmLabel`/`error` props | Modify |
| `frontend/src/pages/trips/components/AddPlaceMode.tsx` | Hold review drafts; send `reviewLinks`; capture-context banner + chain `addStop` | Modify |
| `frontend/src/pages/trips/components/AddPlaceSearchBar.tsx` | `bannerOffset` prop (push search bar below the banner) | Modify |
| `frontend/src/pages/trips/components/TripMap.tsx` | Pass `addStopContext` down to `AddPlaceMode` | Modify |
| `frontend/src/pages/trips/components/ItineraryTab.tsx` | "+ เพิ่มสถานที่ใหม่" row in `AddStopPicker` → `startAddStopCapture` | Modify |
| `frontend/src/pages/trips/TripDetailPage.tsx` | Build `addStopContext`; render capture surface (desktop pane / mobile overlay); `onExit` → `endAddStopCapture` | Modify |
| `frontend/src/pages/trips/lib/addStopCapture.ts` | Pure `addStopDayLabel` helper | Create |
| `frontend/src/pages/trips/lib/addStopCapture.test.ts` | Unit tests for the helper | Create |
| `frontend/src/pages/trips/trips-tokens.css` | Styles: new-place row, divider, capture banner, mobile overlay, search-bar offset | Modify |

---

### Task 1: Capture-context flag in the Trips slice

**Files:**
- Modify: `frontend/src/pages/trips/tripsSlice.ts`
- Test: `frontend/src/pages/trips/tripsSlice.test.ts`

**Interfaces:**
- Produces: state field `addStopForDayId: string | null`; actions `startAddStopCapture(dayId: string)`, `endAddStopCapture()`.

- [ ] **Step 1: Write the failing test** — append to `tripsSlice.test.ts`, and update its top import line to add the two new actions:

Replace the existing import line
`import reducer, {setAddMode, setItineraryMapCollapsed} from './tripsSlice'`
with:
```ts
import reducer, {setAddMode, setItineraryMapCollapsed, startAddStopCapture, endAddStopCapture} from './tripsSlice'
```
Append:
```ts
describe('tripsSlice add-stop capture context', () => {
  it('defaults addStopForDayId to null', () => {
    expect(init.addStopForDayId).toBeNull()
  })
  it('startAddStopCapture stores the day id', () => {
    const on = reducer(init, startAddStopCapture('day-1'))
    expect(on.addStopForDayId).toBe('day-1')
  })
  it('endAddStopCapture clears it', () => {
    const on = reducer(init, startAddStopCapture('day-1'))
    const off = reducer(on, endAddStopCapture())
    expect(off.addStopForDayId).toBeNull()
  })
})
```

- [ ] **Step 2: Run the test — verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/tripsSlice.test.ts`
Expected: FAIL — `startAddStopCapture` is not exported / `addStopForDayId` is undefined.

- [ ] **Step 3: Implement the slice change** in `tripsSlice.ts`:

Add to the `TripsState` interface (after `viewerLocation`):
```ts
  addStopForDayId: string | null
```
Add to `initialState` (after `viewerLocation: null,`):
```ts
  addStopForDayId: null,
```
Add reducers (inside `reducers: { ... }`, after `setViewerLocation`):
```ts
    startAddStopCapture(s, a: PayloadAction<string>) { s.addStopForDayId = a.payload },
    endAddStopCapture(s) { s.addStopForDayId = null },
```
Add to the `export const { ... } = tripsSlice.actions` block:
```ts
  startAddStopCapture, endAddStopCapture,
```

- [ ] **Step 4: Run the test — verify it passes**

Run: `cd frontend && npx vitest run src/pages/trips/tripsSlice.test.ts`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/tripsSlice.ts frontend/src/pages/trips/tripsSlice.test.ts
git commit -m "feat(trips): add addStopForDayId capture-context flag to trips slice (#36)"
```

---

### Task 2: Review links attachable at Capture (shared preview card)

Delivers ADR-069 on its own: capturing from the **Places tab** can now attach review links. No itinerary wiring yet.

**Files:**
- Modify: `frontend/src/pages/trips/components/AddPlacePreviewCard.tsx`
- Modify: `frontend/src/pages/trips/components/AddPlaceMode.tsx`

**Interfaces:**
- Consumes: `ReviewDraft`, `sanitizeReviewDrafts`, `draftsValid`, `MAX_REVIEW_LINKS` (from `lib/reviewLinks.ts`); `ReviewLinksSection`; `getErrorMessage` (from `shared/utils/getErrorMessage`).
- Produces: `AddPlacePreviewCard` props `reviewDrafts`, `onReviewDraftsChange`, `confirmLabel?`, `error?` — consumed unchanged by Task 3.

- [ ] **Step 1: Extend `AddPlacePreviewCard.tsx`.** Add imports at the top (after the existing imports):

```ts
import {ReviewLinksSection} from './ReviewLinksSection'
import type {ReviewDraft} from '../lib/reviewLinks'
```

Extend the props interface:
```ts
export interface AddPlacePreviewCardProps {
  place: ResolvedPlaceDto
  category: PlaceCategory
  guessedCategory?: PlaceCategory
  onCategoryChange(c: PlaceCategory): void
  onCancel(): void
  onAdd(): void
  saving: boolean
  variant?: 'floating' | 'sheet'
  reviewDrafts: ReviewDraft[]
  onReviewDraftsChange(drafts: ReviewDraft[]): void
  confirmLabel?: string
  error?: string | null
}
```

Update the destructure to include the new props (default `confirmLabel`):
```ts
export function AddPlacePreviewCard({
  place, category, guessedCategory, onCategoryChange, onCancel, onAdd, saving, variant = 'floating',
  reviewDrafts, onReviewDraftsChange, confirmLabel = 'เพิ่มลงทริป', error,
}: AddPlacePreviewCardProps) {
```

Insert the review section **between** the `.add-preview-cat` block and `.add-preview-foot`:
```tsx
      <ReviewLinksSection drafts={reviewDrafts} onChange={onReviewDraftsChange} />

      {error && <p className="trips-field-error">{error}</p>}
```

Change the add button's label to use `confirmLabel` (replace `{saving ? 'กำลังเพิ่ม…' : 'เพิ่มลงทริป'}`):
```tsx
          {saving ? 'กำลังเพิ่ม…' : confirmLabel}
```

- [ ] **Step 2: Wire drafts + validation into `AddPlaceMode.tsx`.** Add imports:

```ts
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {sanitizeReviewDrafts, draftsValid, MAX_REVIEW_LINKS, type ReviewDraft} from '../lib/reviewLinks'
```

Add state (after `const [showLink, setShowLink] = useState(false)`):
```ts
  const [reviewDrafts, setReviewDrafts] = useState<ReviewDraft[]>([])
  const [formError, setFormError] = useState<string | null>(null)
```

Update `clearSelection` to also reset the drafts + error:
```ts
  const clearSelection = useCallback(() => {
    setSelected(null)
    setGuessedCategory(undefined)
    setReviewDrafts([])
    setFormError(null)
    search.reset()
  }, [search])
```

Replace `doAdd` with:
```ts
  const doAdd = useCallback(async () => {
    if (!selected) return
    if (!draftsValid(reviewDrafts)) {
      setFormError(`ลิงก์รีวิวไม่ถูกต้อง หรือเกิน ${MAX_REVIEW_LINKS} ลิงก์`)
      return
    }
    setFormError(null)
    try {
      await addTripPlace({
        tripId,
        googlePlaceId: selected.googlePlaceId,
        name: selected.name,
        lat: selected.lat,
        lng: selected.lng,
        address: selected.address,
        category,
        priceLevel: selected.priceLevel,
        photoUrl: selected.photoUrl,
        openingHoursJson: selected.openingHoursJson,
        reviewLinks: sanitizeReviewDrafts(reviewDrafts),
        checklist: [],
      }).unwrap()
      clearSelection() // stay armed for the next place (ADR-016)
    } catch (err) {
      setFormError(getErrorMessage(err))
    }
  }, [selected, category, tripId, reviewDrafts, addTripPlace, clearSelection])
```

Pass the new props to `<AddPlacePreviewCard>` (add to the existing JSX props):
```tsx
          reviewDrafts={reviewDrafts}
          onReviewDraftsChange={setReviewDrafts}
          error={formError}
```

- [ ] **Step 3: Type-check + build — verify it passes**

Run: `cd frontend && npm run build`
Expected: PASS (tsc + vite build succeed; no missing-prop errors).

- [ ] **Step 4: Interactive verification** (no component harness — CLAUDE.md)

In a seeded/authed env, Places tab → "+ เพิ่มสถานที่": search a place → in the preview card the "ลิงก์รีวิว (TikTok ฯลฯ)" section appears; add a valid link → "เพิ่มลงทริป" saves; the Place card then shows the review affordance. Enter an invalid URL → the inline error appears and nothing is saved.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/AddPlacePreviewCard.tsx frontend/src/pages/trips/components/AddPlaceMode.tsx
git commit -m "feat(trips): attach review links while capturing a place (shared preview card) (#36)"
```

---

### Task 3: Capture-context plumbing in AddPlaceMode + TripMap

Adds the optional `addStopContext` capability: banner, relabelled button, and the `addStop` chain. Callers don't pass it yet (Task 4), so the build stays green and behaviour is unchanged.

**Files:**
- Modify: `frontend/src/pages/trips/components/AddPlaceMode.tsx`
- Modify: `frontend/src/pages/trips/components/AddPlaceSearchBar.tsx`
- Modify: `frontend/src/pages/trips/components/TripMap.tsx`

**Interfaces:**
- Consumes: `useAddStopMutation`, `type TravelMode` (from `shared/api/api`); the review-drafts state + `doAdd` from Task 2.
- Produces: exported `interface AddStopContext { dayId: string; dayLabel: string; travelMode: TravelMode }`; `AddPlaceMode` prop `addStopContext?: AddStopContext | null`; `TripMap` prop `addStopContext?: AddStopContext | null`; `AddPlaceSearchBar` prop `bannerOffset?: boolean`.

- [ ] **Step 1: `AddPlaceSearchBar.tsx` — banner offset prop.** Add `bannerOffset?: boolean` to `AddPlaceSearchBarProps`, accept it in the destructure, and apply the class:

```tsx
export interface AddPlaceSearchBarProps {
  query: string
  onQueryChange(q: string): void
  suggestions: Suggestion[]
  loading: boolean
  error: string | null
  onPick(placeId: string): void
  onOpenLinkFallback(): void
  onClose(): void
  autoFocus?: boolean
  bannerOffset?: boolean
}

export function AddPlaceSearchBar({
  query, onQueryChange, suggestions, loading, error, onPick, onOpenLinkFallback, onClose, autoFocus, bannerOffset,
}: AddPlaceSearchBarProps) {
  return (
    <div className={`add-search-wrap${bannerOffset ? ' add-search-wrap--banner' : ''}`}>
```
(Leave the rest of the component body unchanged.)

- [ ] **Step 2: `AddPlaceMode.tsx` — context prop, banner, addStop chain.** Extend the api import to add the mutation + type:

```ts
import {useAddTripPlaceMutation, useAddStopMutation, type PlaceCategory, type ResolvedPlaceDto, type TravelMode} from '../../../shared/api/api'
```

Define and export the context type (above `AddPlaceModeProps`):
```ts
export interface AddStopContext {
  dayId: string
  dayLabel: string
  travelMode: TravelMode
}
```

Add `addStopContext` to the props interface + destructure:
```ts
export interface AddPlaceModeProps {
  tripId: string
  onExit(): void
  tappedPlaceId: string | null
  onTapConsumed(): void
  onSelectedChange(pos: {lat: number; lng: number} | null): void
  addStopContext?: AddStopContext | null
}

export function AddPlaceMode({tripId, onExit, tappedPlaceId, onTapConsumed, onSelectedChange, addStopContext}: AddPlaceModeProps) {
```

Add the mutation hook (after `const [addTripPlace, {isLoading: saving}] = useAddTripPlaceMutation()`):
```ts
  const [addStop, {isLoading: adding}] = useAddStopMutation()
```

Replace `doAdd` (from Task 2) with the context-aware version:
```ts
  const doAdd = useCallback(async () => {
    if (!selected) return
    if (!draftsValid(reviewDrafts)) {
      setFormError(`ลิงก์รีวิวไม่ถูกต้อง หรือเกิน ${MAX_REVIEW_LINKS} ลิงก์`)
      return
    }
    setFormError(null)
    try {
      const created = await addTripPlace({
        tripId,
        googlePlaceId: selected.googlePlaceId,
        name: selected.name,
        lat: selected.lat,
        lng: selected.lng,
        address: selected.address,
        category,
        priceLevel: selected.priceLevel,
        photoUrl: selected.photoUrl,
        openingHoursJson: selected.openingHoursJson,
        reviewLinks: sanitizeReviewDrafts(reviewDrafts),
        checklist: [],
      }).unwrap()
      if (addStopContext) {
        // ADR-071: non-atomic — if this fails, the Place stays captured in the library.
        await addStop({
          tripId,
          dayId: addStopContext.dayId,
          tripPlaceId: created.id,
          dwellMinutes: 60,
          travelModeToReach: addStopContext.travelMode,
        }).unwrap()
        onExit() // ADR-068 single-shot: leave capture; host clears the flag + itinerary refetches
      } else {
        clearSelection() // stay armed for the next place (ADR-016)
      }
    } catch (err) {
      setFormError(getErrorMessage(err))
    }
  }, [selected, category, tripId, reviewDrafts, addTripPlace, addStop, addStopContext, clearSelection, onExit])
```

Render the banner (add as the FIRST child inside the returned fragment, before `<AddPlaceSearchBar ...>`):
```tsx
      {addStopContext && (
        <div className="add-capture-banner">
          <button type="button" className="add-capture-back" aria-label="ยกเลิก" onClick={onExit}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"><path d="M15 6l-6 6 6 6" /></svg>
          </button>
          <span className="add-capture-txt">เพิ่มสถานที่ใหม่เป็นจุดแวะ<small>{addStopContext.dayLabel}</small></span>
        </div>
      )}
```

Pass `bannerOffset` to the search bar and `confirmLabel`/`saving` to the preview card. On `<AddPlaceSearchBar>` add:
```tsx
        bannerOffset={!!addStopContext}
```
On `<AddPlacePreviewCard>` add / update:
```tsx
          saving={saving || adding}
          confirmLabel={addStopContext ? 'เพิ่มเป็นจุดแวะ' : 'เพิ่มลงทริป'}
```

- [ ] **Step 3: `TripMap.tsx` — pass the context through.** Add the import (replace the existing `import {AddPlaceMode} from './AddPlaceMode'`):

```ts
import {AddPlaceMode, type AddStopContext} from './AddPlaceMode'
```

Add `addStopContext` to the destructured props and the props type:
```tsx
  addMode = false,
  addStopContext = null,
  gestureHandling = 'greedy',
```
```tsx
  addMode?: boolean
  addStopContext?: AddStopContext | null
  gestureHandling?: string
```

Pass it to `<AddPlaceMode>`:
```tsx
        {addMode && tripId && (
          <AddPlaceMode
            tripId={tripId}
            onExit={() => onExitAddMode?.()}
            tappedPlaceId={tappedPlaceId}
            onTapConsumed={onTapConsumed}
            onSelectedChange={setAddPin}
            addStopContext={addStopContext}
          />
        )}
```

- [ ] **Step 4: Type-check + build — verify it passes**

Run: `cd frontend && npm run build`
Expected: PASS. Behaviour unchanged for existing callers (`addStopContext` defaults to null → banner hidden, button reads "เพิ่มลงทริป", no `addStop` call).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/AddPlaceMode.tsx frontend/src/pages/trips/components/AddPlaceSearchBar.tsx frontend/src/pages/trips/components/TripMap.tsx
git commit -m "feat(trips): AddPlaceMode capture-context (banner + relabel + addStop chain) (#36)"
```

---

### Task 4: Itinerary trigger + host rendering + styles (wires the flow end-to-end)

Completes the user-reachable flow (ADR-067/068/070). Closes #36.

**Files:**
- Create: `frontend/src/pages/trips/lib/addStopCapture.ts`
- Test: `frontend/src/pages/trips/lib/addStopCapture.test.ts`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: `startAddStopCapture`/`endAddStopCapture` + `addStopForDayId` (Task 1); `AddStopContext`, `TripMap` `addStopContext` prop (Task 3); `dayRoute.days` (from `useDayRoute`); `trip.defaultTravelMode`, `trip.destination`.
- Produces: pure `addStopDayLabel(days, dayId, destination?)`.

- [ ] **Step 1: Write the failing helper test** — create `frontend/src/pages/trips/lib/addStopCapture.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {addStopDayLabel} from './addStopCapture'

const days = [{id: 'a'}, {id: 'b'}, {id: 'c'}]

describe('addStopDayLabel', () => {
  it('labels by 1-based day index', () => {
    expect(addStopDayLabel(days, 'b')).toBe('วัน 2')
  })
  it('appends the destination when present', () => {
    expect(addStopDayLabel(days, 'a', 'ระยอง')).toBe('วัน 1 · ระยอง')
  })
  it('ignores a blank destination', () => {
    expect(addStopDayLabel(days, 'c', '   ')).toBe('วัน 3')
  })
  it('returns null when the day id is not found', () => {
    expect(addStopDayLabel(days, 'z')).toBeNull()
  })
})
```

- [ ] **Step 2: Run the test — verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/lib/addStopCapture.test.ts`
Expected: FAIL — module `./addStopCapture` not found.

- [ ] **Step 3: Implement the helper** — create `frontend/src/pages/trips/lib/addStopCapture.ts`:

```ts
// Pure label for the capture-context banner: "วัน N" (+ optional destination).
export function addStopDayLabel(
  days: {id: string}[],
  dayId: string,
  destination?: string | null,
): string | null {
  const idx = days.findIndex((d) => d.id === dayId)
  if (idx < 0) return null
  const base = `วัน ${idx + 1}`
  const dest = destination?.trim()
  return dest && dest.length > 0 ? `${base} · ${dest}` : base
}
```

- [ ] **Step 4: Run the test — verify it passes**

Run: `cd frontend && npx vitest run src/pages/trips/lib/addStopCapture.test.ts`
Expected: PASS.

- [ ] **Step 5: Add the "+ เพิ่มสถานที่ใหม่" row to `AddStopPicker` in `ItineraryTab.tsx`.**

Add `startAddStopCapture` to the tripsSlice import (replace the existing `import {setActiveDay, setStopEditor, setItineraryMapCollapsed} from '../tripsSlice'`):
```ts
import {setActiveDay, setStopEditor, setItineraryMapCollapsed, startAddStopCapture} from '../tripsSlice'
```

Add `onAddNew` to the `AddStopPicker` prop list + destructure:
```tsx
function AddStopPicker({
  tripId,
  dayId,
  places,
  existingTripPlaceIds,
  defaultTravelMode,
  onClose,
  onAddNew,
}: {
  tripId: string
  dayId: string
  places: TripPlaceDto[]
  existingTripPlaceIds: Set<string>
  defaultTravelMode: string
  onClose: () => void
  onAddNew: () => void
}) {
```

Replace the whole body of `AddStopPicker` (the `if (available.length === 0) {...}` early return **and** the main `return`) with a single return that always shows the new-place row:
```tsx
  const [addStop] = useAddStopMutation()
  const [addError, setAddError] = useState<string | null>(null)

  const available = places.filter((p) => !existingTripPlaceIds.has(p.id))

  return (
    <div className="add-stop-picker">
      <div className="add-stop-header">
        <span>เลือกจุดแวะ</span>
        <button className="btn-text" onClick={onClose}>✕</button>
      </div>

      <button type="button" className="add-stop-new" onClick={onAddNew}>
        <span className="add-stop-new-plus">+</span>
        <span className="add-stop-new-txt">
          เพิ่มสถานที่ใหม่
          <span className="add-stop-new-sub">ค้นหา / แตะหมุดบนแผนที่ / วางลิงก์</span>
        </span>
      </button>

      {available.length === 0 ? (
        <p className="trips-muted">สถานที่ในคลังทั้งหมดอยู่ในแผนแล้ว</p>
      ) : (
        <>
          <div className="add-stop-divider">หรือเลือกจากคลังสถานที่</div>
          <ul className="add-stop-list">
            {available.map((p) => (
              <li key={p.id}>
                <button
                  className="add-stop-item"
                  onClick={async () => {
                    try {
                      await addStop({
                        tripId,
                        dayId,
                        tripPlaceId: p.id,
                        dwellMinutes: 60,
                        travelModeToReach: (defaultTravelMode as 'Drive' | 'Walk' | 'Transit') ?? 'Drive',
                      }).unwrap()
                      onClose()
                    } catch (err) {
                      setAddError(getErrorMessage(err))
                    }
                  }}
                >
                  <span className="add-stop-name">{p.name}</span>
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
      {addError && <p className="trips-field-error">{addError}</p>}
    </div>
  )
```

Wire `onAddNew` where `<AddStopPicker>` is rendered (the `pickerOpen` block near the bottom):
```tsx
      {pickerOpen ? (
        <AddStopPicker
          tripId={tripId}
          dayId={resolvedDayId}
          places={places ?? []}
          existingTripPlaceIds={existingTripPlaceIds}
          defaultTravelMode={trip?.defaultTravelMode ?? 'Drive'}
          onClose={() => setPickerOpen(false)}
          onAddNew={() => {
            dispatch(startAddStopCapture(resolvedDayId))
            setPickerOpen(false)
          }}
        />
      ) : (
```

- [ ] **Step 6: Render the capture surface in `TripDetailPage.tsx`.**

Add imports (extend the tripsSlice import + add the helper + the type):
```ts
import { setActiveTab, setPlacesView, setAddMode, setViewerLocation, setPlaceEditor, endAddStopCapture } from './tripsSlice'
import { addStopDayLabel } from './lib/addStopCapture'
import type { TravelMode } from '../../shared/api/api'
```

Read the flag + build the context (place it just before the `const isDesktop = bp === 'desktop'` line, so `dayRoute` and `trip` are already defined):
```ts
  const addStopForDayId = useAppSelector((s) => s.trips.addStopForDayId)
  const addStopLabel = addStopForDayId ? addStopDayLabel(dayRoute.days, addStopForDayId, trip?.destination) : null
  const addStopContext =
    addStopForDayId && addStopLabel
      ? { dayId: addStopForDayId, dayLabel: addStopLabel, travelMode: (trip?.defaultTravelMode ?? 'Drive') as TravelMode }
      : null
```

**Desktop branch** — update the right-pane `<TripMap>` (currently `addMode={tab === 'places' && addMode}` / `onExitAddMode={() => dispatch(setAddMode(false))}`) to:
```tsx
          <TripMap
            places={places ?? []}
            route={tab === 'itinerary' ? dayRoute.route : undefined}
            segments={tab === 'itinerary' ? dayRoute.segments : undefined}
            summaryLabel={dayRoute.dayLabel}
            summaryText={dayRoute.summaryText}
            viewerLocation={tab === 'itinerary' ? dayRoute.viewerLocation : undefined}
            addMode={(tab === 'places' && addMode) || !!addStopContext}
            addStopContext={addStopContext}
            tripId={tripId}
            onExitAddMode={() => {
              if (addStopContext) dispatch(endAddStopCapture())
              else dispatch(setAddMode(false))
            }}
          />
```

**Mobile branch** — add a full-screen capture overlay just before the closing `</section>` of the mobile return (after the `editingPlace` dialog block):
```tsx
      {addStopContext && (
        <div className="capture-overlay">
          <TripMap
            places={places ?? []}
            addMode
            addStopContext={addStopContext}
            tripId={tripId}
            onExitAddMode={() => dispatch(endAddStopCapture())}
          />
        </div>
      )}
```

- [ ] **Step 7: Add styles to `trips-tokens.css`** (append near the existing `.add-stop-picker` / add-mode blocks):

```css
/* ── Add-stop picker: "+ เพิ่มสถานที่ใหม่" shortcut (#36) ── */
.add-stop-new {
  display: flex; align-items: center; gap: 9px; width: 100%; text-align: left;
  background: var(--teal-soft); border: 1.5px solid var(--teal); border-radius: 10px;
  padding: 11px 13px; color: var(--teal-deep); font: inherit; font-weight: 700;
  font-size: 0.875rem; cursor: pointer;
  transition: background 0.12s ease, border-color 0.12s ease;
}
.add-stop-new:hover { background: #d6eef0; }
.add-stop-new-plus {
  flex: none; width: 22px; height: 22px; border-radius: 50%;
  background: var(--teal); color: #fff; display: flex; align-items: center;
  justify-content: center; font-weight: 700; line-height: 1;
}
.add-stop-new-txt { display: flex; flex-direction: column; min-width: 0; }
.add-stop-new-sub { font-weight: 500; font-size: 11px; color: var(--teal-deep); opacity: 0.8; }
.add-stop-divider {
  display: flex; align-items: center; gap: 8px; color: var(--muted);
  font-size: 11px; margin: 2px 0;
}
.add-stop-divider::before, .add-stop-divider::after {
  content: ""; flex: 1; height: 1px; background: var(--border);
}

/* ── Capture-context banner over the map (#36 / ADR-070) ── */
.add-capture-banner {
  position: absolute; top: 12px; left: 14px; right: 14px; z-index: 8;
  display: flex; align-items: center; gap: 9px;
  background: var(--ink); color: #fff; border-radius: 12px; padding: 9px 12px;
  box-shadow: 0 8px 22px rgba(15, 23, 42, 0.28);
}
.add-capture-back {
  flex: none; width: 26px; height: 26px; border: 0; border-radius: 8px;
  background: rgba(255, 255, 255, 0.12); color: #fff; cursor: pointer;
  display: flex; align-items: center; justify-content: center;
}
.add-capture-back svg { width: 15px; height: 15px; }
.add-capture-txt { font-size: 12px; font-weight: 700; line-height: 1.25; }
.add-capture-txt small { display: block; font-weight: 500; color: #9fb0c4; font-size: 10.5px; }

/* Push the floating search bar below the banner when it is shown */
.add-search-wrap--banner { top: 66px; }

/* ── Mobile full-screen capture overlay (#36 / ADR-070) ── */
.capture-overlay { position: fixed; inset: 0; z-index: 1100; background: #fff; }
.capture-overlay .trip-map { height: 100%; min-height: 0; }
```

- [ ] **Step 8: Type-check + build — verify it passes**

Run: `cd frontend && npm run build`
Expected: PASS (tsc + vite build).

- [ ] **Step 9: Run the trips unit tests — verify green**

Run: `cd frontend && npx vitest run src/pages/trips`
Expected: PASS (slice + helper + existing lib tests).

- [ ] **Step 10: Interactive verification** (required — no component harness, CLAUDE.md)

Seeded/authed env, on **mobile** and **desktop**:
1. แผนเที่ยว → "+ เพิ่มจุดแวะ" → the sheet shows "+ เพิ่มสถานที่ใหม่" on top + "หรือเลือกจากคลังสถานที่" divider + existing list. With every saved Place already scheduled, the new-place row still shows (message: "สถานที่ในคลังทั้งหมดอยู่ในแผนแล้ว").
2. Tap it → capture surface opens with the dark banner "เพิ่มสถานที่ใหม่เป็นจุดแวะ · วัน N"; the search bar sits below the banner. Desktop: itinerary list stays on the left.
3. All three paths work: search → pick; map-tap a POI; วางลิงก์. Preview card shows the review-links section; button reads **เพิ่มเป็นจุดแวะ**.
4. (optional) add a TikTok link → tap เพิ่มเป็นจุดแวะ → returns to the itinerary; the new Stop appears on Day N with dwell 60 and the review affordance; the Place is also in คลังสถานที่.
5. Back arrow / Esc cancels without writing.
6. Error path: force `addStop` to fail (e.g. offline after the place POST) → error shown, and the captured Place is present in คลังสถานที่ / the picker for a retry.

- [ ] **Step 11: Commit**

```bash
git add frontend/src/pages/trips/lib/addStopCapture.ts frontend/src/pages/trips/lib/addStopCapture.test.ts frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/TripDetailPage.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): add a new place (+review link) as a stop from the itinerary picker (closes #36)"
```

- [ ] **Step 12: Commit the design docs** (grill-then-plan artifacts are not swept into feature commits — commit them explicitly)

```bash
git add docs/adr/067-add-new-place-from-itinerary-reuses-capture-and-schedules-stop.md docs/adr/068-itinerary-capture-is-single-shot.md docs/adr/069-review-links-entered-at-capture-shared-card.md docs/adr/070-itinerary-capture-keeps-itinerary-visible.md docs/adr/071-frontend-only-chain-addtripplace-addstop.md docs/superpowers/specs/2026-07-16-add-new-place-from-itinerary-design.md docs/superpowers/plans/2026-07-16-add-new-place-from-itinerary.md docs/mocks/trip-add-new-place-from-itinerary-mock.html CONTEXT.md
git commit -m "docs(trips): ADR-067..071 + design spec/plan/mock for add-new-place-from-itinerary (#36)"
```

---

## Self-Review

**1. Spec coverage**
- Add-new-place from picker → Stop on active Day (ADR-067): Task 4 (row + `startAddStopCapture`) + Task 3 (`addStop` chain). ✓
- Review links at Capture via shared card (ADR-069): Task 2. ✓
- Single-shot return (ADR-068): Task 3 `onExit()` after `addStop`. ✓
- Desktop keeps itinerary / mobile full-screen (ADR-070): Task 4 desktop right-pane `addMode` + mobile `.capture-overlay`. ✓
- Frontend-only, non-atomic (ADR-071): no backend files touched; error path surfaces `formError`, Place remains. ✓
- Defaults dwell 60 / defaultTravelMode: Task 3 `addStop` call + Task 4 picker. ✓
- Context banner + cancel: Task 3 `.add-capture-banner` + `onExit`. ✓

**2. Placeholder scan** — no TBD/TODO; every code step shows full code; error copy and Thai labels are literal. ✓

**3. Type consistency** — `AddStopContext { dayId; dayLabel; travelMode: TravelMode }` defined in Task 3 (AddPlaceMode), imported by TripMap (Task 3) and built in TripDetailPage (Task 4) with the exact field names. `addStopDayLabel(days, dayId, destination?)` signature matches its test and its call site. `startAddStopCapture(dayId)` / `endAddStopCapture()` names match slice (Task 1), ItineraryTab (Task 4), and TripDetailPage (Task 4). `AddPlacePreviewCard` props (`reviewDrafts`, `onReviewDraftsChange`, `confirmLabel?`, `error?`) defined in Task 2 and passed in Tasks 2–3. ✓
