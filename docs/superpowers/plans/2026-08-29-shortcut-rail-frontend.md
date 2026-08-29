# Shortcut Rail Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use sp-subagent-driven-development (recommended) or sp-executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put the shortcut rail on `/budget` — one button bottom-right that expands into Undo, Redo and Change history — and ship it to prod matching the approved mock.

**Architecture:** A `ShortcutRailProvider` sits in `AppLayout` beside the `ConfirmProvider` already there; a page that wants a rail declares its contents through a `useShortcutRail` hook, and a page that declares nothing gets no rail. The rail itself is Syncfusion's `SpeedDialComponent`. Change history is a sheet over the page, not a route.

**Tech Stack:** React 19, Vite 8, TypeScript 6, Redux Toolkit + RTK Query, `@syncfusion/ej2-react-buttons` (new), Playwright.

**Spec:** `docs/adr/menunest-191`, `192`, `195`, `197`, `199`, `200`, `201`, plus the approved mock at `docs/mocks/budget-shortcut-rail-mock.html` — **its spec table is the checkable half and this plan is diffed against it.**

**Depends on:** both backend plans merged and their migrations applied. This plan calls `GET/POST /api/budget/history…`, which does not exist until then.

## Global Constraints

- **The frontend has NO component or visual test harness.** `vite.config.ts` runs vitest in `environment: 'node'` — no jsdom, no React Testing Library. `tsc`, `npm run build` and the unit suite **cannot** catch a rendering, layout or CSS bug. Two consequences this plan is built around: pure logic goes into `frontend/src/pages/budget/lib/*.ts` so vitest can actually test it, and **Task 8's Playwright spec plus Task 9's interactive check on a real phone are the only real gates.**
- **Prod deploys on push to `main`.** A broken render ships. Task 9's phone check is not optional (CLAUDE.md, learned on #36 and #46).
- **Do not diff against `docs/mocks/budget-redesign-mock.html`.** It predates the current CSS and is dark-first with accent `#6366f1`; the shipped app is light-only with `#4f46e5`. The right mock is `budget-shortcut-rail-mock.html`.
- **No new colour hexes.** Everything comes from the tokens already declared at the top of `frontend/src/pages/budget/BudgetPage.css`.
- **Every commit references the issue** — `(#106)` or `Refs #106`.
- **`git add <explicit paths>` only.**
- The pre-commit hook runs the full backend + frontend suite; expect ~40s.

## File Structure

| File | Responsibility |
|---|---|
| `frontend/src/shared/components/ShortcutRailProvider.tsx` | context + the rail's mount point |
| `frontend/src/shared/hooks/useShortcutRail.ts` | the hook a page calls to declare its rail |
| `frontend/src/shared/components/ShortcutRail.tsx` | the SpeedDial itself, hide-on-scroll, keyboard |
| `frontend/src/shared/lib/railVisibility.ts` | the pure hide/show decision — the vitest-able part |
| `frontend/src/shared/lib/keyBinding.ts` | pure "should this keystroke fire?" — also vitest-able |
| `frontend/src/pages/budget/components/ChangeHistorySheet.tsx` | the sheet |
| `frontend/src/pages/budget/BudgetPage.tsx` | declares the rail |
| `frontend/src/shared/api/api.ts` | three endpoints + one tag |
| `frontend/e2e/budget.shortcut-rail.spec.ts` | the smoke spec |

---

### Task 1: The API endpoints and the new dependency

**Files:**
- Modify: `frontend/package.json`
- Modify: `frontend/src/shared/api/api.ts`

**Interfaces:**
- Produces: `useListBudgetHistoryQuery`, `useUndoBudgetChangeMutation`, `useRedoBudgetChangeMutation`, and the `BudgetChangeDto` type.

- [ ] **Step 1: Install the dependency**

```bash
cd frontend
npm install @syncfusion/ej2-react-buttons@33.1.49
```

Pin the exact version to match the `@syncfusion/ej2-buttons` already in `package.json`. **No stylesheet import is needed** — `main.tsx:38` already imports `@syncfusion/ej2-buttons/styles/material.css`, which already carries 134 `e-speeddial` rules. This is the #97 trap and it is already closed; do not add an import "to be safe".

- [ ] **Step 2: Add the DTO type**

In `api.ts`, beside the other budget DTOs:

