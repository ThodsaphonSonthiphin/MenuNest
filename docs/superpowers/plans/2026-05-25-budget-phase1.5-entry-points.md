# Budget Phase 1.5 — Add Entry Points

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Restore four create-side entry points the main redesign dropped: **+ Add Category** on each group header, **+ Add Group** at the bottom of the envelope list, **Set Monthly Income** by tapping the RTA hero, and **Reconcile balance** from the account-detail `⋯` menu.

**Architecture:** Pure frontend — three new dialog components plus button/menu wiring in existing components. Backend already supports all four flows: `useCreateBudgetGroupMutation`, `useCreateBudgetCategoryMutation`, `useSetMonthlyIncomeMutation`, `useCreateBudgetTransactionMutation` (used as the "adjustment transaction" for reconcile). No new endpoints, no new domain code.

**Tech Stack:** React 19, Syncfusion react-inputs / react-buttons (existing dialog pattern), React Hook Form, RTK Query, Playwright for smoke tests.

**Mock:** [docs/mocks/budget-add-entry-points-mock.html](../../mocks/budget-add-entry-points-mock.html)

---

## File Structure

### Create

- `frontend/src/pages/budget/components/AddGroupDialog.tsx`
- `frontend/src/pages/budget/components/SetIncomeDialog.tsx`
- `frontend/src/pages/budget/components/ReconcileBalanceDialog.tsx`
- `frontend/e2e/budget.add-entry-points.spec.ts`

### Modify

- `frontend/src/pages/budget/components/EnvelopeList.tsx` — `+ Cat` button on each group header (opens AddCategoryDialog with the group pre-selected); `+ Add Group` button at the bottom (opens AddGroupDialog)
- `frontend/src/pages/budget/components/AddCategoryDialog.tsx` — accept optional `presetGroupId?: string` so the group dropdown comes pre-filled when invoked from a header
- `frontend/src/pages/budget/components/RtaHero.tsx` — render an inline edit ✎ pill, wire onClick to a callback prop so the parent can open the dialog
- `frontend/src/pages/budget/BudgetPage.tsx` — wire RTA hero onClick → SetIncomeDialog
- `frontend/src/pages/budget/account-detail/AccountDetailPage.tsx` — replace the empty `⋯` placeholder with a real menu (Reconcile / Edit / Close); render ReconcileBalanceDialog
- `frontend/src/pages/budget/BudgetPage.css` — minimal additions: `.bdg-add-cat-btn`, `.bdg-add-group-btn`, `.bdg-rta-hero` pointer cursor + edit-icon, `.bdg-menu-pop`, `.bdg-menu-item`

### Out of scope

- AddAccountDialog already has its `+ Add` card — untouched.
- ReconcileBalanceDialog uses the existing `createBudgetTransaction` mutation; we do NOT add a new endpoint.
- "Edit account" and "Close account" actions are shown in the `⋯` menu but their implementation is Phase-2 (just open the existing dialogs or no-op for v1). Spec for this plan is: render them disabled with a "soon" hint if no dialog wired.

---

## Conventions

- Dialog DOM matches the existing five dialogs: top-level `.budget-modal-overlay` + child `.budget-modal` (these classes were restored in commit `b0199a4` after T24 broke them).
- Submit pattern: `react-hook-form` + `Controller` wrapping Syncfusion `TextBox` / `NumericTextBox`.
- After a successful mutation, call `onClose()`. Errors render via `getErrorMessage` into a `.field-error` `<p>`.
- Test IDs: `bdg-add-group-dialog`, `bdg-set-income-dialog`, `bdg-reconcile-dialog`, `bdg-add-cat-btn`, `bdg-add-group-btn`, `bdg-account-menu`, `bdg-menu-reconcile`.
- Commits: one per task; backend pre-commit hook still runs on every commit.

---

## Task P1: AddGroupDialog

**Files:**
- Create: `frontend/src/pages/budget/components/AddGroupDialog.tsx`

- [ ] **Step 1: Create the component**

