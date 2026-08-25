# Accounts Total vs Budget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide a clear view of the combined account balance and explicitly alert the user if Total Available (budget) exceeds Accounts Total.

**Architecture:** Modifies `BudgetPage` to pass `summary.available` down to `AccountsStrip`. `AccountsStrip` computes `totalAccounts` and `overage`, displaying an alert in the section header if over budget.

**Tech Stack:** React, TypeScript, CSS

**Spec:** `docs/superpowers/specs/2026-08-25-accounts-total-vs-budget-design.md`

## Global Constraints

- Text labels must match the agreed Thai wording.
- Any new CSS classes must be added to the relevant CSS file (`BudgetPage.css` or equivalent).
- Syncfusion React Dialog styling (style dropped, zIndex TS error) overrides MUST be applied via className + !important in a global CSS file.

---

### Task 1: Update AccountsStrip to show totals and overage

**Files:**
- Modify: `frontend/src/pages/budget/components/AccountsStrip.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.tsx`
- Modify: `frontend/src/pages/budget/BudgetPage.css`

**Interfaces:**
- Consumes: `summary.available` and `summary.accounts` from `BudgetPage`

- [ ] **Step 1: Update `AccountsStrip` props and calculation**

In `frontend/src/pages/budget/components/AccountsStrip.tsx`, update the props to include `totalAvailable`:
```tsx
export function AccountsStrip({accounts, totalAvailable}: {accounts: BudgetAccountDto[], totalAvailable: number}) {
  const [addOpen, setAddOpen] = useState(false)
  const [reconcileFor, setReconcileFor] = useState<BudgetAccountDto | null>(null)
  
  const totalAccounts = accounts.reduce((sum, a) => sum + a.balance, 0)
  const overage = totalAvailable - totalAccounts
```

- [ ] **Step 2: Update UI rendering for the header in `AccountsStrip`**

In `frontend/src/pages/budget/components/AccountsStrip.tsx`, replace `<h3>Accounts · newest first</h3>` with:
```tsx
        <h3 style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          บัญชีรวม {formatTHB(totalAccounts)}
          {overage > 0 && (
            <span className="bdg-accounts-overage">
              (ตั้งงบเกิน -{formatTHB(overage)})
            </span>
          )}
        </h3>
```

- [ ] **Step 3: Update `BudgetPage.tsx` to pass the prop**

In `frontend/src/pages/budget/BudgetPage.tsx`, update the `<AccountsStrip>` usage:
```tsx
      <AccountsStrip accounts={summary.accounts} totalAvailable={summary.available} />
```

- [ ] **Step 4: Add CSS for the overage alert**

In `frontend/src/pages/budget/BudgetPage.css`, append:
```css
.bdg-accounts-overage {
  color: var(--red);
  font-weight: 500;
  font-size: 0.9em;
}
```

- [ ] **Step 5: Verify build**
Run the TypeScript compiler to ensure the prop changes are correctly typed.
```bash
cd frontend && npm run typecheck
```

- [ ] **Step 6: Commit**
```bash
git add frontend/src/pages/budget/components/AccountsStrip.tsx frontend/src/pages/budget/BudgetPage.tsx frontend/src/pages/budget/BudgetPage.css docs/superpowers/specs/2026-08-25-accounts-total-vs-budget-design.md docs/superpowers/plans/2026-08-25-accounts-total-vs-budget.md
git commit -m "feat(budget): display combined accounts total and budget overage alert in AccountsStrip"
```
