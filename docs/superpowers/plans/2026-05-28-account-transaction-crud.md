# Account Transaction CRUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire frontend UI so users can Edit and Delete transactions on the Account Detail page (`/budget/accounts/:accountId`), with an optimistic delete + 5-second Undo toast.

**Architecture:** Pure frontend wiring inside the budget module. The page owns its list state (the `useAccountDetail` accumulator), so we patch local state directly via three new helpers (`applyEdit` / `applyDelete` / `applyRestore`) and stop the affected RTK Query mutations from invalidating the `BudgetAccountDetail` tag. `BudgetSummary` is still invalidated so the hero balance refreshes. A new `TransactionUndoToast` component owns the 5-second timer and is force-re-mounted with `key` for each new pending delete.

**Tech Stack:** React, TypeScript, RTK Query (existing mutations), Playwright for E2E.

**Spec:** `docs/superpowers/specs/2026-05-26-account-transaction-crud-design.md`

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `frontend/src/shared/api/api.ts` | Modify (lines ~931-940) | Drop `BudgetAccountDetail` from `updateBudgetTransaction` and `deleteBudgetTransaction` `invalidatesTags` |
| `frontend/src/pages/budget/account-detail/AccountDetailPage.hooks.ts` | Modify | Add `applyEdit`, `applyDelete`, `applyRestore` to the returned object; export an `insertSorted` helper |
| `frontend/src/pages/budget/components/TransactionDialog.tsx` | Modify (lines 41-118) | Replace the stub edit path with `useUpdateBudgetTransactionMutation`; accept `onSaved` prop |
| `frontend/src/pages/budget/components/TransactionUndoToast.tsx` | **Create** | Self-contained toast with countdown bar + Undo button; `useEffect` timer; parent re-mounts via `key` |
| `frontend/src/pages/budget/account-detail/AccountTransactionList.tsx` | Modify | Add per-row 3-dot button + popover menu; new `onEdit` / `onDelete` props |
| `frontend/src/pages/budget/account-detail/AccountDetailPage.tsx` | Modify | Wire edit/delete handlers; manage `pending` state; render `TransactionUndoToast`; commit on unmount |
| `frontend/src/pages/budget/BudgetPage.css` | Modify (append) | Styles for `bdg-tx-menu-btn`, `bdg-tx-menu-pop`, `bdg-undo-toast`, progress bar |
| `frontend/e2e/budget.account-tx-crud.spec.ts` | **Create** | Playwright happy-path: edit, delete + undo, delete + commit |

No other files are touched. The plan does not change backend code, schema, or routing.

## Key types and constants (referenced across tasks)

```ts
// Already defined in shared/api/api.ts — for reference only:
interface BudgetTransactionDto {
  id: string
  accountId: string
  categoryId: string | null
  categoryName: string | null
  categoryEmoji: string | null
  amount: number
  date: string         // ISO yyyy-mm-dd
  notes: string | null
  createdAt: string    // ISO datetime
}

useUpdateBudgetTransactionMutation()  // input: {id, year, month, accountId, categoryId, amount, date, notes} → BudgetTransactionDto
useDeleteBudgetTransactionMutation()  // input: {id, year, month} → void
```

```ts
// New, introduced by this plan:
interface PendingDelete {
  tx: BudgetTransactionDto
  timerId: number
}

interface TransactionUndoToastProps {
  message: string
  onUndo: () => void
  onTimeout: () => void
  durationMs?: number   // default 5000
}

// useAccountDetail return additions:
interface AccountDetailHelpers {
  applyEdit: (updated: BudgetTransactionDto) => void
  applyDelete: (id: string) => void
  applyRestore: (tx: BudgetTransactionDto) => void
}
```

---

### Task 1: Drop `BudgetAccountDetail` invalidation from update/delete transaction mutations

**Files:**
- Modify: `frontend/src/shared/api/api.ts` (lines 931-940)

This keeps the per-account transaction list (`listBudgetAccountTransactions`, which provides `BudgetAccountDetail`) from refetching after our optimistic edit/delete, so the local `allItems` accumulator stays in charge of list state. `BudgetTransactions`, `BudgetAccounts`, and `BudgetSummary` invalidations remain so the global tx list, account list, and summary refresh.

- [ ] **Step 1: Edit the two mutation definitions**

Open `frontend/src/shared/api/api.ts` and locate the two mutations near line 931:

```ts
updateBudgetTransaction: build.mutation<BudgetTransactionDto, {id: string; year: number; month: number} & UpdateTransactionRequest>({
    query: ({id, year: _y, month: _m, ...b}) => ({url: `/api/budget/transactions/${id}`, method: 'PUT', body: b}),
    invalidatesTags: (_r, _e, a) => ['BudgetTransactions', 'BudgetAccounts', 'BudgetAccountDetail',
        {type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
}),
deleteBudgetTransaction: build.mutation<void, {id: string; year: number; month: number}>({
    query: ({id}) => ({url: `/api/budget/transactions/${id}`, method: 'DELETE'}),
    invalidatesTags: (_r, _e, a) => ['BudgetTransactions', 'BudgetAccounts', 'BudgetAccountDetail',
        {type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
}),
```