```tsx
import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {TextBox} from '@syncfusion/react-inputs'
import {useCreateBudgetGroupMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

interface FormValues { name: string }

export function AddGroupDialog({onClose}: {onClose: () => void}) {
  const [create, {isLoading}] = useCreateBudgetGroupMutation()
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, formState} = useForm<FormValues>({defaultValues: {name: ''}})

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    try {
      await create({name: values.name.trim()}).unwrap()
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  })

  return (
    <div
      className="budget-modal-overlay"
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
      data-testid="bdg-add-group-dialog"
    >
      <form className="budget-modal" onSubmit={onSubmit} noValidate>
        <h3>Add Group</h3>
        <div className="subtitle">A group bundles related envelopes (e.g. "Bills", "Fun").</div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Name</div>
          <Controller
            control={control}
            name="name"
            rules={{
              required: 'Name is required.',
              maxLength: {value: 120, message: 'Max 120 characters.'},
              validate: v => v.trim().length > 0 || 'Name is required.',
            }}
            render={({field}) => (
              <TextBox
                value={field.value}
                placeholder="e.g. Bills"
                onChange={e => field.onChange(e.value ?? '')}
              />
            )}
          />
          {formState.errors.name && <p className="field-error">{formState.errors.name.message}</p>}
        </div>

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isLoading}>
            {isLoading ? '…' : 'Create'}
          </Button>
        </div>
      </form>
    </div>
  )
}
```

- [ ] **Step 2: Build**

```bash
cd frontend && npm run build
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/budget/components/AddGroupDialog.tsx
git commit -m "feat(budget): add AddGroupDialog (name-only)"
```

---

## Task P2: SetIncomeDialog

**Files:**
- Create: `frontend/src/pages/budget/components/SetIncomeDialog.tsx`

- [ ] **Step 1: Create the component**

```tsx
import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {NumericTextBox} from '@syncfusion/react-inputs'
import {useAppSelector} from '../../../store'
import {useSetMonthlyIncomeMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

const MONTHS = ['January','February','March','April','May','June','July','August','September','October','November','December']

interface FormValues { amount: number | null }

export function SetIncomeDialog({
  currentAmount,
  onClose,
}: {
  currentAmount: number
  onClose: () => void
}) {
  const {year, month} = useAppSelector(s => s.budget)
  const [setIncome, {isLoading}] = useSetMonthlyIncomeMutation()
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, formState} = useForm<FormValues>({defaultValues: {amount: currentAmount}})

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    try {
      await setIncome({year, month, amount: Number(values.amount ?? 0)}).unwrap()
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  })

  return (
    <div
      className="budget-modal-overlay"
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
      data-testid="bdg-set-income-dialog"
    >
      <form className="budget-modal" onSubmit={onSubmit} noValidate>
        <h3>Monthly income — {MONTHS[month - 1]} {year}</h3>
        <div className="subtitle">All money you expect to receive this month, before assigning to envelopes.</div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Amount (THB)</div>
          <Controller
            control={control}
            name="amount"
            rules={{validate: v => (v != null && Number(v) >= 0) || 'Must be 0 or more.'}}
            render={({field}) => (
              <NumericTextBox
                min={0}
                value={field.value ?? null}
                onChange={e => field.onChange((e.value as number | null) ?? null)}
              />
            )}
          />
          {formState.errors.amount && <p className="field-error">{formState.errors.amount.message}</p>}
        </div>

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isLoading}>
            {isLoading ? '…' : 'Save'}
          </Button>
        </div>
      </form>
    </div>
  )
}
```

- [ ] **Step 2: Build + commit**

```bash
cd frontend && npm run build
git add frontend/src/pages/budget/components/SetIncomeDialog.tsx
git commit -m "feat(budget): add SetIncomeDialog (uses useSetMonthlyIncomeMutation)"
```

---

## Task P3: ReconcileBalanceDialog

**Files:**
- Create: `frontend/src/pages/budget/components/ReconcileBalanceDialog.tsx`

- [ ] **Step 1: Create the component**