```ts
export type BudgetChangeKind = 'Assign' | 'Move' | 'Cover' | 'EverydayMark'

export interface BudgetChangeDto {
  id: string
  userId: string
  userDisplayName: string
  kind: BudgetChangeKind
  batchId: string | null
  categoryName: string
  secondCategoryName: string | null
  delta: number
  flagValue: boolean | null
  isUndone: boolean
  undoneByDisplayName: string | null
  createdAt: string
  /** menunest-197: false when the envelope was deleted — the row stays, unpressable. */
  canUndo: boolean
  blockedReason: string | null
}
```

- [ ] **Step 3: Add the tag and the three endpoints**

Add `'BudgetHistory'` to the main api's `tagTypes` array (line ~694).

Then, beside `setEverydayMarks`:

```ts
        listBudgetHistory: build.query<BudgetChangeDto[], {year: number; month: number}>({
            query: ({year, month}) => `/api/budget/history?year=${year}&month=${month}`,
            providesTags: (_r, _e, a) => [{type: 'BudgetHistory', id: `${a.year}-${a.month}`}],
        }),
        // Undo and redo move money, so they invalidate the summary as well as
        // the history list — the numbers on the page behind the sheet change.
        undoBudgetChange: build.mutation<void, {id: string; year: number; month: number}>({
            query: ({id}) => ({url: `/api/budget/history/${id}/undo`, method: 'POST'}),
            invalidatesTags: (_r, _e, a) => [
                {type: 'BudgetHistory', id: `${a.year}-${a.month}`},
                {type: 'BudgetSummary', id: `${a.year}-${a.month}`},
            ],
        }),
        redoBudgetChange: build.mutation<void, {id: string; year: number; month: number}>({
            query: ({id}) => ({url: `/api/budget/history/${id}/redo`, method: 'POST'}),
            invalidatesTags: (_r, _e, a) => [
                {type: 'BudgetHistory', id: `${a.year}-${a.month}`},
                {type: 'BudgetSummary', id: `${a.year}-${a.month}`},
            ],
        }),
```

- [ ] **Step 4: Add `batchId` to the assign mutation**

menunest-196 makes one quick-assign press a single history row, and the backend already accepts it:

```ts
        setAssignedAmount: build.mutation<void, {categoryId: string; year: number; month: number; amount: number; timeZoneId?: string; batchId?: string}>({
```

- [ ] **Step 5: Verify and commit**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: clean.

```bash
git add frontend/package.json frontend/package-lock.json frontend/src/shared/api/api.ts
git commit -m "feat(budget): api endpoints for the change history (#106)"
```

---

### Task 2: The provider and its hook

**Files:**
- Create: `frontend/src/shared/components/ShortcutRailProvider.tsx`
- Create: `frontend/src/shared/hooks/useShortcutRail.ts`
- Modify: `frontend/src/shared/components/AppLayout.tsx`

**Interfaces:**
- Produces:
  ```ts
  export interface RailAction { key: string; label: string; icon: string; hint?: string; disabled?: boolean; onPress: () => void }
  export interface RailDeclaration { actions: RailAction[] }
  export function useShortcutRail(declaration: RailDeclaration | null): void
  ```
  A page calls `useShortcutRail({actions})` to show a rail and `useShortcutRail(null)` — or simply never calls it — to show none.

- [ ] **Step 1: Write the provider**

```tsx
import {createContext, useCallback, useMemo, useState, type ReactNode} from 'react'
import {ShortcutRail} from './ShortcutRail'

export interface RailAction {
  key: string
  label: string
  /** A Syncfusion icon class, or a single character rendered as text. */
  icon: string
  /** Desktop-only keyboard hint, e.g. "⌘Z" (menunest-200). */
  hint?: string
  disabled?: boolean
  onPress: () => void
}

export interface RailDeclaration { actions: RailAction[] }

interface RailContextValue { declare: (d: RailDeclaration | null) => void }

export const ShortcutRailContext = createContext<RailContextValue | null>(null)

/**
 * Mirrors ConfirmProvider, three lines above this in AppLayout: a
 * cross-cutting UI capability any page can opt into (menunest-199). A page
 * that declares nothing gets no rail, which is why /budget can have one and
 * AccountDetailPage — whose bottom-right corner is taken by .bdg-fab — simply
 * does not.
 */
export function ShortcutRailProvider({children}: {children: ReactNode}) {
  const [declaration, setDeclaration] = useState<RailDeclaration | null>(null)

  const declare = useCallback((d: RailDeclaration | null) => setDeclaration(d), [])
  const value = useMemo<RailContextValue>(() => ({declare}), [declare])

  return (
    <ShortcutRailContext.Provider value={value}>
      {children}
      {declaration && <ShortcutRail actions={declaration.actions} />}
    </ShortcutRailContext.Provider>
  )
}
```

