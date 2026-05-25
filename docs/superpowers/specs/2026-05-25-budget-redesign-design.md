# Budget Redesign — Design

**Status:** Approved — ready for implementation planning
**Date:** 2026-05-25
**Author:** Brainstormed with Claude (MenuNest project)
**Mock:** `docs/mocks/budget-redesign-mock.html`

## Problem

The Budget module exists end-to-end (envelope categories, monthly
assignment, accounts, transactions, targets) but the UI was built for
desktop: a fixed three-column layout (Accounts sidebar | Envelope
table | Monthly summary panel) on a dark background. Transactions are
hidden inside a per-category dialog so the user cannot answer "what
did I spend from this account?" without drilling through unrelated
state. The user wants:

1. A mobile-first redesign of `/budget` — single column, the
   side-panels stop trying to fit and become content blocks instead.
2. Transactions grouped under the account they were spent from
   (banking-app shape) so account → transaction provenance is obvious.
3. Both `accounts` and `transactions` reordered by `CreatedAt DESC`
   so the newest row is always on top regardless of `Date`.
4. Tap and long-press affordances on each envelope so common actions
   (add transaction in this envelope, move money, cover overspending)
   are reachable in one gesture instead of a dialog walk.

All existing envelope features (groups, categories, targets, monthly
assignment, Ready to Assign, cover overspending, move money) stay —
this is a UI restyle plus one new route, not a feature rewrite.

## Goals

- Restyle `/budget` as a single-column mobile-first page. Desktop
  centers the column at a comfortable max-width; tablet does the
  same; the legacy 3-column layout is removed.
- Add a new route `/budget/accounts/:accountId` that shows one
  account's balance, this-month inflow/outflow, and the full list of
  its transactions (paginated, CreatedAt DESC).
- Change `ListAccounts` sort to `CreatedAt DESC` (closed accounts
  filtered out of the strip; visible via "All" link if needed).
- Change `ListTransactions` (the existing month-filtered query used
  by the category drill) sort from `Date DESC, CreatedAt DESC` to
  just `CreatedAt DESC`.
- Add a new query `ListAccountTransactions(accountId, skip, take)`
  returning the account's full transaction history (not month-bound)
  for the account-detail page.
- Auto-assign `SortOrder` in `CreateCategoryHandler`,
  `CreateGroupHandler`, and `CreateAccountHandler` so the field
  disappears from create forms — clients no longer compute it.
- Envelope card interactions:
  - Tap → expand inline (one at a time): editable Assigned input,
    Activity/Available read-out, action row [+ Transaction] [⇄ Move]
    [✎ Edit] (and [⚠ Cover] when overspent).
  - Long-press → fast path: open `TransactionDialog` with
    `categoryId` pre-filled.
  - Collapsed cards show small inline icons for the most relevant
    action: `⚠` (red) on overspent rows, `⇄` on healthy rows.

## Non-goals

- Daily-budget indicator (e.g. "you can spend ฿X per day"). Deferred
  to Phase 2; envelope semantics still apply.
- Drag-to-reorder for groups, categories, or accounts. `SortOrder`
  becomes implementation-internal; users do not reorder manually.
- Migrating the existing modals (AddAccount, Transaction, MoveMoney,
  CoverOverspending, AddCategory) to a bottom-sheet container. They
  remain centered Syncfusion modals; only the surrounding page is
  restyled.
- Showing the transaction creator's name or avatar in the activity
  list. `CreatedByUserId` keeps being stored but is not surfaced.
- Adding a global transactions feed across all accounts. Each
  account's feed lives under its own detail route.
- Charts / sparklines / spending analytics. Out of scope.

## Approach

The page splits into two routes with disjoint responsibilities:

- `/budget` — envelopes-and-overview. Renders the Ready-to-Assign
  hero, a horizontal scrollable Accounts strip (each card tappable),
  and the grouped Envelope list. No transactions on this page.
- `/budget/accounts/:accountId` — one account's detail. Renders an
  Account hero (balance + month in/out summary) and a paginated
  transaction list sorted by `CreatedAt DESC`. A FAB opens the
  TransactionDialog with `accountId` pre-filled.

The existing per-month state (selected `year`/`month`, current
filter) stays in `budgetSlice`. The account-detail page is
month-agnostic — it shows all history for that account, paginated.

Backend changes are additive except for two pure sort flips and three
auto-sortOrder rewrites. No data migration. No domain change.
`MonthlySummaryDto` is unchanged. The frontend's RTK Query slice gets
one new endpoint (`listAccountTransactions`).