```tsx
import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {NumericTextBox} from '@syncfusion/react-inputs'
import {useCreateBudgetTransactionMutation} from '../../../shared/api/api'
import {useAppSelector} from '../../../store'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {formatTHB} from '../BudgetPage.hooks'

interface FormValues { actualBalance: number | null }

function todayIso(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * Reconcile dialog — user enters the true bank-side balance.
 * We compute (actual − tracked) and post a single adjustment
 * transaction (categoryId=null, amount=diff) so the account's
 * running balance lines up with reality. No new backend.
 */
export function ReconcileBalanceDialog({
  accountId,
  trackedBalance,
  onClose,
}: {
  accountId: string
  trackedBalance: number
  onClose: () => void
}) {
  const {year, month} = useAppSelector(s => s.budget)
  const [create, {isLoading}] = useCreateBudgetTransactionMutation()
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, watch, formState} = useForm<FormValues>({
    defaultValues: {actualBalance: trackedBalance},
  })
  const actual = watch('actualBalance')
  const diff = actual == null ? 0 : Number(actual) - trackedBalance

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    if (values.actualBalance == null) { setErr('Enter the actual balance.'); return }
    if (diff === 0) { onClose(); return } // nothing to adjust
    try {
      await create({
        accountId,
        categoryId: null,
        amount: diff,
        date: todayIso(),
        notes: 'Manual balance fix',
        year, month,
      }).unwrap()
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  })

  return (
    <div
      className="budget-modal-overlay"
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
      data-testid="bdg-reconcile-dialog"
    >
      <form className="budget-modal" onSubmit={onSubmit} noValidate>
        <h3>Reconcile balance</h3>
        <div className="subtitle">
          Enter what your bank actually shows. We'll post a single adjustment transaction to make our running balance match.
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Tracked here</div>
          <div style={{fontSize: 15, fontWeight: 700}}>{formatTHB(trackedBalance)}</div>
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Actual balance (bank)</div>
          <Controller
            control={control}
            name="actualBalance"
            rules={{validate: v => v != null || 'Required.'}}
            render={({field}) => (
              <NumericTextBox
                value={field.value ?? null}
                onChange={e => field.onChange((e.value as number | null) ?? null)}
              />
            )}
          />
          {formState.errors.actualBalance && (
            <p className="field-error">{formState.errors.actualBalance.message}</p>
          )}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Adjustment</div>
          <div style={{fontSize: 14, color: diff === 0 ? 'var(--text-muted)' : diff > 0 ? 'var(--green)' : 'var(--red)'}}>
            {diff > 0 ? '+' : ''}{formatTHB(diff)}
            {diff !== 0 && <span style={{fontSize: 11, color: 'var(--text-muted)', marginLeft: 8}}>
              · creates "Manual balance fix" transaction
            </span>}
          </div>
        </div>

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isLoading}>
            {isLoading ? '…' : diff === 0 ? 'No change' : 'Save adjustment'}
          </Button>
        </div>
      </form>
    </div>
  )
}
```

- [ ] **Step 2: Build + commit**

```bash
cd frontend && npm run build
git add frontend/src/pages/budget/components/ReconcileBalanceDialog.tsx
git commit -m "feat(budget): add ReconcileBalanceDialog (posts adjustment tx)"
```

---

## Task P4: + Add Category button on each group header

Wire a button on each `EnvelopeList` group header that opens `AddCategoryDialog` with the group pre-selected. Requires extending `AddCategoryDialog` to accept `presetGroupId`.

**Files:**
- Modify: `frontend/src/pages/budget/components/AddCategoryDialog.tsx`
- Modify: `frontend/src/pages/budget/components/EnvelopeList.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.css`

- [ ] **Step 1: AddCategoryDialog accepts presetGroupId**

In `AddCategoryDialog.tsx`, add the optional prop and use it as the form default:

```tsx
export function AddCategoryDialog({
  onClose,
  presetGroupId,
}: {
  onClose: () => void
  presetGroupId?: string
}) {
  // … existing code …
  const {control, handleSubmit, formState, watch} = useForm<FormValues>({
    defaultValues: {
      groupId: presetGroupId ?? '',
      name: '',
      emoji: '',
      targetType: 'None',
      targetAmount: null,
      targetDueDate: '',
      targetDayOfMonth: null,
    },
  })
```

(Read the current file first; this is a small surgical edit — only the props signature and the `defaultValues.groupId` line change.)

- [ ] **Step 2: EnvelopeList renders + Cat per group header**

Re-import `AddCategoryDialog` (removed in commit `b459566`) and add a new state hook:

```tsx
const [addCatGroupId, setAddCatGroupId] = useState<string | null>(null)
```

In the JSX, expand each group's header to include the button:

```tsx
<div className="bdg-env-group-header">
  <span>{g.name}</span>
  <span className="bdg-env-group-actions">
    <span>{formatTHB(g.totalAssigned)} / {formatTHB(g.totalAvailable)}</span>
    <button
      type="button"
      className="bdg-add-cat-btn"
      data-testid="bdg-add-cat-btn"
      onClick={() => setAddCatGroupId(g.groupId)}
    >+ Cat</button>
  </span>
</div>
```

After the four existing dialog mount blocks, add:

```tsx
{addCatGroupId && (
  <AddCategoryDialog
    presetGroupId={addCatGroupId}
    onClose={() => setAddCatGroupId(null)}
  />
)}
```

- [ ] **Step 3: CSS for + Cat button**

Append to `BudgetPage.css`:

```css
.bdg-env-group-actions { display: inline-flex; align-items: center; gap: 8px; }
.bdg-add-cat-btn {
  background: var(--accent-soft); color: var(--accent);
  border: 1px solid var(--accent); border-radius: 6px;
  padding: 2px 8px; font-size: 10px; font-weight: 700; cursor: pointer;
  text-transform: uppercase; letter-spacing: .4px;
}
```

- [ ] **Step 4: Build + commit**

```bash
cd frontend && npm run build
git add frontend/src/pages/budget/components/AddCategoryDialog.tsx frontend/src/pages/budget/components/EnvelopeList.tsx frontend/src/pages/budget/BudgetPage.css
git commit -m "feat(budget): + Add Category button per group header"
```

---

## Task P5: + Add Group button below the envelope list

**Files:**
- Modify: `frontend/src/pages/budget/components/EnvelopeList.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.css`

- [ ] **Step 1: Import dialog + add state**

```tsx
import {AddGroupDialog} from './AddGroupDialog'
// …
const [addGroupOpen, setAddGroupOpen] = useState(false)
```

- [ ] **Step 2: Render button at end of list**

After the `.map(g => …)` block (immediately before the dialog mount blocks), insert:

```tsx
<button
  type="button"
  className="bdg-add-group-btn"
  data-testid="bdg-add-group-btn"
  onClick={() => setAddGroupOpen(true)}
>＋ Add Group</button>
```

And the conditional render:

```tsx
{addGroupOpen && <AddGroupDialog onClose={() => setAddGroupOpen(false)} />}
```

- [ ] **Step 3: CSS**

Append to `BudgetPage.css`:

```css
.bdg-add-group-btn {
  margin-top: 8px; padding: 12px;
  background: transparent; border: 1px dashed var(--accent);
  color: var(--accent); font-weight: 600; font-size: 13px;
  border-radius: 12px; cursor: pointer;
  display: flex; align-items: center; justify-content: center; gap: 6px;
}
.bdg-add-group-btn:hover { background: var(--accent-soft); }
```

- [ ] **Step 4: Build + commit**

```bash
cd frontend && npm run build
git add frontend/src/pages/budget/components/EnvelopeList.tsx frontend/src/pages/budget/BudgetPage.css
git commit -m "feat(budget): + Add Group button below envelope list"
```

---

## Task P6: RTA hero is tappable → opens SetIncomeDialog

**Files:**
- Modify: `frontend/src/pages/budget/components/RtaHero.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.css`

- [ ] **Step 1: RtaHero accepts an onClick**

```tsx
import {formatTHB} from '../BudgetPage.hooks'
import type {MonthlySummaryDto} from '../../../shared/api/api'

export function RtaHero({
  summary,
  onClick,
}: {
  summary: MonthlySummaryDto
  onClick?: () => void
}) {
  const negative = summary.readyToAssign < 0
  const zero = summary.readyToAssign === 0
  return (
    <button
      type="button"
      className={`bdg-rta-hero ${negative ? 'is-negative' : ''}`}
      data-testid="bdg-rta-hero"
      onClick={onClick}
    >
      <span className="bdg-rta-edit-icon" aria-hidden>✎</span>
      <div className="bdg-rta-label">
        {zero ? 'All Money Assigned' : negative ? 'Over-Assigned' : 'Ready to Assign'}
      </div>
      <div className="bdg-rta-amount" data-testid="bdg-rta-amount">
        {formatTHB(summary.readyToAssign)}
      </div>
      <div className="bdg-rta-sub">
        {formatTHB(summary.income)} income · {formatTHB(summary.totalAssigned)} assigned
      </div>
    </button>
  )
}
```