- [ ] **Step 2: Write the hook**

```ts
import {useContext, useEffect} from 'react'
import {ShortcutRailContext, type RailDeclaration} from '../components/ShortcutRailProvider'

/**
 * Declares this page's rail for as long as the page is mounted, and clears it
 * on unmount so the rail never outlives the page that asked for it.
 */
export function useShortcutRail(declaration: RailDeclaration | null) {
  const ctx = useContext(ShortcutRailContext)
  if (!ctx) throw new Error('useShortcutRail must be used inside ShortcutRailProvider')

  const {declare} = ctx
  useEffect(() => {
    declare(declaration)
    return () => declare(null)
  }, [declare, declaration])
}
```

> The `declaration` object must be memoised by the caller (`useMemo`) or this effect re-runs every render. Task 3's `BudgetPage` shows the shape.

- [ ] **Step 3: Mount the provider**

In `AppLayout.tsx`, wrap inside `ConfirmProvider`:

```tsx
      <ConfirmProvider>
        <ShortcutRailProvider>
          <div className="app-shell">
            <NavBar />
            <main className="app-main">
              <Outlet />
            </main>
          </div>
        </ShortcutRailProvider>
      </ConfirmProvider>
```

- [ ] **Step 4: Verify and commit**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: clean once Task 3 supplies `ShortcutRail`. **Write a temporary stub** `ShortcutRail.tsx` returning `null` so this task builds on its own, and replace it in Task 3.

```bash
git add frontend/src/shared/components/ShortcutRailProvider.tsx \
        frontend/src/shared/components/ShortcutRail.tsx \
        frontend/src/shared/hooks/useShortcutRail.ts \
        frontend/src/shared/components/AppLayout.tsx
git commit -m "feat(ui): a shortcut rail provider pages opt into (#106)"
```

---

### Task 3: The rail itself, and `/budget` opting in

**Files:**
- Modify: `frontend/src/shared/components/ShortcutRail.tsx` (replacing the stub)
- Modify: `frontend/src/pages/budget/BudgetPage.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.css`

**The mock's spec table is the contract.** Copy these values exactly: main button 52×52, `border-radius: 50%`, `background: var(--accent)`, `box-shadow: 0 8px 24px rgba(79,70,229,0.45)`; items 44×44 on `var(--bg-card)` with `1px solid var(--border)`, `color: var(--accent)`, `box-shadow: 0 4px 14px rgba(15,23,42,0.14)`; `right: 16px`, `bottom: 22px`; 10px between items, 12px to the main button; scrim `rgba(15,23,42,0.20)`.

- [ ] **Step 1: Write the rail**

```tsx
import {SpeedDialComponent, type SpeedDialItemModel} from '@syncfusion/ej2-react-buttons'
import {useMemo} from 'react'
import type {RailAction} from './ShortcutRailProvider'

/**
 * menunest-192: one button resting bottom-right, expanding VERTICALLY UPWARD.
 * position and direction are the component's own properties, so the corner and
 * the expansion need no custom positioning.
 */
export function ShortcutRail({actions}: {actions: RailAction[]}) {
  const items = useMemo<SpeedDialItemModel[]>(
    () => actions.map(a => ({
      id: a.key,
      text: a.hint ? `${a.label} ${a.hint}` : a.label,
      iconCss: undefined,
      disabled: a.disabled,
    })),
    [actions],
  )

  return (
    <div className="bdg-rail" data-testid="bdg-rail">
      <SpeedDialComponent
        position="BottomRight"
        mode="Linear"
        direction="Up"
        items={items}
        modal={true}
        clicked={(args) => {
          const hit = actions.find(a => a.key === args.item?.id)
          if (hit && !hit.disabled) hit.onPress()
        }}
      />
    </div>
  )
}
```

