# Account Transaction CRUD — Design

**Date:** 2026-05-26
**Status:** Design approved, ready for implementation plan
**Scope:** Frontend only. Backend CRUD and RTK Query mutations already exist.

## Problem

The Account Detail page (`/budget/account/:id`) currently lets users **create** and **list** transactions but offers no way to **edit** or **delete** them. The backend (`UpdateTransaction`, `DeleteTransaction` use cases) and the RTK Query mutations (`useUpdateBudgetTransactionMutation`, `useDeleteBudgetTransactionMutation`) are already in place; `TransactionDialog` even accepts an `existing` prop but its edit path is a stub that returns `"Editing transactions is not yet supported."`

This spec wires up the missing UI so the account page supports full CRUD on the transactions it shows.

## Scope

In scope:
- 3-dot menu per transaction row on the account detail page with `Edit` and `Delete` actions
- Edit path through the existing `TransactionDialog` (now backed by the update mutation)
- Optimistic delete with a 5-second `Undo` toast
- Local cache patching in `useAccountDetail` so the infinite-scroll position is preserved
- One E2E happy-path test covering delete + undo + delete + commit

Out of scope:
- Bulk select / multi-delete
- Tap-row-to-edit (FAB and 3-dot menu are the only entry points)
- Swipe gestures on mobile
- Edit / Delete on other transaction views (none exist today)
- Editing of system-generated transactions (none exist today; if introduced later, edit/delete UI may need to filter them out)

## Architecture

The change is contained to the budget module. No new state slices, routes, or backend code.

### Files changed

| File | Change |
|---|---|
| `frontend/src/pages/budget/account-detail/AccountTransactionList.tsx` | Add 3-dot button per row; popover menu with Edit/Delete; new `onEdit`/`onDelete` callback props |
| `frontend/src/pages/budget/account-detail/AccountDetailPage.tsx` | Wire edit/delete handlers; manage single pending-delete state; render `TransactionUndoToast` |
| `frontend/src/pages/budget/account-detail/AccountDetailPage.hooks.ts` | Expose `applyEdit`, `applyDelete`, `applyRestore` helpers that mutate `allItems` directly |
| `frontend/src/pages/budget/components/TransactionDialog.tsx` | Remove the stub; when `existing` is set, call `useUpdateBudgetTransactionMutation` |
| `frontend/src/pages/budget/components/TransactionUndoToast.tsx` | **New** — countdown bar + Undo button; timer fires `onTimeout`, unmount with `pending` still set also fires it |
| `frontend/src/pages/budget/BudgetPage.css` | Styles for row menu button, popover, and toast |
| `frontend/e2e/budget/*.spec.ts` | New Playwright scenario: delete → undo → delete → wait 5s → reload → row gone |

No file grows materially. The largest new piece is `TransactionUndoToast.tsx` (estimated ~60 lines).

## UX flows

### Edit
1. User taps `⋯` on a row → row menu opens
2. User taps `Edit` → `AccountDetailPage` opens `TransactionDialog` with `existing={tx}`
3. Dialog title shows `Edit Transaction`; all fields populate from `existing` (logic already present)
4. User edits and submits → `useUpdateBudgetTransactionMutation({id, accountId, categoryId, amount, date, notes})`
5. On success: dialog closes; `applyEdit(updated)` replaces the row in `allItems` using the mutation's response payload; `BudgetSummary` tag is invalidated so the account hero (balance) and envelope figures refresh
6. On error: dialog stays open, error message renders via the existing `field-error` pattern

All `UpdateTransactionCommand` fields are editable, including `accountId` (which moves the transaction to another account) and `categoryId` (which can be cleared to "Uncategorized").