Note: the root becomes a `<button>` so the entire hero is keyboard-accessible. CSS will neutralize default button styling.

- [ ] **Step 2: BudgetPage wires it up**

```tsx
import {SetIncomeDialog} from './components/SetIncomeDialog'
// …
const [incomeOpen, setIncomeOpen] = useState(false)
// inside JSX, replace <RtaHero summary={summary} /> with:
<RtaHero summary={summary} onClick={() => setIncomeOpen(true)} />
// …
{incomeOpen && (
  <SetIncomeDialog
    currentAmount={summary.income}
    onClose={() => setIncomeOpen(false)}
  />
)}
```

- [ ] **Step 3: CSS — neutralise button styles + edit-icon**

Append to `BudgetPage.css`:

```css
.bdg-rta-hero {
  width: 100%; text-align: left; cursor: pointer;
  border: none; outline: none;
  font-family: inherit; /* needed because we made it a button */
  position: relative;
}
.bdg-rta-hero:focus-visible { outline: 2px solid white; outline-offset: 2px; }
.bdg-rta-edit-icon {
  position: absolute; top: 14px; right: 14px;
  width: 28px; height: 28px; border-radius: 50%;
  background: rgba(255,255,255,0.18);
  display: flex; align-items: center; justify-content: center;
  font-size: 13px; color: white;
}
```

- [ ] **Step 4: Build + commit**

```bash
cd frontend && npm run build
git add frontend/src/pages/budget/components/RtaHero.tsx frontend/src/pages/budget/BudgetPage.tsx frontend/src/pages/budget/BudgetPage.css
git commit -m "feat(budget): tap RTA hero opens SetIncomeDialog"
```

---

## Task P7: Account-detail ⋯ menu + Reconcile wire-up

**Files:**
- Modify: `frontend/src/pages/budget/account-detail/AccountDetailPage.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.css`

- [ ] **Step 1: Replace the empty placeholder with a menu button + popover**

Find the `<span style={{width: 32}} aria-hidden />` in the top-bar of `AccountDetailPage.tsx`. Replace it with a real button + state-controlled popover. Below is the patch — add `useRef` to the imports and a state hook, then update the top bar:

```tsx
import {useEffect, useRef, useState} from 'react'
// …
const [menuOpen, setMenuOpen] = useState(false)
const [reconcileOpen, setReconcileOpen] = useState(false)
const menuRef = useRef<HTMLDivElement | null>(null)

// Close menu when clicking outside.
useEffect(() => {
  if (!menuOpen) return
  function onDoc(e: MouseEvent) {
    if (!menuRef.current?.contains(e.target as Node)) setMenuOpen(false)
  }
  document.addEventListener('mousedown', onDoc)
  return () => document.removeEventListener('mousedown', onDoc)
}, [menuOpen])
```

Replace the empty span at the top-bar right with:

```tsx
<div ref={menuRef} className="bdg-menu-anchor">
  <button
    type="button"
    className="bdg-menu-btn"
    onClick={() => setMenuOpen(o => !o)}
    aria-label="Account menu"
    data-testid="bdg-account-menu"
  >⋯</button>
  {menuOpen && (
    <div className="bdg-menu-pop">
      <button
        type="button"
        className="bdg-menu-item"
        data-testid="bdg-menu-reconcile"
        onClick={() => { setMenuOpen(false); setReconcileOpen(true) }}
      >
        <span className="icon">⚖</span>
        <span>Reconcile balance</span>
      </button>
      <button type="button" className="bdg-menu-item is-disabled" disabled>
        <span className="icon">✎</span>
        <span>Edit account (soon)</span>
      </button>
      <button type="button" className="bdg-menu-item is-disabled" disabled>
        <span className="icon">🗄</span>
        <span>Close account (soon)</span>
      </button>
    </div>
  )}
</div>
```

After the FAB conditional, add:

```tsx
{reconcileOpen && (
  <ReconcileBalanceDialog
    accountId={accountId}
    trackedBalance={account.balance}
    onClose={() => setReconcileOpen(false)}
  />
)}
```

Don't forget the import:

```tsx
import {ReconcileBalanceDialog} from '../components/ReconcileBalanceDialog'
```