> **Read `@syncfusion/ej2-react-buttons`' own typings before finalising this.** The prop names above come from the vendor docs (`position`, `mode`, `direction`, `items`, `modal`, `clicked`); if the installed 33.1.49 typings disagree, follow the typings and note the difference in the commit body.

- [ ] **Step 2: Declare the rail from `BudgetPage`**

```tsx
  const [historyOpen, setHistoryOpen] = useState(false)
  const isMac = typeof navigator !== 'undefined' && /Mac/i.test(navigator.platform)
  const mod = isMac ? '⌘' : 'Ctrl+'

  // menunest-191: undo nearest the thumb, then redo, then change history.
  // menunest-200: the hint shows on desktop widths only.
  const railActions = useMemo(() => [
    {key: 'undo', label: 'Undo', icon: '↶', hint: `${mod}Z`, onPress: () => void undoLatest()},
    {key: 'redo', label: 'Redo', icon: '↷', hint: `${mod}⇧Z`, onPress: () => void redoLatest()},
    {key: 'history', label: 'Change history', icon: '⌚', onPress: () => setHistoryOpen(true)},
  ], [mod, undoLatest, redoLatest])

  useShortcutRail(useMemo(() => ({actions: railActions}), [railActions]))
```

`undoLatest` / `redoLatest` call the mutations from Task 1 against the newest row in the history query. Put that selection in `frontend/src/pages/budget/lib/latestUndoable.ts` as a pure function so vitest can test it:

```ts
import type {BudgetChangeDto} from '../../../shared/api/api'

/** The newest row this user can still undo, or null. */
export function latestUndoable(rows: BudgetChangeDto[]): BudgetChangeDto | null {
  return rows.find(r => !r.isUndone && r.canUndo) ?? null
}

/** The newest row this user can redo — the most recently undone one. */
export function latestRedoable(rows: BudgetChangeDto[]): BudgetChangeDto | null {
  return rows.find(r => r.isUndone && r.canUndo) ?? null
}
```

with `latestUndoable.test.ts` covering: empty list, all undone, a dead row skipped, newest-first ordering respected.

- [ ] **Step 3: Add the CSS**

Append to `BudgetPage.css`, using only existing tokens. The rail is rendered from `AppLayout`, so scope it by its own class rather than nesting it under `.bdg-page`.

- [ ] **Step 4: Verify and commit**

Run: `cd frontend && npx tsc -b && npm run build && npx vitest run`
Expected: clean, and the new `latestUndoable` tests pass.

```bash
git add frontend/src/shared/components/ShortcutRail.tsx \
        frontend/src/pages/budget/BudgetPage.tsx \
        frontend/src/pages/budget/BudgetPage.css \
        frontend/src/pages/budget/lib/latestUndoable.ts \
        frontend/src/pages/budget/lib/latestUndoable.test.ts
git commit -m "feat(budget): the shortcut rail on /budget (#106)"
```

---

### Task 4: Hide on scroll, with both guards

**Files:**
- Create: `frontend/src/shared/lib/railVisibility.ts` + `.test.ts`
- Modify: `frontend/src/shared/components/ShortcutRail.tsx`

**Interfaces:**
- Produces: `decideRailVisibility(prev: RailScrollState, next: {scrollTop: number; isOpen: boolean}): RailScrollState`

Syncfusion has no hide-on-scroll, so this is ours. Putting the decision in a pure function is the only way it gets a real test.

- [ ] **Step 1: Write the failing test**

```ts
import {describe, expect, it} from 'vitest'
import {decideRailVisibility, initialRailScrollState} from './railVisibility'

describe('decideRailVisibility', () => {
  it('hides after a downward flick past the threshold', () => {
    const s = decideRailVisibility({...initialRailScrollState, lastY: 100}, {scrollTop: 200, isOpen: false})
    expect(s.hidden).toBe(true)
  })

  it('shows again on an upward flick', () => {
    const s = decideRailVisibility({hidden: true, lastY: 200}, {scrollTop: 100, isOpen: false})
    expect(s.hidden).toBe(false)
  })

  it('ignores jitter below the threshold', () => {
    const s = decideRailVisibility({hidden: false, lastY: 100}, {scrollTop: 104, isOpen: false})
    expect(s.hidden).toBe(false)
    expect(s.lastY).toBe(100)   // the anchor does not move on jitter
  })

  it('never hides while the dial is open', () => {
    const s = decideRailVisibility({hidden: false, lastY: 100}, {scrollTop: 400, isOpen: true})
    expect(s.hidden).toBe(false)
  })

  it('does not hide within the first 40px of the page', () => {
    const s = decideRailVisibility({hidden: false, lastY: 0}, {scrollTop: 30, isOpen: false})
    expect(s.hidden).toBe(false)
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd frontend && npx vitest run src/shared/lib/railVisibility.test.ts`
Expected: FAIL — the module does not exist.

