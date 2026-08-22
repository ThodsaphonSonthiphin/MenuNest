/**
 * Pure THB currency formatter — lives in `lib/` (no dependency on
 * `shared/api/api`, which pulls in msal/`window` at import time and is
 * therefore unusable from vitest's node environment). `BudgetPage.hooks.ts`
 * re-exports this so every existing `formatTHB` import site is unaffected.
 */
export function formatTHB(n: number): string {
  const sign = n < 0 ? '−' : ''
  return `${sign}฿${Math.abs(n).toLocaleString('en-US', {minimumFractionDigits: 2, maximumFractionDigits: 2})}`
}
