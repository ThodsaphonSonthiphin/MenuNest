import type { Page } from '@playwright/test'
import { recordRequest, type RequestCapture } from './types'

/**
 * `/budget` sits behind FamilyRequiredRoute, so a spec that does not answer
 * `/api/me` with a familyId never reaches the page at all — it renders
 * "Could not load your profile" or bounces to /join-family. Playwright's
 * config deliberately serves the built SPA WITHOUT a backend (see
 * playwright.config.ts), so any budget assertion needs these routes.
 *
 * Fixtures are shaped for the shortcut rail (menunest-191/195): the history
 * carries one live row (so Undo is enabled) and one already-undone row (so
 * Redo is enabled), which is what makes both rail buttons pressable.
 */

const CATEGORY_FOOD = 'cat-food'
const CATEGORY_POWER = 'cat-power'

// menunest-202/207/212 — issue #112 fixtures. Exported so a spec can locate
// these rows and build its own override summaries (e.g. driving the Credit
// card's shortfall to 20,000, matching the mock's second panel) without
// hand-rolling a whole payload that then drifts from this one.
export const CREDIT_GROUP_ID = 'grp-credit'
export const CREDIT_ENVELOPE_ID = 'cat-payment-kbank'
export const CREDIT_ACCOUNT_ID = 'acct-credit-kbank'
export const LOAN_ACCOUNT_ID = 'acct-loan-car'

const meResponse = {
  userId: 'user-1',
  email: 'test@menunest.app',
  displayName: 'ทศพล',
  familyId: 'family-1',
  familyName: 'ครอบครัวทดสอบ',
  familyInviteCode: 'TEST01',
  authProvider: 'Google',
  homePath: null,
  uvWarnThreshold: null,
  feelsLikeWarnThreshold: null,
  activeTargetRule: null,
}

const envelope = (categoryId: string, name: string, available: number) => ({
  categoryId,
  name,
  emoji: null,
  sortOrder: 0,
  isHidden: false,
  assigned: 1000,
  activity: -(1000 - available),
  available,
  targetType: 'None',
  targetAmount: null,
  targetDueDate: null,
  targetDayOfMonth: null,
  targetProgressFraction: null,
  targetHint: null,
  isEveryday: false,
  // Ordinary envelopes carry neither field — only a Payment envelope does
  // (menunest-202). Present here so the shape matches EnvelopeDto exactly.
  paymentForAccountId: null,
  shortfall: null,
  cardSpending: null,
})

// menunest-202/205 — the Payment envelope docs/mocks/… shows on the "จ่ายบัตร
// KBank" card: funded (shortfall 0 ⇒ จ่ายเต็มได้) by default. A spec drives it
// underfunded by spreading the summary and overriding `shortfall` on both the
// envelope and its account, per the mock's second panel (−฿20,500 / ขาดอีก
// ฿20,000).
const paymentEnvelope = (
  accountName: string,
  available: number,
  shortfall: number,
  cardSpending: number,
) => ({
  categoryId: CREDIT_ENVELOPE_ID,
  name: `จ่ายบัตร ${accountName}`,
  emoji: '💳',
  sortOrder: 0,
  isHidden: false,
  assigned: 0,
  activity: 0,
  available,
  targetType: 'None',
  targetAmount: null,
  targetDueDate: null,
  targetDayOfMonth: null,
  targetProgressFraction: null,
  targetHint: null,
  // menunest-205: a Payment envelope can never be Everyday.
  isEveryday: false,
  paymentForAccountId: CREDIT_ACCOUNT_ID,
  shortfall,
  cardSpending,
})

const accountCash = {
  id: 'acct-1',
  name: 'เงินสด',
  type: 'Cash',
  balance: 5000,
  sortOrder: 0,
  isClosed: false,
  shortfall: null,
}

// Funded by default (balance −500, envelope available 500 ⇒ shortfall 0),
// matching the mock's first panel exactly.
const accountCreditKBank = {
  id: CREDIT_ACCOUNT_ID,
  name: 'KBank',
  type: 'Credit',
  balance: -500,
  sortOrder: 1,
  isClosed: false,
  shortfall: 0,
}

// menunest-207/212/214 — a Loan has NO Payment envelope (menunest-206), so
// its only pay path is the account-menu's `จ่ายค่างวด` item → PaymentDialog's
// funding-envelope picker. `shortfall` is always null on a Loan.
const accountLoanCar = {
  id: LOAN_ACCOUNT_ID,
  name: 'ผ่อนรถ',
  type: 'Loan',
  balance: -100000,
  sortOrder: 2,
  isClosed: false,
  shortfall: null,
}

const accountsById: Record<string, typeof accountCash> = {
  [accountCash.id]: accountCash,
  [accountCreditKBank.id]: accountCreditKBank,
  [accountLoanCar.id]: accountLoanCar,
}

