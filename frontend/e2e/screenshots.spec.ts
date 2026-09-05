import {mkdirSync, readFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'
import {expect, type Page} from '@playwright/test'
import {test} from './fixtures/healthFixture'
import {budgetSummaryFixture} from './helpers/mockRoutes/budgetRoutes'

/**
 * Regenerates the README's screenshots. Opt-in — it writes files into the
 * repo, so it must never run as part of the normal e2e suite:
 *
 *   SHOOT=1 npx playwright test screenshots.spec.ts
 *
 * Every screen is driven against mocked API responses, so no backend, no
 * Azure, no SQL and no real health or financial data is involved.
 */

const baseDir = dirname(fileURLToPath(import.meta.url))
const OUT = resolve(baseDir, '../../docs/images')

const SYMPTOMS = [{id: 'symptom-migraine', name: 'Migraine', isCustom: false}]
const TRIGGERS = [{id: 'trigger-stress', name: 'Stress', isCustom: false}]

/**
 * `mocks/reports/full-report.json` ships `days: []` — it was written for
 * text-presence assertions in `health.doctor-report.spec.ts`, which never
 * needed populated days. But four of the doctor-report page's sections
 * (`FrequencyChart`, `LocationQuality`, `AuraSymptomsSection`, `DailyTimeline`
 * — see `src/pages/health/components/DoctorReport/`) derive entirely from
 * `days`, so an empty array leaves them blank, and `AuraSymptomsSection`'s
 * own tally (0/8, 0%) then visibly contradicts the header's aura stat, which
 * reads straight off `summary` (3 attacks, 38%) instead.
 *
 * Rather than edit the shared fixture — `health.doctor-report.spec.ts`
 * asserts against it as-is — this builds a days array LOCAL to the
 * screenshot spec that reconciles with the existing `summary` block:
 *   - 8 attacks across 7 days (one day carries 2) → totalAttacks 8,
 *     daysAffected 7
 *   - hasAura on exactly 3 of them → attacksWithAura 3 / auraPercentage
 *     37.5, matching AuraSymptomsSection's own from-days tally
 *   - severity >= 8 on exactly 2 → severeAttacksCount 2
 *   - FunctionalImpact.SevereBedrest on exactly 1 → daysFullyDisabled 1
 *   - a dosed intake on 6 of the 7 days (the 7th has noDrugTaken instead)
 *     → acuteMedDays 6
 *   - locations and qualities spread across all three/four buckets so
 *     both LocationQuality donuts draw non-zero segments
 */
const REPORT_EPISODE_DEFAULTS = {
  symptomId: 'symptom-migraine',
  symptomName: 'Migraine',
  severityAfter: null,
  isOnPeriod: false,
  noDrugTaken: false,
  noDrugReasonCode: null as number | null,
  followUps: [] as unknown[],
  triggerIds: [] as string[],
}

const dose = (takenAtIso: string) => [{takenAt: takenAtIso, drugName: 'Ibuprofen', doseAmount: 1}]

const REPORT_DAYS = [
  {
    date: '2026-04-20', isPeriodDay: false, attackCount: 1, peakSeverity: 5, doseCount: 1, noDrugEvents: 0,
    episodes: [{
      ...REPORT_EPISODE_DEFAULTS, id: 'ep-1',
      startedAt: '2026-04-20T09:15:00Z', endedAt: '2026-04-20T14:15:00Z',
      severity: 5, hasAura: false, location: 2 /* Right */, quality: 1 /* Throbbing */,
      associatedSymptoms: [1] /* Nausea */, functionalImpact: 3 /* Moderate */,
      intakes: dose('2026-04-20T09:45:00Z'),
    }],
  },
  {
    date: '2026-04-25', isPeriodDay: false, attackCount: 1, peakSeverity: 8, doseCount: 1, noDrugEvents: 0,
    episodes: [{
      ...REPORT_EPISODE_DEFAULTS, id: 'ep-2',
      startedAt: '2026-04-25T22:00:00Z', endedAt: '2026-04-26T05:00:00Z',
      severity: 8, hasAura: true, location: 1 /* Left */, quality: 1 /* Throbbing */,
      associatedSymptoms: [1, 3, 4] /* Nausea, Photophobia, Phonophobia */, functionalImpact: 4 /* SevereBedrest */,
      intakes: dose('2026-04-25T22:30:00Z'),
    }],
  },
  {
    date: '2026-04-28', isPeriodDay: false, attackCount: 1, peakSeverity: 5, doseCount: 0, noDrugEvents: 1,
    episodes: [{
      ...REPORT_EPISODE_DEFAULTS, id: 'ep-3', noDrugTaken: true, noDrugReasonCode: 9 /* UserSkip */,
      startedAt: '2026-04-28T08:00:00Z', endedAt: '2026-04-28T11:00:00Z',
      severity: 5, hasAura: false, location: 3 /* Bilateral */, quality: 2 /* Pressure */,
      associatedSymptoms: [], functionalImpact: 2 /* Mild */,
      intakes: [],
    }],
  },
  {
    date: '2026-05-02', isPeriodDay: false, attackCount: 2, peakSeverity: 7, doseCount: 2, noDrugEvents: 0,
    episodes: [
      {
        ...REPORT_EPISODE_DEFAULTS, id: 'ep-4a',
        startedAt: '2026-05-02T07:00:00Z', endedAt: '2026-05-02T13:00:00Z',
        severity: 7, hasAura: true, location: 2 /* Right */, quality: 1 /* Throbbing */,
        associatedSymptoms: [3, 4] /* Photophobia, Phonophobia */, functionalImpact: 3 /* Moderate */,
        intakes: dose('2026-05-02T07:30:00Z'),
      },
      {
        ...REPORT_EPISODE_DEFAULTS, id: 'ep-4b',
        startedAt: '2026-05-02T18:00:00Z', endedAt: '2026-05-02T22:00:00Z',
        severity: 7, hasAura: false, location: 1 /* Left */, quality: 3 /* Stabbing */,
        associatedSymptoms: [1] /* Nausea */, functionalImpact: 2 /* Mild */,
        intakes: dose('2026-05-02T18:30:00Z'),
      },
    ],
  },
  {
    date: '2026-05-05', isPeriodDay: false, attackCount: 1, peakSeverity: 9, doseCount: 1, noDrugEvents: 0,
    episodes: [{
      ...REPORT_EPISODE_DEFAULTS, id: 'ep-5',
      startedAt: '2026-05-05T02:00:00Z', endedAt: '2026-05-05T10:00:00Z',
      severity: 9, hasAura: true, location: 1 /* Left */, quality: 1 /* Throbbing */,
      associatedSymptoms: [1, 2, 3, 4, 5] /* Nausea, Vomiting, Photophobia, Phonophobia, Osmophobia */,
      functionalImpact: 3 /* Moderate */,
      intakes: dose('2026-05-05T02:30:00Z'),
    }],
  },
  {
    date: '2026-05-10', isPeriodDay: false, attackCount: 1, peakSeverity: 7, doseCount: 1, noDrugEvents: 0,
    episodes: [{
      ...REPORT_EPISODE_DEFAULTS, id: 'ep-6',
      startedAt: '2026-05-10T13:00:00Z', endedAt: '2026-05-10T18:00:00Z',
      severity: 7, hasAura: false, location: 2 /* Right */, quality: 2 /* Pressure */,
      associatedSymptoms: [3] /* Photophobia */, functionalImpact: null,
      intakes: dose('2026-05-10T13:30:00Z'),
    }],
  },
  {
    date: '2026-05-14', isPeriodDay: false, attackCount: 1, peakSeverity: 6, doseCount: 1, noDrugEvents: 0,
    episodes: [{
      ...REPORT_EPISODE_DEFAULTS, id: 'ep-7',
      startedAt: '2026-05-14T20:00:00Z', endedAt: '2026-05-15T01:00:00Z',
      severity: 6, hasAura: false, location: 3 /* Bilateral */, quality: 1 /* Throbbing */,
      associatedSymptoms: [], functionalImpact: null,
      intakes: dose('2026-05-14T20:30:00Z'),
    }],
  },
]

const fullReportFixture = JSON.parse(
  readFileSync(resolve(baseDir, 'mocks/reports/full-report.json'), 'utf-8'),
)
const REPORT_WITH_DAYS = {...fullReportFixture, days: REPORT_DAYS}

/**
 * `BudgetPage`'s month strip reads `year`/`month` from Redux state, which
 * defaults to `new Date()` (see `budgetSlice.ts`) — i.e. whatever "now" is
 * when the page opens. `budgetSummaryFixture` is pinned to a fixed August
 * 2026, which `RtaHero` prints straight from `summary.year`/`summary.month`
 * (`RtaHero.tsx:49`). Left alone, the strip and the hero card show two
 * different months, and it drifts further every real month that passes.
 * Overriding the summary's year/month to "now" at shoot time keeps both
 * elements naming the same month, indefinitely.
 */
const now = new Date()
const BUDGET_SUMMARY_FOR_SHOOT = {
  ...budgetSummaryFixture,
  year: now.getFullYear(),
  month: now.getMonth() + 1,
}

/**
 * No real `VITE_SYNCFUSION_LICENSE_KEY` is configured for this environment
 * (`.env.example` ships it blank), so any page that mounts a Syncfusion
 * component injects a fixed, full-width trial banner
 * (`@syncfusion/ej2-base`'s `validate-lic.js`) directly onto `document.body`
 * — with no id/class, only matchable by its text. Left in place it covers
 * the top of the screen in every screenshot. It is injected once per full
 * page load, so removing it right before each shot is enough.
 */
async function hideSyncfusionBanner(page: Page) {
  await page.evaluate(() => {
    document.querySelectorAll('body > div').forEach((el) => {
      if (el.textContent?.includes('trial version of Syncfusion')) el.remove()
    })
  })
}

test.describe('README screenshots', () => {
  test.use({viewport: {width: 1280, height: 800}})

  test.beforeEach(() => {
    test.skip(!process.env.SHOOT, 'Set SHOOT=1 to regenerate docs/images/*.png')
    mkdirSync(OUT, {recursive: true})
  })

  test('budget', async ({authedPage: page, mockApi}) => {
    await mockApi.budget.summary(BUDGET_SUMMARY_FOR_SHOOT).apply()
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-rta-hero')).toBeVisible()
    await page.waitForLoadState('networkidle')
    await hideSyncfusionBanner(page)
    await page.screenshot({path: `${OUT}/budget.png`})
  })

  test('health quick log', async ({authedPage: page, mockApi}) => {
    await mockApi.episodes.activeNone().startSuccess().apply()
    await page.route('**/api/symptoms', (route) => route.fulfill({json: SYMPTOMS}))
    await page.route('**/api/triggers', (route) => route.fulfill({json: TRIGGERS}))
    await page.goto('/health/log')
    await expect(page.getByRole('button', {name: /บันทึก attack/})).toBeEnabled()
    await page.waitForLoadState('networkidle')
    await hideSyncfusionBanner(page)
    await page.screenshot({path: `${OUT}/health-quick-log.png`})
  })

  test('doctor report', async ({page, mockApi}) => {
    await mockApi.report.publicReport(REPORT_WITH_DAYS).apply()
    await page.goto('/share/valid-token-abc')
    await expect(page.getByText('ทดสอบ ใจดี')).toBeVisible()
    await page.waitForLoadState('networkidle')
    await hideSyncfusionBanner(page)
    await page.screenshot({path: `${OUT}/doctor-report.png`, fullPage: true})
  })

  test('trips', async ({authedPage: page, mockApi}) => {
    await mockApi.trips.apply()
    await page.goto('/trips')
    await expect(page.getByRole('heading', {name: /ทริปของฉัน/})).toBeVisible()
    await page.waitForLoadState('networkidle')
    await hideSyncfusionBanner(page)
    await page.screenshot({path: `${OUT}/trips.png`})
  })

  // The brief's original version of this case went straight to /ai-assistant
  // and screenshotted without selecting a conversation. That would capture an
  // empty chat pane: activeConversationId starts null
  // (aiAssistantSlice.ts), the messages query is skipped until one is set
  // (useAiAssistant.ts), and nothing on the page auto-selects the first
  // conversation. Clicking the conversation and asserting a real message is
  // visible before shooting is required to get a populated screenshot.
  test('ai assistant', async ({authedPage: page, mockApi}) => {
    await mockApi.chat.apply()
    await page.goto('/ai-assistant')
    await expect(page.getByRole('heading', {name: 'AI Assistant'})).toBeVisible()
    await page.getByText('เมนูเย็นนี้').click()
    await expect(page.getByText(/มีไข่กับหมูสับ/)).toBeVisible()
    await page.waitForLoadState('networkidle')
    await hideSyncfusionBanner(page)
    await page.screenshot({path: `${OUT}/ai-assistant.png`})
  })
})
