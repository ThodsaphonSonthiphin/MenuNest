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

const account = {
  id: 'acct-1',
  name: 'เงินสด',
  type: 'Cash',
  balance: 5000,
  sortOrder: 0,
  isClosed: false,
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
})

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
  ],
  accounts: [account],
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
      await page.route(/\/api\/budget\/accounts\/[^/]+\/transactions(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ json: { account: { ...account, monthInflow: 0, monthOutflow: 0 }, items: [], hasMore: false } })
      })
    },
  }
  return self
}

export type BudgetMocks = ReturnType<typeof createBudgetMocks>