/**
 * Exported so a spec can reshape one field (e.g. drive a category negative to
 * make the "⚠ Overspent" filter chip render its danger variant) without
 * hand-rolling a whole summary payload that then drifts from this one.
 */
export const budgetSummaryFixture = {
  year: 2026,
  month: 8,
  income: 20000,
  totalAssigned: 2000,
  totalActivity: -800,
  readyToAssign: 500,
  available: 1200,
  groups: [
    {
      groupId: 'grp-1',
      name: 'ค่าใช้จ่ายประจำ',
      sortOrder: 0,
      isHidden: false,
      totalAssigned: 2000,
      totalActivity: -800,
      totalAvailable: 1200,
      categories: [envelope(CATEGORY_FOOD, 'ค่ากิน', 700), envelope(CATEGORY_POWER, 'ค่าไฟ', 500)],
    },
    {
      // menunest-202: "its own group, made with the Account" — a Credit
      // account's Payment envelope never lands inside a group the user made.
      groupId: CREDIT_GROUP_ID,
      name: 'บัตรเครดิต',
      sortOrder: 1,
      isHidden: false,
      totalAssigned: 0,
      totalActivity: 0,
      totalAvailable: 500,
      categories: [paymentEnvelope('KBank', 500, 0, 500)],
    },
  ],
  accounts: [accountCash, accountCreditKBank, accountLoanCar],
  dailyAllowance: null,
}

const historyResponse = [
  {
    id: 'chg-2',
    userId: 'user-1',
    userDisplayName: 'ทศพล',
    kind: 'Assign',
    batchId: null,
    categoryName: 'ค่ากิน',
    secondCategoryName: null,
    delta: 300,
    flagValue: null,
    isUndone: false,
    undoneByDisplayName: null,
    createdAt: '2026-08-28T09:00:00Z',
    canUndo: true,
    blockedReason: null,
  },
  {
    id: 'chg-1',
    userId: 'user-1',
    userDisplayName: 'ทศพล',
    kind: 'Move',
    batchId: null,
    categoryName: 'ค่ากิน',
    secondCategoryName: 'ค่าไฟ',
    delta: -200,
    flagValue: null,
    isUndone: true,
    undoneByDisplayName: 'มาลี',
    createdAt: '2026-08-27T09:00:00Z',
    canUndo: true,
    blockedReason: null,
  },
]

// menunest-204/207 — `POST /api/budget/payments` returns the created pair as
// a PaymentDto. The dialog's own success path only reads that it resolved
// (onSaved/onClose), so one fixed shape covers every payment a spec makes.
const paymentFixture = {
  paymentId: 'pay-1',
  fromAccountId: accountCash.id,
  fromAccountName: accountCash.name,
  toAccountId: accountCreditKBank.id,
  toAccountName: accountCreditKBank.name,
  amount: 500,
  date: '2026-08-30',
  notes: null,
}

type BudgetConfig = {
  me: unknown
  summary: unknown
  history: unknown[]
}

export const createBudgetMocks = (page: Page, capture: RequestCapture) => {
  const config: BudgetConfig = {
    me: meResponse,
    summary: budgetSummaryFixture,
    history: historyResponse,
  }

  const self = {
    me: (data: unknown) => {
      config.me = data
      return self
    },
    summary: (data: unknown) => {
      config.summary = data
      return self
    },
    /** Pass [] to prove the rail's Undo/Redo go disabled with nothing to undo. */
    history: (rows: unknown[]) => {
      config.history = rows
      return self
    },
    apply: async () => {
      await page.route(/\/api\/me(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ json: config.me })
      })
      await page.route(/\/api\/budget\/summary(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ json: config.summary })
      })
      await page.route(/\/api\/budget\/history(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ json: config.history })
      })
      // menunest-193: undo/redo are compensating writes, so the SPA only needs
      // a 204 plus the re-fetch its invalidatesTags triggers.
      await page.route(/\/api\/budget\/history\/[^/]+\/(undo|redo)$/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ status: 204, body: '' })
      })
      await page.route(/\/api\/budget\/accounts\/([^/]+)\/transactions(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        const [, id] = new URL(request.url()).pathname.match(/\/accounts\/([^/]+)\/transactions/) ?? []
        // menunest-207/212: AccountDetailPage's loading gate is `data?.account`,
        // so a Credit or Loan account visited directly must resolve to ITS OWN
        // row here, not fall back to the cash fixture — otherwise the page
        // never gets past "Loading…" for those accounts.
        const acct = (id && accountsById[id]) || accountCash
        await route.fulfill({
          json: { account: { ...acct, monthInflow: 0, monthOutflow: 0 }, items: [], hasMore: false },
        })
      })
      await page.route(/\/api\/budget\/payments(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        if (request.method() === 'POST') {
          await route.fulfill({ status: 201, json: paymentFixture })
          return
        }
        await route.fulfill({ status: 404, json: { message: 'not mocked' } })
      })
    },
  }
  return self
}

export type BudgetMocks = ReturnType<typeof createBudgetMocks>