### Delete (optimistic + undo)
1. User taps `⋯` → `Delete`
2. **Optimistic step**: `applyDelete(tx.id)` removes the row from `allItems` immediately; row visually disappears
3. `TransactionUndoToast` renders at the bottom of the page with message `Deleted` (or `Deleted '{notes ?? categoryName}'` if available), a `Undo` button, and a 5-second progress bar
4. **If user taps Undo**: `clearTimeout`, `applyRestore(tx)` re-inserts the row at its original sorted position, `setPending(null)`
5. **If timer fires**: `deleteTx({id})` runs; on success `setPending(null)` and `BudgetSummary` is invalidated to refresh balances; on error `applyRestore(tx)` + a short error toast `"Could not delete. Restored."`
6. **If user triggers a second Delete while a toast is pending**: commit the first delete immediately (cancel timer, call `deleteTx`), then start the new toast — single-pending policy
7. **If user navigates away (unmount)**: commit the pending delete via a `useRef`-held dispatch so the API call goes out even after the component unmounts. RTK Query handles the in-flight request independently. Failure after unmount is silent (logged to console).
8. **If the browser is refreshed mid-toast**: the delete is effectively cancelled — the row still exists on the server. This is acceptable; the user can retry.

## State machine

`AccountDetailPage` holds a single piece of state:

```ts
interface PendingDelete {
  tx: BudgetTransactionDto   // for rollback
  timerId: number            // setTimeout id, so we can clear it
}
const [pending, setPending] = useState<PendingDelete | null>(null)
```

Transitions:

| Event | Effect |
|---|---|
| User clicks Delete on row R | If `pending` exists: `clearTimeout(pending.timerId)` + commit it via `deleteTx({id: pending.tx.id})`. Then: `applyDelete(R.id)`, start new 5s timer, set `pending = {tx: R, timerId}` |
| User clicks Undo | `clearTimeout(pending.timerId)`, `applyRestore(pending.tx)`, `setPending(null)` |
| Timer fires | `await deleteTx({id: pending.tx.id})`. On success: invalidate `BudgetSummary`, `setPending(null)`. On error: `applyRestore(pending.tx)`, show error toast, `setPending(null)` |
| Component unmount with `pending` set | Fire-and-forget `dispatch(api.endpoints.deleteBudgetTransaction.initiate({id: pending.tx.id}))`; do not await |

## Cache strategy

The infinite-scroll list in `useAccountDetail` keeps a local `allItems` array that accumulates pages of the underlying `useListBudgetAccountTransactionsQuery`. Patching the RTK cache alone doesn't propagate to `allItems`, so we patch `allItems` directly and skip query invalidation for these specific mutations.

### Hook surface changes
```ts
function useAccountDetail(accountId: string): {
  // ... existing fields
  applyEdit(updated: BudgetTransactionDto): void
  applyDelete(id: string): void
  applyRestore(tx: BudgetTransactionDto): void
}
```

Implementations:
- `applyEdit`: `setAllItems(prev => prev.map(t => t.id === updated.id ? updated : t).sort(byDateDescThenCreatedAtDesc))` — re-sort because `date` may have changed
- `applyDelete`: `setAllItems(prev => prev.filter(t => t.id !== id))`
- `applyRestore`: insert at the first index where the existing row is older (date DESC, then createdAt DESC); fall back to end of list

### Mutation invalidation
Modify the `updateBudgetTransaction` and `deleteBudgetTransaction` mutation definitions in `api.ts` so they **do not** invalidate the `BudgetTransactions` tag. The page owns list state through `applyEdit` / `applyDelete` / `applyRestore`.

After every successful edit and after every successful delete commit, the page dispatches `api.util.invalidateTags([{type: 'BudgetSummary', id: 'LIST'}])` manually so the hero card and envelope figures pick up new balances.

This split makes responsibilities explicit: the list is page-owned, the summary is cache-owned.

### Account balance refresh
After every successful edit and delete commit, dispatch `api.util.invalidateTags([{type: 'BudgetSummary', id: 'LIST'}])`. The hero card (`AccountHero`) reads from `getBudgetSummary`, so the balance updates without a full list refetch.

## Component contracts

### `TransactionRowMenu` (inline in `AccountTransactionList`)
- A `<button class="bdg-tx-menu-btn">⋯</button>` placed after the amount in each row
- Per-row menu open state via a single `openMenuId: string | null` in `AccountTransactionList`; clicking another row's button closes the previous one
- Outside-click closes the menu (mousedown listener pattern from `AccountDetailPage.tsx:21-28`)
- Menu items: `✎ Edit` (default colour), `🗑 Delete` (red)
- New `AccountTransactionList` props:
  ```ts
  interface Props {
    items: BudgetTransactionDto[]
    endSentinelRef: React.RefObject<HTMLDivElement | null>
    onEdit: (tx: BudgetTransactionDto) => void
    onDelete: (tx: BudgetTransactionDto) => void
  }
  ```