- [ ] **Step 3: Write it**

```ts
export interface RailScrollState { hidden: boolean; lastY: number }
export const initialRailScrollState: RailScrollState = {hidden: false, lastY: 0}

const JITTER = 8       // ignore anything smaller than a real flick
const FLOOR = 40       // never hide at the very top of the page

/**
 * menunest-192's two guards live here: the rail never hides while the dial is
 * open, and a small wobble never moves it. The idle-return timer belongs to the
 * component, since it is a timeout rather than a decision.
 */
export function decideRailVisibility(
  prev: RailScrollState,
  next: {scrollTop: number; isOpen: boolean},
): RailScrollState {
  if (next.isOpen) return {hidden: false, lastY: next.scrollTop}

  const dy = next.scrollTop - prev.lastY
  if (Math.abs(dy) <= JITTER) return prev

  const hidden = dy > 0 && next.scrollTop > FLOOR
  return {hidden, lastY: next.scrollTop}
}
```

- [ ] **Step 4: Wire it into the rail**

In `ShortcutRail`, listen on `window` scroll (passive), feed `decideRailVisibility`, toggle a `.is-hidden` class carrying `transform: translateY(96px) scale(.9); opacity: 0;` over 220ms — the mock's exact values — and set an ~900ms idle timer that calls show. Respect `prefers-reduced-motion`.

- [ ] **Step 5: Verify and commit**

Run: `cd frontend && npx tsc -b && npm run build && npx vitest run`

```bash
git add frontend/src/shared/lib/railVisibility.ts \
        frontend/src/shared/lib/railVisibility.test.ts \
        frontend/src/shared/components/ShortcutRail.tsx \
        frontend/src/pages/budget/BudgetPage.css
git commit -m "feat(budget): the rail hides on scroll and returns on idle (#106)"
```

---

### Task 5: The keyboard bindings

**Files:**
- Create: `frontend/src/shared/lib/keyBinding.ts` + `.test.ts`
- Modify: `frontend/src/shared/components/ShortcutRail.tsx`

**Interfaces:**
- Produces: `classifyUndoKey(e: {key: string; metaKey: boolean; ctrlKey: boolean; shiftKey: boolean}, ctx: {inEditable: boolean; dialogOpen: boolean}): 'undo' | 'redo' | 'ignore'`

- [ ] **Step 1: Write the failing test**

```ts
import {describe, expect, it} from 'vitest'
import {classifyUndoKey} from './keyBinding'

const free = {inEditable: false, dialogOpen: false}

describe('classifyUndoKey', () => {
  it('treats Ctrl+Z as undo', () => {
    expect(classifyUndoKey({key: 'z', ctrlKey: true, metaKey: false, shiftKey: false}, free)).toBe('undo')
  })

  it('treats Cmd+Z as undo', () => {
    expect(classifyUndoKey({key: 'z', ctrlKey: false, metaKey: true, shiftKey: false}, free)).toBe('undo')
  })

  it('treats Cmd+Shift+Z as redo', () => {
    expect(classifyUndoKey({key: 'z', ctrlKey: false, metaKey: true, shiftKey: true}, free)).toBe('redo')
  })

  it('ignores a bare z', () => {
    expect(classifyUndoKey({key: 'z', ctrlKey: false, metaKey: false, shiftKey: false}, free)).toBe('ignore')
  })

  it('ignores the binding inside a text field, so the browser undo wins', () => {
    expect(classifyUndoKey({key: 'z', ctrlKey: true, metaKey: false, shiftKey: false},
      {inEditable: true, dialogOpen: false})).toBe('ignore')
  })

  it('ignores the binding while a dialog is open', () => {
    expect(classifyUndoKey({key: 'z', ctrlKey: true, metaKey: false, shiftKey: false},
      {inEditable: false, dialogOpen: true})).toBe('ignore')
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd frontend && npx vitest run src/shared/lib/keyBinding.test.ts`