## Data Model (unchanged)

No domain or schema change. `BudgetAccount`, `BudgetTransaction`,
`BudgetCategoryGroup`, `BudgetCategory`, `MonthlyAssignment`,
`MonthlyIncome` keep their existing fields. The `Entity` base class
already carries `CreatedAt`, so the sort change is a one-line
`orderby` flip on the EF query side.

## Backend

### Sort changes

`ListAccountsHandler`:
```csharp
// before
.OrderBy(a => a.IsClosed).ThenBy(a => a.Type)
.ThenBy(a => a.SortOrder).ThenBy(a => a.Name)

// after
.Where(a => !a.IsClosed)        // hide closed by default
.OrderByDescending(a => a.CreatedAt)
```
A future "show closed" toggle can re-add them; for v1 they are simply
filtered out. (The existing `ReopenAccount` flow is preserved on the
backend; the strip just doesn't render them.)

`ListTransactionsHandler` (month-filtered, used by category drill):
```csharp
orderby t.CreatedAt descending   // was: orderby t.Date descending, t.CreatedAt descending
```

### New query: ListAccountTransactions

```csharp
public sealed record ListAccountTransactionsQuery(
    Guid AccountId,
    int Year,                    // for MonthInflow/MonthOutflow window
    int Month,                   // 1..12
    int Skip,                    // default 0
    int Take                     // default 50, clamped to [1, 100]
) : IQuery<AccountTransactionsPageDto>;

public sealed record AccountTransactionsPageDto(
    AccountSummaryDto Account,
    IReadOnlyList<BudgetTransactionDto> Items,
    bool HasMore                 // true if more rows exist beyond Skip+Take
);

public sealed record AccountSummaryDto(
    Guid Id,
    string Name,
    BudgetAccountType Type,
    decimal Balance,
    decimal MonthInflow,         // sum of positive amounts where Date in current month
    decimal MonthOutflow         // sum of negative amounts where Date in current month
);
```

Handler responsibilities:
- Resolve `familyId` via `_users.RequireFamilyAsync`.
- Look up the account by `Id` AND `FamilyId == familyId`. If not
  found, throw `NotFoundException` — never leak existence across
  families.
- Clamp `Take` to `[1, 100]` and `Skip` to `>= 0`.
- `Items` = transactions for that account, ordered by `CreatedAt
  DESC`, paginated. Join account name and category name/emoji like
  the existing `ListTransactionsHandler` does.
- `MonthInflow/MonthOutflow` use the `Year`/`Month` window passed
  by the client — no server-side timezone inference. The client
  passes whatever the user is viewing on the budget page (which
  comes from `budgetSlice`, derived from the user's local clock
  when the page first loads).
- `HasMore` = `await query.Skip(skip+take).AnyAsync()` — single
  cheap row check, no extra count.

REST surface:
```
GET /api/budget/accounts/{accountId}/transactions?year=2026&month=5&skip=0&take=50
  → 200 AccountTransactionsPageDto
  → 404 if account not found in caller's family
```

### Auto sortOrder

`CreateCategoryHandler`, `CreateGroupHandler`,
`CreateAccountHandler` compute `SortOrder` server-side as `(max
existing SortOrder in scope) + 1`, with `0` when the scope is empty.
"Scope" is:
- Category → `FamilyId == familyId && GroupId == cmd.GroupId`
- Group → `FamilyId == familyId`
- Account → `FamilyId == familyId`

Commands lose `SortOrder` from their record (breaking change for
in-repo callers — only the dialogs call them, all updated in this
spec). `UpdateCategoryCommand`, `UpdateGroupCommand`,
`UpdateAccountCommand` keep `SortOrder` (manual reorder may return
later); the redesigned UI never sends a non-existent value.

### Endpoint registration

`BudgetController` gets one new action:
```csharp
[HttpGet("accounts/{id:guid}/transactions")]
public async Task<AccountTransactionsPageDto> ListAccountTransactions(
    Guid id, [FromQuery] int year, [FromQuery] int month,
    [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    => await _mediator.Send(new ListAccountTransactionsQuery(id, year, month, skip, take), ct);
```

## Frontend

### Routes

```
/budget                          BudgetPage           (restyled)
/budget/accounts/:accountId      AccountDetailPage    (new)
```

`router.tsx` adds the second route under the same auth shell.

### Component map

`frontend/src/pages/budget/`:
```
BudgetPage.tsx                  — restyled, single column
BudgetPage.css                  — replaced (new tokens, new layout)
BudgetPage.hooks.ts             — reused (selectors, formatTHB)
budgetSlice.ts                  — minor: drop accountsOpen/summaryOpen
                                  (no more side drawers)

components/
  MonthStrip.tsx                — extracted from old BudgetPage header
  RtaHero.tsx                   — extracted from old budget-rta block,
                                  promoted to hero card
  AccountsStrip.tsx             — new (horizontal scroll, tappable)
  EnvelopeList.tsx              — new (groups + EnvelopeCard, replaces
                                  EnvelopeTable + EnvelopeRow table layout)
  EnvelopeCard.tsx              — new (replaces EnvelopeRow tr/td)
  AddAccountDialog.tsx          — kept (drop sortOrder field)
  AddCategoryDialog.tsx         — kept (drop sortOrder field)
  TransactionDialog.tsx         — kept, accepts optional preset
                                  { accountId?, categoryId? } props
  MoveMoneyDialog.tsx           — kept
  CoverOverspendingDialog.tsx   — kept

  AccountDetailPage.tsx         — new
  AccountDetailPage.hooks.ts    — new (RTK infinite-scroll wrapper)
  AccountHero.tsx               — new
  AccountTransactionList.tsx    — new (grouped by date header)
```

Deleted:
- `AccountsSidebar.tsx` — content moves into the `AccountsStrip` +
  `AccountDetailPage` pair.
- `MonthlySummaryPanel.tsx` — its key numbers (income, assigned,
  available, leftover) are folded into `RtaHero` and the
  `AccountHero`. Cover-overspending and Move-money entry points
  move to the EnvelopeCard.
- `EnvelopeTable.tsx` and `EnvelopeRow.tsx` — replaced by
  `EnvelopeList` + `EnvelopeCard`.

### EnvelopeCard interactions

Per the react-structure skill, logic lives in
`EnvelopeCard.hooks.ts`; the `.tsx` only renders. The hook owns:
- `expanded: boolean` (only one card expands at a time — coordinated
  via `budgetSlice.expandedCategoryId`).
- `assignedDraft: number` (mirrors `cat.assigned`, commits on blur
  and Enter via `setAssignedAmountMutation`).
- `onTap` / `onLongPress` (pointer-based; long-press = 450ms
  hold without movement > 8px).
- `onAddTransaction` → opens `TransactionDialog` with
  `{ categoryId: cat.categoryId }` preset. The dialog still
  requires the user to pick an account explicitly — no smart
  default account — so the gesture skips category selection
  without making an assumption the user can't see.
- `onMoveMoney` → opens `MoveMoneyDialog` with `from: cat`.
- `onCoverOverspending` → opens `CoverOverspendingDialog` with
  `overspent: cat`. Only when `cat.available < 0`.
- `onEdit` → opens `EditCategoryDialog` (reuses `AddCategoryDialog`
  in update mode — already supported by the underlying
  `UpdateCategoryCommand`).

Collapsed card layout:
- Row 1: emoji + name + (inline icon: `⇄` healthy / `⚠` overspent)
  + availability pill.
- Row 2: target hint + activity text.
- Row 3: progress bar.

Expanded body (below a dashed divider):
- Assigned input (right-aligned, focuses on expand, commits on blur/
  Enter; Escape reverts).
- Activity / Available read-out (text).
- Action row: [+ Transaction] (primary), [⇄ Move], [✎ Edit], and
  [⚠ Cover] when `available < 0`.

Tap on the inline icon (`⇄` or `⚠`) bypasses expansion and opens its
dialog directly — single-gesture shortcut.

### AccountDetailPage behavior

- Hero card: `Type` chip, account name, big balance, `+inflow /
  −outflow` for the current month.
- Section header: "Transactions · newest first" with a Filter link
  (Filter is a Phase-2 hook; v1 renders an inert link until the
  filter UI is built).
- Transaction list: rows grouped by `Date` header ("Today · May 25",
  "Yesterday · May 24", "May 23", …). Group label uses the
  transaction `Date`, not `CreatedAt`, so rows added today for last
  week's expenses still bucket under the spend date — while
  *ordering within the page* stays `CreatedAt DESC`.
- Infinite scroll: `IntersectionObserver` on a sentinel at the
  bottom of the list calls `fetchMore({skip: items.length, take:
  50})` while `HasMore` is true.
- FAB: opens `TransactionDialog` with `accountId` preset to this
  account. On success the cache is invalidated for both the account
  page and the budget page (RTK `invalidatesTags`).
- Back button returns to `/budget`. No nested routes.

### budgetSlice changes

```diff
- accountsOpen: boolean
- summaryOpen: boolean
+ expandedCategoryId: string | null
```
Drawer state is gone (no more side panels). The new field is what
the EnvelopeCard hook reads to know whether it's the active one.

### CSS / theme

Move off the dark-only YNAB palette. The new sheet uses CSS
variables defaulting to a soft slate dark theme (matches the
existing migraine and pomodoro mocks). Light mode is supported via a
`.light` class on `<body>`; toggle wiring is a Phase-2 hook —
present in the mockup, deferred in the real app until a global theme
toggle exists.

Tokens:
- `--accent` indigo-500 (primary), `--accent-soft` 15% alpha.
- `--green / --red / --orange` for healthy / overspent / urgent.
- Cards use 12–18px border radius, no shadows in dark, soft shadows
  in light.
- Hero blocks use a `linear-gradient(135deg, indigo → violet)` or
  `(sky → indigo)` for the account hero.

### Responsive

- < 600px: single column, page padding 16px, account strip h-scrolls.
- 600–1023px: single column centered, max-width 540px, page padding 24px.
- ≥ 1024px: single column centered, max-width 720px, page padding 32px.
  The legacy 3-column layout is not restored — desktop is just a wider
  comfortable single column. (This is the deliberate trade for
  mobile-first; users who want more density can resize the window
  but won't see a separate sidebar.)

## Family sharing

No change. `IUserProvisioner.RequireFamilyAsync` already gates every
query and command; both the new endpoint and the existing ones
filter by `FamilyId`. Multiple family members see the same data; the
creator's identity is stored on each transaction but not shown.

## Migration

- No database migration.
- One frontend code migration: replace `AccountsSidebar` usage,
  `MonthlySummaryPanel` usage, and `EnvelopeTable`/`EnvelopeRow`
  with the new components. Tests that target the old structure are
  updated in lockstep.
- Sort-order auto-assignment is backward-compatible: existing rows
  keep their stored `SortOrder`, but new rows go to the end. The
  envelope view no longer relies on `SortOrder` for ordering of
  groups inside the list rendering — order is the `MonthlySummaryDto`
  order, which still sorts by `SortOrder` in the handler, so the
  effective output stays stable.

## Testing

### Backend (xUnit, `MenuNest.Application.UnitTests`)

- `ListAccountsHandlerTests`: returns rows sorted by `CreatedAt
  DESC`; excludes closed; respects family scope.
- `ListTransactionsHandlerTests`: ordered by `CreatedAt DESC` only;
  drops the old date-first ordering test.
- `ListAccountTransactionsHandlerTests`: family scope (404 on cross-
  family), pagination (`Take` clamp, `HasMore` correctness), and
  the month-in/out summary.
- `CreateCategoryHandlerTests`: `SortOrder` is `0` when group is
  empty, `max+1` otherwise.
- `CreateGroupHandlerTests`, `CreateAccountHandlerTests`: same
  pattern.

### Frontend (Vitest + RTL where applicable)

- `EnvelopeCard` long-press fires `onAddTransaction` with the right
  preset; short tap fires `onTap` and toggles expansion.
- Only one card expanded at a time (state coordinator).
- Assigned input commits on blur and Enter; reverts on Escape.
- `AccountDetailPage` loads page 1, then more on scroll-to-end;
  stops requesting when `HasMore=false`; cache invalidates on
  `createTransaction` success.

### E2E (Playwright, smoke)

- `/budget` renders one column with envelopes; tap account card →
  `/budget/accounts/:id` with that account's transactions.
- Long-press on an envelope opens `TransactionDialog` with the
  category preselected. Submitting the form lands the row at the
  top of the account-detail list (CreatedAt DESC).

## Open questions

None for v1.

Phase 2 candidates explicitly tabled during brainstorming:
- Daily-budget indicator on the hero ("≈ ฿X/day").
- Bottom-sheet container for dialogs on mobile.
- Drag-to-reorder envelopes / accounts.
- Show creator's avatar on transactions.
- Filter UI on the account-detail page (the Filter link is a stub).
- Global cross-account transactions feed.
- Light/dark toggle wired into a real theme store.