### `TransactionUndoToast` (new)
```ts
interface Props {
  message: string
  onUndo: () => void
  onTimeout: () => void
  durationMs?: number   // default 5000
}
```
- Renders a fixed-bottom bar with `message` left, `Undo` button right, and a thin progress bar at the bottom edge
- Internal `useEffect`:
  ```ts
  useEffect(() => {
    const id = window.setTimeout(onTimeout, durationMs ?? 5000)
    return () => window.clearTimeout(id)
  }, [])  // intentionally once; parent re-mounts component for each new pending delete
  ```
- Parent gives the component a `key={pending.tx.id}` so a second pending delete forces a fresh mount with a fresh timer
- Progress bar uses CSS transition: `width: 100% → 0` over `durationMs`

### `TransactionDialog` edit path
Replace lines 97-101:
```ts
if (existing) {
  setErr('Editing transactions is not yet supported.')
  return
}
```
with:
```ts
if (existing) {
  await updateTx({
    id: existing.id,
    accountId: values.accountId,
    categoryId: values.categoryId === UNCATEGORIZED_ID ? null : values.categoryId,
    amount: signed,
    date: values.date,
    notes: values.notes.trim() || null,
  }).unwrap()
  onClose()
  return
}
```
Add `const [updateTx, {isLoading: isUpdating}] = useUpdateBudgetTransactionMutation()` and combine the loading flags for the submit button. Remove the obsolete TODO comment blocks at `TransactionDialog.tsx:41-49` and `TransactionDialog.tsx:63`.

The dialog also needs to accept an `onSaved?: (updated: BudgetTransactionDto) => void` callback so `AccountDetailPage` can call `applyEdit` with the server response (the update mutation returns the updated DTO).

## Error handling

| Failure | UI behaviour |
|---|---|
| Update API returns error | Dialog stays open; error message renders inline via existing `field-error` pattern; row is unchanged in the list |
| Delete commit (timer or unmount) returns error | `applyRestore(tx)` + show a short non-undoable error toast `"Could not delete. Restored."` (auto-dismiss 3s) |
| Network error during undo | N/A — undo never hits the network |
| Multiple toasts queued | Single-pending policy: starting a new delete commits the previous one immediately (best-effort; if that commit fails, we still proceed with the new delete and silently log the earlier failure — the row was already visibly gone) |

## Testing

**Backend**: existing tests for `UpdateTransactionHandler` and `DeleteTransactionHandler` already cover the use cases. No changes.

**Frontend unit**: skip unless the budget module already has unit tests for similar dialogs. The implementation plan should verify this and add minimal coverage for `applyEdit` / `applyDelete` / `applyRestore` sort behaviour only if a Vitest setup exists.

**E2E (Playwright)**: one happy-path scenario:
1. Open account detail page (seeded with at least 2 transactions)
2. Click `⋯` on first row → click `Delete` → toast appears → click `Undo` → row is back
3. Click `⋯` on first row → click `Delete` → wait 5+ seconds → reload page → row is gone
4. Click `⋯` on remaining row → click `Edit` → change amount → save → row shows new amount

## Open questions

None. All UX, scope, and cache decisions resolved during brainstorming.

## Notes

- The existing `useAccountDetail` accumulator pattern was the trickiest decision point. Option A (local patches + skip invalidation) was chosen over Option B (invalidate + reset) because (a) the optimistic undo flow needs stable list state for 5 seconds without server-side races, and (b) preserving infinite-scroll position is a clear win for users who have scrolled deep into history.
- The `commit-on-unmount` rule is deliberate. Without it, a user who taps Delete then navigates away thinks the row is gone but the server still has it — a classic source of user confusion.
- This design does not change how Create works. The FAB still uses the existing `TransactionDialog` create path with its default `invalidatesTags` behaviour.