- [ ] **Step 3: Write it**

```ts
/**
 * menunest-200: Ctrl+Z and Cmd+Z both, but INERT inside an editable — the
 * browser's own undo is what a person pressing it there expects, and getting
 * it wrong moves money when they wanted their typing back — and INERT while a
 * budget dialog is open, because the dialog shows figures the undo would move
 * underneath it.
 */
export function classifyUndoKey(
  e: {key: string; metaKey: boolean; ctrlKey: boolean; shiftKey: boolean},
  ctx: {inEditable: boolean; dialogOpen: boolean},
): 'undo' | 'redo' | 'ignore' {
  if (ctx.inEditable || ctx.dialogOpen) return 'ignore'
  if (e.key.toLowerCase() !== 'z') return 'ignore'
  if (!e.metaKey && !e.ctrlKey) return 'ignore'
  return e.shiftKey ? 'redo' : 'undo'
}
```

- [ ] **Step 4: Wire it up**

Add a `keydown` listener on `window` inside `ShortcutRail`. Compute the context at event time:

```ts
const el = document.activeElement as HTMLElement | null
const inEditable = !!el && (
  el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable
)
// menunest-200 left this to the build. The DOM check is chosen over having
// each dialog register: the five budget dialogs are local useState in five
// components, and a guard that needs no edit to any of them is less to keep
// in step. Swap to registration if a dialog ever renders without this class.
const dialogOpen = !!document.querySelector('.budget-modal-overlay')
```

Call `e.preventDefault()` only when the result is not `'ignore'`.

- [ ] **Step 5: Verify and commit**

Run: `cd frontend && npx tsc -b && npm run build && npx vitest run`

```bash
git add frontend/src/shared/lib/keyBinding.ts \
        frontend/src/shared/lib/keyBinding.test.ts \
        frontend/src/shared/components/ShortcutRail.tsx
git commit -m "feat(budget): Ctrl+Z and Cmd+Z on the budget page (#106)"
```

---

### Task 6: The Change history sheet

**Files:**
- Create: `frontend/src/pages/budget/components/ChangeHistorySheet.tsx`
- Create: `frontend/src/pages/budget/lib/changeRowLabel.ts` + `.test.ts`
- Modify: `frontend/src/pages/budget/BudgetPage.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.css`

**Interfaces:**
- Consumes: `useListBudgetHistoryQuery`, `useUndoBudgetChangeMutation`, `useRedoBudgetChangeMutation`.
- Produces: `describeChange(row: BudgetChangeDto): string` — the one-line human description of a row.

menunest-195: a sheet on the existing `budget-modal-overlay` / `budget-modal` scaffolding, **every row carrying its own Undo and Redo**, and **an undone row STAYS**, marked, so it can be redone.

- [ ] **Step 1: Write the failing test for the row label**

```ts
import {describe, expect, it} from 'vitest'
import {describeChange} from './changeRowLabel'
import type {BudgetChangeDto} from '../../../shared/api/api'

const base: BudgetChangeDto = {
  id: '1', userId: 'u', userDisplayName: 'ทศพล', kind: 'Assign', batchId: null,
  categoryName: 'ค่ากิน', secondCategoryName: null, delta: 300, flagValue: null,
  isUndone: false, undoneByDisplayName: null, createdAt: '2026-08-20T00:00:00Z',
  canUndo: true, blockedReason: null,
}

describe('describeChange', () => {
  it('describes an assign', () => {
    expect(describeChange(base)).toContain('ค่ากิน')
    expect(describeChange(base)).toContain('300')
  })

  it('describes a move with both envelopes', () => {
    const s = describeChange({...base, kind: 'Move', secondCategoryName: 'ค่าไฟ', delta: -200})
    expect(s).toContain('ค่ากิน')
    expect(s).toContain('ค่าไฟ')
  })

  it('describes an everyday mark by its new value', () => {
    expect(describeChange({...base, kind: 'EverydayMark', delta: 0, flagValue: true}))
      .toContain('ค่ากิน')
  })

  it('names a quick-assign batch as one act', () => {
    expect(describeChange({...base, batchId: 'b1'})).toContain('แจกเงิน')
  })
})
```