- [ ] **Step 2: CSS**

Append to `BudgetPage.css`:

```css
.bdg-menu-anchor { position: relative; }
.bdg-menu-btn {
  width: 32px; height: 32px; border-radius: 50%;
  background: var(--bg-card); border: 1px solid var(--border);
  display: inline-flex; align-items: center; justify-content: center;
  font-size: 18px; color: var(--text); font-weight: 700; cursor: pointer;
}
.bdg-menu-pop {
  position: absolute; top: 38px; right: 0;
  background: var(--bg-card); border: 1px solid var(--border);
  border-radius: 12px; padding: 6px; min-width: 200px;
  box-shadow: 0 8px 32px rgba(0,0,0,0.35); z-index: 20;
  display: flex; flex-direction: column;
}
.bdg-menu-item {
  display: flex; align-items: center; gap: 10px;
  padding: 9px 12px; border-radius: 8px; font-size: 13px;
  color: var(--text); cursor: pointer;
  background: transparent; border: none; text-align: left;
}
.bdg-menu-item:hover:not(.is-disabled) { background: var(--bg-card-2); }
.bdg-menu-item.is-disabled { opacity: 0.5; cursor: not-allowed; }
.bdg-menu-item .icon { font-size: 14px; }
```

- [ ] **Step 3: Build + commit**

```bash
cd frontend && npm run build
git add frontend/src/pages/budget/account-detail/AccountDetailPage.tsx frontend/src/pages/budget/BudgetPage.css
git commit -m "feat(budget): account-detail ⋯ menu + Reconcile balance wire-up"
```

---

## Task P8: Playwright smoke spec

**Files:**
- Create: `frontend/e2e/budget.add-entry-points.spec.ts`

- [ ] **Step 1: Write the spec**

```ts
import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

test.describe('Budget — add entry points', () => {
  test('+ Cat button on group header opens AddCategoryDialog', async ({authedPage: page}) => {
    await page.goto('/budget')
    const btn = page.getByTestId('bdg-add-cat-btn').first()
    if (await btn.count() === 0) test.skip()
    await btn.click()
    await expect(page.locator('.budget-modal h3')).toContainText(/category/i)
  })

  test('+ Add Group button opens AddGroupDialog', async ({authedPage: page}) => {
    await page.goto('/budget')
    const btn = page.getByTestId('bdg-add-group-btn')
    if (await btn.count() === 0) test.skip()
    await btn.click()
    await expect(page.getByTestId('bdg-add-group-dialog')).toBeVisible()
    await expect(page.locator('.budget-modal h3')).toContainText(/group/i)
  })

  test('tap RTA hero opens SetIncomeDialog', async ({authedPage: page}) => {
    await page.goto('/budget')
    await page.getByTestId('bdg-rta-hero').click()
    await expect(page.getByTestId('bdg-set-income-dialog')).toBeVisible()
    await expect(page.locator('.budget-modal h3')).toContainText(/income/i)
  })

  test('Reconcile menu item on account detail opens ReconcileBalanceDialog', async ({authedPage: page}) => {
    await page.goto('/budget')
    const firstAccount = page.getByTestId('bdg-account-card').first()
    if (await firstAccount.count() === 0) test.skip()
    await firstAccount.click()
    await page.getByTestId('bdg-account-menu').click()
    await page.getByTestId('bdg-menu-reconcile').click()
    await expect(page.getByTestId('bdg-reconcile-dialog')).toBeVisible()
    await expect(page.locator('.budget-modal h3')).toContainText(/reconcile/i)
  })
})
```

- [ ] **Step 2: Build + list + commit**

```bash
cd frontend && npm run build
cd frontend && npx playwright test budget.add-entry-points --list
git add frontend/e2e/budget.add-entry-points.spec.ts
git commit -m "test(budget): smoke specs for 4 new entry points"
```

---

## Spec coverage

- ① + Add Category at group header: P4
- ② + Add Group at end of list: P5
- ③ RTA hero → Set Monthly Income: P6
- ④ Account detail ⋯ → Reconcile balance: P7
- Dialogs themselves: P1 (Group), P2 (Income), P3 (Reconcile)
- Playwright smoke for all four: P8

No backend changes; no domain/migration; all four use already-shipped mutations.