Remove `'BudgetAccountDetail'` from both `invalidatesTags` arrays so they read:

```ts
updateBudgetTransaction: build.mutation<BudgetTransactionDto, {id: string; year: number; month: number} & UpdateTransactionRequest>({
    query: ({id, year: _y, month: _m, ...b}) => ({url: `/api/budget/transactions/${id}`, method: 'PUT', body: b}),
    invalidatesTags: (_r, _e, a) => ['BudgetTransactions', 'BudgetAccounts',
        {type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
}),
deleteBudgetTransaction: build.mutation<void, {id: string; year: number; month: number}>({
    query: ({id}) => ({url: `/api/budget/transactions/${id}`, method: 'DELETE'}),
    invalidatesTags: (_r, _e, a) => ['BudgetTransactions', 'BudgetAccounts',
        {type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
}),
```

Leave `createBudgetTransaction` exactly as it is — Create still relies on `BudgetAccountDetail` invalidation to push the new row into the list.

- [ ] **Step 2: Typecheck and build**

Run:
```
npm --prefix frontend run build
```
Expected: build succeeds. (TS may infer the tag-array type more narrowly without `'BudgetAccountDetail'`; if there's a complaint about the array type, annotate with `as const` after the tag list.)

- [ ] **Step 3: Commit**

```
git add frontend/src/shared/api/api.ts
git commit -m "refactor(budget-api): stop invalidating BudgetAccountDetail on tx update/delete

The account detail page owns its transaction list locally so the
per-account list can stay stable during optimistic delete/undo. The
account hero still refreshes via BudgetSummary invalidation."
```

---

### Task 2: Add `applyEdit`/`applyDelete`/`applyRestore` helpers to `useAccountDetail`

**Files:**
- Modify: `frontend/src/pages/budget/account-detail/AccountDetailPage.hooks.ts`

The page-level pending-delete state and the dialog's edit submit handler both need a way to mutate the `allItems` accumulator. These three helpers wrap that.

- [ ] **Step 1: Define the sort comparator and insert helper**

Edit `AccountDetailPage.hooks.ts`. Add a file-local helper above the `useAccountDetail` function:

```ts
function byDateDescThenCreatedAtDesc(a: BudgetTransactionDto, b: BudgetTransactionDto): number {
  if (a.date !== b.date) return a.date < b.date ? 1 : -1
  if (a.createdAt !== b.createdAt) return a.createdAt < b.createdAt ? 1 : -1
  return 0
}
```

- [ ] **Step 2: Wire the helpers and expose them from the hook**

Inside `useAccountDetail`, just before the `return { ... }`, add:

```ts
const applyEdit = useCallback((updated: BudgetTransactionDto) => {
  setAllItems(prev => {
    const next = prev.map(t => t.id === updated.id ? updated : t)
    next.sort(byDateDescThenCreatedAtDesc)
    return next
  })
}, [])

const applyDelete = useCallback((id: string) => {
  setAllItems(prev => prev.filter(t => t.id !== id))
}, [])

const applyRestore = useCallback((tx: BudgetTransactionDto) => {
  setAllItems(prev => {
    if (prev.some(t => t.id === tx.id)) return prev   // idempotent: already restored
    const next = [...prev, tx]
    next.sort(byDateDescThenCreatedAtDesc)
    return next
  })
}, [])
```

Then update the returned object:

```ts
return {
  account: data?.account ?? null,
  items: allItems,
  isLoading,
  isFetching,
  error,
  endSentinelRef,
  hasMore,
  applyEdit,
  applyDelete,
  applyRestore,
}
```

Also add `useCallback` to the imports at the top of the file:

```ts
import {useCallback, useEffect, useRef, useState} from 'react'
```

- [ ] **Step 3: Typecheck**

Run:
```
npm --prefix frontend run build
```
Expected: build succeeds. No other call sites consume `useAccountDetail` so no fan-out breakage.

- [ ] **Step 4: Commit**

```
git add frontend/src/pages/budget/account-detail/AccountDetailPage.hooks.ts
git commit -m "feat(budget): expose applyEdit/applyDelete/applyRestore from useAccountDetail

Adds three local-state helpers the page will use to keep the transaction
list in sync during optimistic delete/undo and after Edit submits."
```

---

### Task 3: Wire the edit path inside `TransactionDialog`

**Files:**
- Modify: `frontend/src/pages/budget/components/TransactionDialog.tsx`

Replace the stub error with a real call to `useUpdateBudgetTransactionMutation`. The dialog must also accept an `onSaved` callback so the page can patch local state with the server response.

- [ ] **Step 1: Update imports**

At the top of the file, ensure the import block includes `useUpdateBudgetTransactionMutation`:

```ts
import {
  useCreateBudgetTransactionMutation,
  useUpdateBudgetTransactionMutation,
  type BudgetAccountDto,
  type BudgetTransactionDto,
  type EnvelopeGroupDto,
} from '../../../shared/api/api'
```

- [ ] **Step 2: Replace the doc-comment block at lines 41-49**

The current comment claims edit is unsupported. Replace lines 41-49 with a short, accurate block:

```ts
/**
 * Transaction dialog — creates or edits a `BudgetTransaction`. The user
 * enters a positive `amount` together with an Expense/Income toggle; we
 * flip the sign before submission so the backend always receives the
 * canonical signed amount (negative = expense). When `existing` is set,
 * the dialog opens in edit mode and the parent receives the updated DTO
 * via `onSaved`.
 */
```

- [ ] **Step 3: Update the props type and add `onSaved`**

In the function signature, add `onSaved?: (updated: BudgetTransactionDto) => void` to the props:

```ts
export function TransactionDialog({
  accounts,
  groups,
  existing,
  onClose,
  onSaved,
  preset,
}: {
  accounts: BudgetAccountDto[]
  groups: EnvelopeGroupDto[]
  existing?: BudgetTransactionDto
  onClose: () => void
  onSaved?: (updated: BudgetTransactionDto) => void
  preset?: {accountId?: string; categoryId?: string}
}) {
```

- [ ] **Step 4: Wire the update mutation**

Inside the function body, remove the obsolete TODO at line 63 and add the update hook next to the create hook:

```ts
const {year, month} = useAppSelector(s => s.budget)
const [createTx, {isLoading: isCreating}] = useCreateBudgetTransactionMutation()
const [updateTx, {isLoading: isUpdating}] = useUpdateBudgetTransactionMutation()
const isLoading = isCreating || isUpdating
const [err, setErr] = useState<string | null>(null)
```

- [ ] **Step 5: Replace the edit-stub branch in `onSubmit`**

Locate the block that currently reads:

```ts
if (existing) {
  // Reserved for edit-mode work; see TODO above.
  setErr('Editing transactions is not yet supported.')
  return
}
const magnitude = Number(values.amount ?? 0)
const signed = values.direction === 'Expense' ? -magnitude : magnitude
try {
  await createTx({ ... }).unwrap()
  onClose()
} catch (e) {
  setErr(getErrorMessage(e))
}
```

Replace it with:

```ts
const magnitude = Number(values.amount ?? 0)
const signed = values.direction === 'Expense' ? -magnitude : magnitude
try {
  if (existing) {
    const updated = await updateTx({
      id: existing.id,
      year,
      month,
      accountId: values.accountId,
      categoryId: values.categoryId === UNCATEGORIZED_ID ? null : values.categoryId,
      amount: signed,
      date: values.date,
      notes: values.notes.trim() || null,
    }).unwrap()
    onSaved?.(updated)
    onClose()
    return
  }
  await createTx({
    accountId: values.accountId,
    categoryId: values.categoryId === UNCATEGORIZED_ID ? null : values.categoryId,
    amount: signed,
    date: values.date,
    notes: values.notes.trim() || null,
    year,
    month,
  }).unwrap()
  onClose()
} catch (e) {
  setErr(getErrorMessage(e))
}
```

- [ ] **Step 6: Typecheck**

Run:
```
npm --prefix frontend run build
```
Expected: build succeeds.

- [ ] **Step 7: Commit**

```
git add frontend/src/pages/budget/components/TransactionDialog.tsx
git commit -m "feat(budget): wire edit path in TransactionDialog

Replaces the 'Editing transactions is not yet supported.' stub with a
real call to useUpdateBudgetTransactionMutation. Adds an onSaved
callback so callers can patch their local list with the updated DTO."
```

---

### Task 4: Create the `TransactionUndoToast` component

**Files:**
- Create: `frontend/src/pages/budget/components/TransactionUndoToast.tsx`

Self-contained. The parent controls visibility by mounting/unmounting; a `key` prop forces a fresh mount (and a fresh timer) on each new pending delete.

- [ ] **Step 1: Create the component file**

Write `frontend/src/pages/budget/components/TransactionUndoToast.tsx` with the following content:

```tsx
import {useEffect, useRef} from 'react'

interface Props {
  message: string
  onUndo: () => void
  onTimeout: () => void
  durationMs?: number
}

/**
 * Bottom-fixed toast with a 5-second countdown bar and an Undo button.
 * The parent mounts this component when a delete is pending and unmounts
 * it after Undo or after onTimeout fires. To start a fresh countdown for
 * a second pending delete, give the component a new `key`.
 */
export function TransactionUndoToast({message, onUndo, onTimeout, durationMs = 5000}: Props) {
  // Stash the timeout id so the cleanup can clear it. We deliberately keep
  // the effect's dep list empty so the timer is created exactly once per
  // mount — the parent forces a re-mount via `key` when it wants a new
  // countdown.
  const firedRef = useRef(false)

  useEffect(() => {
    const id = window.setTimeout(() => {
      firedRef.current = true
      onTimeout()
    }, durationMs)
    return () => {
      window.clearTimeout(id)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const handleUndo = () => {
    if (firedRef.current) return  // race: onTimeout already fired
    onUndo()
  }

  return (
    <div className="bdg-undo-toast" data-testid="bdg-undo-toast" role="status" aria-live="polite">
      <span className="bdg-undo-toast-msg">{message}</span>
      <button
        type="button"
        className="bdg-undo-toast-btn"
        data-testid="bdg-undo-btn"
        onClick={handleUndo}
      >
        Undo
      </button>
      <div
        className="bdg-undo-toast-bar"
        style={{animationDuration: `${durationMs}ms`}}
        aria-hidden="true"
      />
    </div>
  )
}
```

- [ ] **Step 2: Typecheck**

Run:
```
npm --prefix frontend run build
```
Expected: build succeeds (component is unused but type-clean).

- [ ] **Step 3: Commit**

```
git add frontend/src/pages/budget/components/TransactionUndoToast.tsx
git commit -m "feat(budget): add TransactionUndoToast component

Self-contained 5-second countdown toast with an Undo button. Parent
mounts/unmounts it; re-mount via key starts a fresh countdown."
```

---

### Task 5: Add the 3-dot row menu to `AccountTransactionList`

**Files:**
- Modify: `frontend/src/pages/budget/account-detail/AccountTransactionList.tsx`

Each row gets a `⋯` button. Tapping it opens a small popover with Edit and Delete. Clicking elsewhere or on another row's button closes the open menu.

- [ ] **Step 1: Replace the file contents**

Rewrite `frontend/src/pages/budget/account-detail/AccountTransactionList.tsx`:

```tsx
import {Fragment, useEffect, useRef, useState} from 'react'
import type {BudgetTransactionDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'

interface Props {
  items: BudgetTransactionDto[]
  /** Sentinel for IntersectionObserver — page-end ref. Caller wires it. */
  endSentinelRef: React.RefObject<HTMLDivElement | null>
  onEdit: (tx: BudgetTransactionDto) => void
  onDelete: (tx: BudgetTransactionDto) => void
}

function todayIso(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function dateHeaderFor(iso: string): string {
  const today = todayIso()
  if (iso === today) return `Today · ${formatDateShort(iso)}`
  const yest = new Date(Date.now() - 86400_000)
  const yestIso = `${yest.getFullYear()}-${String(yest.getMonth() + 1).padStart(2, '0')}-${String(yest.getDate()).padStart(2, '0')}`
  if (iso === yestIso) return `Yesterday · ${formatDateShort(iso)}`
  return formatDateShort(iso)
}

function formatDateShort(iso: string): string {
  const [, m, d] = iso.split('-').map(Number)
  const months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec']
  return `${months[m - 1]} ${d}`
}

export function AccountTransactionList({items, endSentinelRef, onEdit, onDelete}: Props) {
  // Bucket by Date — preserves CreatedAt DESC order within each bucket.
  const buckets: {date: string; rows: BudgetTransactionDto[]}[] = []
  for (const tx of items) {
    const last = buckets[buckets.length - 1]
    if (last && last.date === tx.date) last.rows.push(tx)
    else buckets.push({date: tx.date, rows: [tx]})
  }

  const [openMenuId, setOpenMenuId] = useState<string | null>(null)
  const containerRef = useRef<HTMLDivElement | null>(null)

  // Close the open menu when the user clicks anywhere outside any menu.
  useEffect(() => {
    if (!openMenuId) return
    function onDoc(e: MouseEvent) {
      const root = containerRef.current
      if (!root) return
      const target = e.target as Node
      if (!root.contains(target)) {
        setOpenMenuId(null)
        return
      }
      // Click was inside the feed but not on a menu anchor — close.
      const anchor = (target as HTMLElement).closest('.bdg-tx-menu-anchor')
      if (!anchor) setOpenMenuId(null)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [openMenuId])

  return (
    <div ref={containerRef} className="bdg-tx-feed" data-testid="bdg-tx-feed">
      {buckets.map((b) => (
        <Fragment key={b.date}>
          <div className="bdg-tx-date-header">{dateHeaderFor(b.date)}</div>
          {b.rows.map(tx => {
            const isOpen = openMenuId === tx.id
            return (
              <div key={tx.id} className="bdg-tx-row" data-testid="bdg-tx-row" data-tx-id={tx.id}>
                <div className="bdg-tx-icon">{tx.categoryEmoji ?? '•'}</div>
                <div className="bdg-tx-body">
                  <div className="bdg-tx-title">{tx.notes ?? tx.categoryName ?? 'Transaction'}</div>
                  <div className="bdg-tx-sub">{tx.categoryName ?? 'Uncategorized'}</div>
                </div>
                <div className={`bdg-tx-amount ${tx.amount >= 0 ? 'is-income' : ''}`}>
                  {tx.amount >= 0 ? '+' : ''}{formatTHB(tx.amount)}
                </div>
                <div className="bdg-tx-menu-anchor">
                  <button
                    type="button"
                    className="bdg-tx-menu-btn"
                    aria-label="Row menu"
                    data-testid="bdg-tx-menu-btn"
                    onClick={() => setOpenMenuId(isOpen ? null : tx.id)}
                  >
                    ⋯
                  </button>
                  {isOpen && (
                    <div className="bdg-tx-menu-pop" role="menu">
                      <button
                        type="button"
                        className="bdg-tx-menu-item"
                        data-testid="bdg-tx-menu-edit"
                        role="menuitem"
                        onClick={() => { setOpenMenuId(null); onEdit(tx) }}
                      >
                        <span className="icon">✎</span>
                        <span>Edit</span>
                      </button>
                      <button
                        type="button"
                        className="bdg-tx-menu-item is-destructive"
                        data-testid="bdg-tx-menu-delete"
                        role="menuitem"
                        onClick={() => { setOpenMenuId(null); onDelete(tx) }}
                      >
                        <span className="icon">🗑</span>
                        <span>Delete</span>
                      </button>
                    </div>
                  )}
                </div>
              </div>
            )
          })}
        </Fragment>
      ))}
      <div ref={endSentinelRef} className="bdg-tx-sentinel" />
    </div>
  )
}
```

- [ ] **Step 2: Typecheck (the page caller will go red because the new required props aren't passed yet — that's expected)**

Run:
```
npm --prefix frontend run build
```
Expected: TS error at `AccountDetailPage.tsx` saying `onEdit` and `onDelete` are missing on `<AccountTransactionList .../>`. This will be fixed in Task 6. Do **not** commit yet.

- [ ] **Step 3: (No commit at this task boundary — wait for Task 6)**

Because the build is intentionally broken until Task 6 wires the callers, defer the commit. The combined Task 5+6 changes ship in Task 6's commit.

---

### Task 6: Wire delete + undo flow in `AccountDetailPage`

**Files:**
- Modify: `frontend/src/pages/budget/account-detail/AccountDetailPage.tsx`

This task is the choreography hub. It owns the `pending` state, handles row callbacks from Task 5, opens `TransactionDialog` in edit mode, mounts `TransactionUndoToast`, and commits on unmount.

- [ ] **Step 1: Replace the file contents**

Rewrite `frontend/src/pages/budget/account-detail/AccountDetailPage.tsx`:

```tsx
import {useCallback, useEffect, useRef, useState} from 'react'
import {Link, useNavigate, useParams} from 'react-router-dom'
import '../BudgetPage.css'
import {AccountHero} from './AccountHero'
import {AccountTransactionList} from './AccountTransactionList'
import {TransactionDialog} from '../components/TransactionDialog'
import {ReconcileBalanceDialog} from '../components/ReconcileBalanceDialog'
import {TransactionUndoToast} from '../components/TransactionUndoToast'
import {useAccountDetail} from './AccountDetailPage.hooks'
import {useAppDispatch, useAppSelector} from '../../../store'
import {
  api,
  useDeleteBudgetTransactionMutation,
  useGetBudgetSummaryQuery,
  type BudgetTransactionDto,
} from '../../../shared/api/api'

interface PendingDelete {
  tx: BudgetTransactionDto
  timerId: number
}

const UNDO_MS = 5000

export function AccountDetailPage() {
  const {accountId = ''} = useParams<{accountId: string}>()
  const navigate = useNavigate()
  const dispatch = useAppDispatch()
  const {account, items, isLoading, error, endSentinelRef, hasMore, applyEdit, applyDelete, applyRestore} =
    useAccountDetail(accountId)
  const [txOpen, setTxOpen] = useState(false)
  const [editing, setEditing] = useState<BudgetTransactionDto | null>(null)
  const [menuOpen, setMenuOpen] = useState(false)
  const [reconcileOpen, setReconcileOpen] = useState(false)
  const [pending, setPending] = useState<PendingDelete | null>(null)
  const [errorToast, setErrorToast] = useState<string | null>(null)
  const menuRef = useRef<HTMLDivElement | null>(null)

  const [deleteTx] = useDeleteBudgetTransactionMutation()
  const {year, month} = useAppSelector(s => s.budget)
  const {data: summary} = useGetBudgetSummaryQuery({year, month})

  // Account-level top-bar menu outside-click handler (unchanged).
  useEffect(() => {
    if (!menuOpen) return
    function onDoc(e: MouseEvent) {
      if (!menuRef.current?.contains(e.target as Node)) setMenuOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [menuOpen])

  // Mirror `pending` into a ref so the unmount cleanup (which has empty
  // deps and captures its initial closure) can see the latest value.
  const pendingRef = useRef<PendingDelete | null>(null)
  useEffect(() => { pendingRef.current = pending }, [pending])

  // Commit a pending delete (fire-and-forget). Used by the row timer, by
  // single-pending replacement (a second delete while one is pending),
  // and by the unmount cleanup. On API failure we restore the row and
  // surface an error toast.
  const commitPending = useCallback((p: PendingDelete) => {
    window.clearTimeout(p.timerId)
    void deleteTx({id: p.tx.id, year, month}).unwrap().catch(() => {
      applyRestore(p.tx)
      setErrorToast('Could not delete. Restored.')
    })
  }, [deleteTx, year, month, applyRestore])

  // Auto-dismiss the small error toast.
  useEffect(() => {
    if (!errorToast) return
    const id = window.setTimeout(() => setErrorToast(null), 3000)
    return () => window.clearTimeout(id)
  }, [errorToast])

  // Commit any still-pending delete when the page unmounts (e.g. user
  // navigated Back before the 5-second timer ran out). Without this the
  // user thinks the row is gone but the server still has it. We use
  // `dispatch(...)` directly so the request goes out even after React
  // tears the component down.
  useEffect(() => {
    return () => {
      const p = pendingRef.current
      if (!p) return
      window.clearTimeout(p.timerId)
      void dispatch(api.endpoints.deleteBudgetTransaction.initiate({id: p.tx.id, year, month}))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const handleEdit = useCallback((tx: BudgetTransactionDto) => {
    setEditing(tx)
    setTxOpen(true)
  }, [])

  const handleDelete = useCallback((tx: BudgetTransactionDto) => {
    // Single-pending policy: if a delete is already pending, commit it
    // immediately so we only ever have one undoable toast on screen.
    if (pending) commitPending(pending)

    applyDelete(tx.id)
    const timerId = window.setTimeout(() => {
      commitPending({tx, timerId: 0})   // timer already fired; clearTimeout is a no-op
      setPending(null)
    }, UNDO_MS)
    setPending({tx, timerId})
  }, [pending, commitPending, applyDelete])

  const handleUndo = useCallback(() => {
    if (!pending) return
    window.clearTimeout(pending.timerId)
    applyRestore(pending.tx)
    setPending(null)
  }, [pending, applyRestore])

  // The toast's onTimeout fires from inside the component, but our
  // setTimeout above already does the commit. Provide a no-op so the
  // toast component's contract stays simple — the page is the source of
  // truth for the timer (durationMs matches).
  const handleToastTimeout = useCallback(() => {
    // No-op; commit already scheduled by handleDelete's setTimeout.
  }, [])

  if (isLoading && !account) {
    return <div className="bdg-loading">Loading…</div>
  }
  if (error || !account) {
    return (
      <div className="bdg-error">
        <p>Could not load this account.</p>
        <button type="button" onClick={() => navigate('/budget')}>Back to budget</button>
      </div>
    )
  }

  return (
    <div className="bdg-account-page" data-testid="bdg-account-page">
      <div className="bdg-top-bar">
        <Link to="/budget" className="bdg-back-btn" aria-label="Back">‹</Link>
        <div className="bdg-top-bar-title">{account.name}</div>
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
      </div>

      <AccountHero account={account} />

      <div className="bdg-section-title">
        <h3>Transactions · newest first</h3>
      </div>

      <AccountTransactionList
        items={items}
        endSentinelRef={endSentinelRef}
        onEdit={handleEdit}
        onDelete={handleDelete}
      />

      {!hasMore && items.length === 0 && (
        <div className="bdg-tx-empty">No transactions yet.</div>
      )}

      <button
        type="button"
        className="bdg-fab"
        onClick={() => { setEditing(null); setTxOpen(true) }}
        aria-label="Add transaction"
        data-testid="bdg-fab"
      >+</button>

      {txOpen && (
        <TransactionDialog
          accounts={summary?.accounts ?? []}
          groups={summary?.groups ?? []}
          existing={editing ?? undefined}
          preset={editing ? undefined : {accountId}}
          onSaved={(updated) => applyEdit(updated)}
          onClose={() => { setTxOpen(false); setEditing(null) }}
        />
      )}

      {reconcileOpen && (
        <ReconcileBalanceDialog
          accountId={accountId}
          trackedBalance={account.balance}
          onClose={() => setReconcileOpen(false)}
        />
      )}

      {pending && (
        <TransactionUndoToast
          key={pending.tx.id}
          message={`Deleted '${pending.tx.notes ?? pending.tx.categoryName ?? 'transaction'}'`}
          onUndo={handleUndo}
          onTimeout={handleToastTimeout}
          durationMs={UNDO_MS}
        />
      )}

      {errorToast && (
        <div className="bdg-undo-toast is-error" data-testid="bdg-error-toast" role="status">
          <span className="bdg-undo-toast-msg">{errorToast}</span>
        </div>
      )}
    </div>
  )
}
```

Note on the `pendingRef` mirror: the unmount cleanup effect has `[]` deps so it captures the closure once. Mirroring `pending` into `pendingRef` (via the small effect just below the `useRef`) lets the cleanup read the latest pending value at unmount time.

- [ ] **Step 2: Confirm `useAppDispatch` exists in `store/`**

Open `frontend/src/store/index.ts` (or wherever the store is exported from) and verify `useAppDispatch` is exported. If only `useAppSelector` exists, also export `useAppDispatch`:

```ts
import {useDispatch} from 'react-redux'
import type {AppDispatch} from './'
export const useAppDispatch: () => AppDispatch = useDispatch
```

If you have to add it, also update the store's index re-export so `import {useAppDispatch} from '../../../store'` resolves.

- [ ] **Step 3: Build**

Run:
```
npm --prefix frontend run build
```
Expected: build succeeds. The page now compiles cleanly with the new `AccountTransactionList` props.

- [ ] **Step 4: Commit both Task 5 and Task 6 changes together**

```
git add frontend/src/pages/budget/account-detail/AccountTransactionList.tsx \
        frontend/src/pages/budget/account-detail/AccountDetailPage.tsx \
        frontend/src/store/index.ts
git commit -m "feat(budget): edit and delete transactions from account detail

Adds a per-row 3-dot menu (Edit / Delete) to the account transaction
list and wires it into AccountDetailPage. Edit reuses TransactionDialog
in edit mode; Delete is optimistic with a 5-second Undo toast,
single-pending policy, and commit-on-unmount."
```

(Adjust the `git add` list if `store/index.ts` did not need changes in Step 2.)

---

### Task 7: CSS for the row menu, popover, and toast

**Files:**
- Modify: `frontend/src/pages/budget/BudgetPage.css` (append at the end)

- [ ] **Step 1: Append the new styles**

Open `frontend/src/pages/budget/BudgetPage.css` and append at the bottom:

```css
/* ─── Per-row menu on account transaction list ─────────────────── */
.bdg-tx-row { position: relative; }
.bdg-tx-menu-anchor { position: relative; display: inline-flex; align-items: center; margin-left: 8px; }
.bdg-tx-menu-btn {
  background: transparent;
  border: none;
  font-size: 18px;
  line-height: 1;
  padding: 4px 8px;
  cursor: pointer;
  color: var(--bdg-muted, #888);
  border-radius: 6px;
}
.bdg-tx-menu-btn:hover { background: rgba(0,0,0,0.05); }
.bdg-tx-menu-pop {
  position: absolute;
  top: 100%;
  right: 0;
  margin-top: 4px;
  background: var(--bdg-surface, #fff);
  border: 1px solid var(--bdg-border, #e0e0e0);
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0,0,0,0.10);
  min-width: 140px;
  z-index: 30;
  padding: 4px;
}
.bdg-tx-menu-item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 8px 10px;
  background: transparent;
  border: none;
  cursor: pointer;
  text-align: left;
  font-size: 14px;
  border-radius: 6px;
}
.bdg-tx-menu-item:hover { background: rgba(0,0,0,0.05); }
.bdg-tx-menu-item.is-destructive { color: #c0392b; }
.bdg-tx-menu-item.is-destructive:hover { background: rgba(192,57,43,0.08); }
.bdg-tx-menu-item .icon { font-size: 14px; width: 16px; text-align: center; }

/* ─── Undo toast ────────────────────────────────────────────────── */
.bdg-undo-toast {
  position: fixed;
  left: 50%;
  bottom: 24px;
  transform: translateX(-50%);
  background: #232f3e;
  color: #fff;
  border-radius: 10px;
  padding: 10px 14px;
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 260px;
  max-width: calc(100% - 32px);
  z-index: 50;
  box-shadow: 0 6px 18px rgba(0,0,0,0.25);
  overflow: hidden;
}
.bdg-undo-toast-msg { flex: 1; font-size: 14px; }
.bdg-undo-toast-btn {
  background: transparent;
  border: none;
  color: #ffd166;
  font-weight: 600;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 6px;
  font-size: 14px;
}
.bdg-undo-toast-btn:hover { background: rgba(255,255,255,0.08); }
.bdg-undo-toast-bar {
  position: absolute;
  left: 0;
  bottom: 0;
  height: 3px;
  background: #ffd166;
  animation-name: bdg-undo-shrink;
  animation-timing-function: linear;
  animation-fill-mode: forwards;
  width: 100%;
}
.bdg-undo-toast.is-error { background: #8e2a25; }
.bdg-undo-toast.is-error .bdg-undo-toast-bar { display: none; }

@keyframes bdg-undo-shrink {
  from { width: 100%; }
  to { width: 0%; }
}
```

- [ ] **Step 2: Visual smoke-check (optional but recommended)**

If the dev server is running (`npm --prefix frontend run dev`), navigate to `/budget/accounts/<id>` and verify:
- ⋯ shows up at the right edge of each row
- Tapping it opens a small menu with Edit and Delete
- Tapping Delete makes the row disappear and shows a toast at the bottom with a shrinking yellow bar

If no dev server is running, skip this step.

- [ ] **Step 3: Commit**

```
git add frontend/src/pages/budget/BudgetPage.css
git commit -m "style(budget): row menu, popover, and undo toast styles"
```

---

### Task 8: Playwright E2E happy path

**Files:**
- Create: `frontend/e2e/budget.account-tx-crud.spec.ts`

One spec file with three scenarios: edit, delete + undo, delete + commit. All gracefully skip if the test account doesn't yet have transactions seeded — same pattern used by the other budget specs.

- [ ] **Step 1: Write the spec**

Create `frontend/e2e/budget.account-tx-crud.spec.ts` with:

```ts
import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

async function goToAccountWithRows(page: import('@playwright/test').Page): Promise<boolean> {
  await page.goto('/budget')
  const card = page.getByTestId('bdg-account-card').first()
  if (await card.count() === 0) return false
  await card.click()
  await expect(page).toHaveURL(/\/budget\/accounts\/[0-9a-f-]+$/)
  const rows = page.getByTestId('bdg-tx-row')
  return (await rows.count()) > 0
}

test.describe('Budget — account transaction CRUD', () => {
  test('row menu opens with Edit and Delete', async ({authedPage: page}) => {
    if (!await goToAccountWithRows(page)) test.skip()
    const firstRow = page.getByTestId('bdg-tx-row').first()
    await firstRow.getByTestId('bdg-tx-menu-btn').click()
    await expect(page.getByTestId('bdg-tx-menu-edit')).toBeVisible()
    await expect(page.getByTestId('bdg-tx-menu-delete')).toBeVisible()
  })

  test('Edit opens TransactionDialog in edit mode', async ({authedPage: page}) => {
    if (!await goToAccountWithRows(page)) test.skip()
    const firstRow = page.getByTestId('bdg-tx-row').first()
    await firstRow.getByTestId('bdg-tx-menu-btn').click()
    await page.getByTestId('bdg-tx-menu-edit').click()
    await expect(page.locator('.budget-modal h3')).toContainText(/edit/i)
    // Dismiss
    await page.getByRole('button', {name: /Cancel/i}).click()
  })

  test('Delete + Undo restores the row', async ({authedPage: page}) => {
    if (!await goToAccountWithRows(page)) test.skip()
    const rowsBefore = await page.getByTestId('bdg-tx-row').count()
    const firstRow = page.getByTestId('bdg-tx-row').first()
    const firstId = await firstRow.getAttribute('data-tx-id')
    await firstRow.getByTestId('bdg-tx-menu-btn').click()
    await page.getByTestId('bdg-tx-menu-delete').click()
    // Row removed, toast visible.
    await expect(page.locator(`[data-tx-id="${firstId}"]`)).toHaveCount(0)
    await expect(page.getByTestId('bdg-undo-toast')).toBeVisible()
    // Undo
    await page.getByTestId('bdg-undo-btn').click()
    await expect(page.getByTestId('bdg-undo-toast')).toBeHidden()
    await expect(page.getByTestId('bdg-tx-row')).toHaveCount(rowsBefore)
    await expect(page.locator(`[data-tx-id="${firstId}"]`)).toHaveCount(1)
  })

  test('Delete commits after 5 seconds and survives reload', async ({authedPage: page}) => {
    if (!await goToAccountWithRows(page)) test.skip()
    const firstRow = page.getByTestId('bdg-tx-row').first()
    const firstId = await firstRow.getAttribute('data-tx-id')
    await firstRow.getByTestId('bdg-tx-menu-btn').click()
    await page.getByTestId('bdg-tx-menu-delete').click()
    // Wait past the 5s undo window plus a small buffer for the API commit.
    await page.waitForTimeout(5500)
    await page.reload()
    // Wait for the page to settle.
    await expect(page.getByTestId('bdg-account-page')).toBeVisible()
    await expect(page.locator(`[data-tx-id="${firstId}"]`)).toHaveCount(0)
  })
})
```

- [ ] **Step 2: Run the new spec**

Run:
```
npm --prefix frontend run test:e2e -- budget.account-tx-crud.spec.ts
```
Expected: all 4 tests pass (or skip cleanly if the seeded test account has no transactions). If a test fails because the UI doesn't behave as specified, treat that as a real defect — fix the implementation, not the test.

- [ ] **Step 3: Commit**

```
git add frontend/e2e/budget.account-tx-crud.spec.ts
git commit -m "test(budget,e2e): cover row menu, edit, delete+undo, delete+commit"
```

---

### Task 9: Final verification

**Files:**
- (none — verification only)

- [ ] **Step 1: Full frontend build**

Run:
```
npm --prefix frontend run build
```
Expected: succeeds with no TypeScript errors.

- [ ] **Step 2: Lint**

Run:
```
npm --prefix frontend run lint
```
Expected: passes (or matches the project's pre-existing lint baseline).

- [ ] **Step 3: Full Playwright suite**

Run:
```
npm --prefix frontend run test:e2e
```
Expected: all tests pass or skip; no regressions in existing budget specs.

- [ ] **Step 4: Manual smoke (if a dev server is reachable)**

Open `/budget`, tap into an account, exercise:
- Tap ⋯ on a row → Edit → change amount → Save → hero balance and row update
- Tap ⋯ → Delete → Undo → row returns
- Tap ⋯ → Delete → wait 5s → reload → row is gone

If any step misbehaves, return to the relevant task and fix.

- [ ] **Step 5: Commit any cleanup fixes**

If any of the above surfaced small bugs, fix them and commit with a focused message. Otherwise no commit is needed — Task 9 is verification only.

---

## Self-Review Notes

- **Spec coverage**: every section of the spec maps to a task —
  - Scope/Files changed → Tasks 1-8 file table
  - UX flows (Edit) → Task 3 + Task 6
  - UX flows (Delete) → Task 5 + Task 6 + Task 4
  - State machine → Task 6 (`handleDelete`, `handleUndo`, timer, unmount effect)
  - Cache strategy → Task 1 (tag drop) + Task 2 (helpers)
  - Component contracts → Tasks 3, 4, 5
  - Error handling → Task 6 (`errorToast`, catch branches in `commitPending` / `handleDelete` timer)
  - Testing → Task 8 (E2E)
- **Placeholder scan**: no TBD / TODO / "implement later" / vague handler placeholders.
- **Type consistency**: `BudgetTransactionDto`, `PendingDelete`, `applyEdit/applyDelete/applyRestore`, `onSaved`, `onEdit/onDelete` props all match across tasks. `useUpdateBudgetTransactionMutation` is passed `{id, year, month, accountId, categoryId, amount, date, notes}` exactly as defined in `api.ts`.
- **Single-pending policy**: Task 6's `handleDelete` commits any existing `pending` before scheduling the new one.
- **Commit-on-unmount**: handled via the `pendingRef` + empty-deps cleanup effect; uses `dispatch(api.endpoints.deleteBudgetTransaction.initiate(...))` so the request fires even after React tears the component down.