- [ ] **Step 2: Run it, write `describeChange`, run again**

Run: `cd frontend && npx vitest run src/pages/budget/lib/changeRowLabel.test.ts`

- [ ] **Step 3: Write the sheet**

Reuse the exact markup shape of `EverydayMarksSheet.tsx`: a `.budget-modal-overlay` that closes on backdrop click, containing a `.budget-modal`. Inside, one row per change with:

- `describeChange(row)` as the text, and `row.userDisplayName` beneath it;
- an **Undo** button when `!row.isUndone`, a **Redo** button when `row.isUndone`;
- both **disabled** when `!row.canUndo`, with `row.blockedReason` shown as the reason (menunest-197 — the row stays, greyed, saying why);
- `data-testid="bdg-history-row"` on each row and `bdg-history-sheet` on the container.

Group rows sharing a `batchId` into a single row — one quick-assign press is one entry (menunest-196). Put that grouping in `changeRowLabel.ts` as a pure `groupByBatch(rows)` with its own test.

- [ ] **Step 4: Open it from the rail**

`BudgetPage` already holds `historyOpen` from Task 3; render `{historyOpen && <ChangeHistorySheet onClose={() => setHistoryOpen(false)} />}`.

- [ ] **Step 5: Verify and commit**

Run: `cd frontend && npx tsc -b && npm run build && npx vitest run`

```bash
git add frontend/src/pages/budget/components/ChangeHistorySheet.tsx \
        frontend/src/pages/budget/lib/changeRowLabel.ts \
        frontend/src/pages/budget/lib/changeRowLabel.test.ts \
        frontend/src/pages/budget/BudgetPage.tsx \
        frontend/src/pages/budget/BudgetPage.css
git commit -m "feat(budget): the change history sheet (#106)"
```

---

### Task 7: One quick-assign press is one history row

**Files:**
- Modify: `frontend/src/pages/budget/components/QuickAssignDialog.tsx`

- [ ] **Step 1: Send one batch id for the whole press**

At line ~122 the dialog loops `for (const a of plan) { await setAssigned({...}) }`. Generate one id before the loop and pass it on every call:

```tsx
      // menunest-196: one press is ONE history row, so every write in this
      // plan carries the same batch id. NOTE: this loop is still not atomic —
      // a failure at request 7 of 12 leaves the user half-assigned, which is a
      // PRE-EXISTING defect recorded out of scope on the decision map, not
      // something undo introduced.
      const batchId = crypto.randomUUID()
      for (const a of plan) {
        await setAssigned({
          ...,
          batchId,
        })
      }
```

- [ ] **Step 2: Verify and commit**

Run: `cd frontend && npx tsc -b && npm run build`

```bash
git add frontend/src/pages/budget/components/QuickAssignDialog.tsx
git commit -m "feat(budget): one quick-assign press is one history row (#106)"
```

---

### Task 8: The Playwright smoke spec

**Files:**
- Create: `frontend/e2e/budget.shortcut-rail.spec.ts`

Per CLAUDE.md this is **the only automatic gate that can catch a rendering bug**, and only for what it exercises. #97 shipped a broken page precisely because no spec touched it.

- [ ] **Step 1: Write the spec**

```ts
import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

test.describe('Budget — shortcut rail', () => {
  test('the rail renders on /budget', async ({authedPage: page}) => {
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-page')).toBeVisible()
    await expect(page.getByTestId('bdg-rail')).toBeVisible()
  })

  test('pressing the rail expands it to three actions', async ({authedPage: page}) => {
    await page.goto('/budget')
    await page.getByTestId('bdg-rail').getByRole('button').first().click()
    await expect(page.getByText('Undo')).toBeVisible()
    await expect(page.getByText('Redo')).toBeVisible()
    await expect(page.getByText('Change history')).toBeVisible()
  })

  test('Change history opens a sheet', async ({authedPage: page}) => {
    await page.goto('/budget')
    await page.getByTestId('bdg-rail').getByRole('button').first().click()
    await page.getByText('Change history').click()
    await expect(page.getByTestId('bdg-history-sheet')).toBeVisible()
  })

  test('the rail does NOT render on the account detail page', async ({authedPage: page}) => {
    // menunest-199: opt-in means AccountDetailPage declares nothing, so the
    // .bdg-fab corner stays uncontested.
    await page.goto('/budget')
    const firstCard = page.getByTestId('bdg-account-card').first()
    if (await firstCard.count() === 0) test.skip()
    await firstCard.click()
    await expect(page.getByTestId('bdg-account-page')).toBeVisible()
    await expect(page.getByTestId('bdg-rail')).toHaveCount(0)
    await expect(page.getByTestId('bdg-fab')).toBeVisible()
  })
})
```

- [ ] **Step 2: Run it**

Run: `cd frontend && npx playwright test e2e/budget.shortcut-rail.spec.ts`
Expected: PASS. If the SpeedDial's DOM differs from the selectors above, fix the **selectors**, not the component.

- [ ] **Step 3: Commit**

```bash
git add frontend/e2e/budget.shortcut-rail.spec.ts
git commit -m "test(budget): playwright smoke spec for the shortcut rail (#106)"
```

---

### Task 9: Check it on a real phone, then ship

**Files:** none.

**Do not skip this and do not do it after pushing.** CLAUDE.md records two features that passed every automated gate and shipped visibly wrong (#36, #46), and one where the whole page rendered unstyled (#97). Prod deploys on push.

- [ ] **Step 1: Run the app and open it on your phone**

```bash
cd frontend && npm run dev -- --host
```

Open the printed LAN address on the phone, on the same network.

- [ ] **Step 2: Walk the mock, state by state**

Open `docs/mocks/budget-shortcut-rail-mock.html` beside it and compare:

- **Resting** — one button, bottom-right, 52px, indigo, the shadow present.
- **Expanded** — three items rising vertically; **Undo nearest the thumb**; labels to the left; the scrim dimming the page.
- **Hidden on scroll** — flick down through the envelopes: the rail drops away. Flick up, or stop for about a second: it returns. Open the dial and scroll: **it must not hide**.
- **Desktop** — at a desktop width the labels read `Undo ⌘Z` (or `Ctrl+Z`). Press `Cmd+Z` on the page: undo fires. Click into an envelope's amount field and press `Cmd+Z`: **the browser's own undo runs, not ours**. Open Move money and press it: **nothing happens**.
- **Change history** — the sheet opens; rows name who made each change; an undone row stays with a Redo.

- [ ] **Step 3: Diff the CSS against the mock's spec table**

Open devtools on the rail and check the values in the mock's table one by one: 52/44px, `right:16px`, `bottom:22px`, gaps 10/12px, both shadows, the scrim alpha. The review gates are blind to this; you are the gate.

- [ ] **Step 4: Only then, push**

```bash
git push origin <branch>
```

Open a PR, or merge if that is the agreed flow. Watch the three workflows (CI, Azure deploy, Playwright E2E) go green before calling it shipped.

- [ ] **Step 5: Verify in prod**

Open `/budget` in prod on the phone. Assign money to an envelope, press Undo on the rail, and confirm the number moves back. Then open Change history and confirm the row is there, marked undone, with a Redo.

---

## Self-Review

**Spec coverage.** menunest-191 (three slots, that order) — Task 3. menunest-192 (bottom-right, expand up, not draggable, hide on scroll + both guards) — Tasks 3 and 4; *not draggable* is covered by never writing drag code. menunest-195 (sheet, per-row undo/redo, undone rows stay) — Task 6. menunest-196 (one press, one row) — Task 7 plus Task 6's `groupByBatch`. menunest-197 (dead rows disabled with a reason) — Task 6, driven by `canUndo`/`blockedReason` from the API. menunest-199 (provider, opt-in, no rail on account-detail) — Tasks 2 and 8's fourth test. menunest-200 (both modifiers, both inert cases, desktop hints) — Task 5. menunest-201 (the head's wider reach) — nothing to do here: the backend decides, and the button simply succeeds or fails.

**The weakest point is Task 3's Syncfusion prop names.** They come from vendor documentation rather than from the installed typings, and the step says so explicitly. An executor who finds them wrong should follow the typings.

**What no automated gate covers:** whether the thing looks right. Task 9 is the gate, it is manual, and the plan places it before the push rather than after for that reason.
