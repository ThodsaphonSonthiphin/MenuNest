import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

/**
 * e2e coverage for the Daily allowance card (menunest-181/184/185/186) and
 * the ✎ one-tap balance-correction affordance on the account strip
 * (menunest one-tap-affordances rework, #99 Task 9).
 *
 * This repo has no component/visual test harness — vitest runs with no
 * jsdom, so tsc/build/vitest never render anything. Playwright is the only
 * mechanism here that can catch a rendering bug automatically, and only
 * for what it actually exercises (see CLAUDE.md: #46, #97). These specs
 * hit the real backend (no mockRoutes exist for /budget, unlike the health
 * fixtures' mockApi), so — matching the style already used by the sibling
 * budget specs — each test verifies its live-data precondition and skips
 * rather than asserting against state it cannot control or seed.
 */
test.describe('Budget — daily allowance card', () => {
  test('with no everyday envelope marked, the empty state shows and no figure renders', async ({authedPage: page}) => {
    await page.goto('/budget')
    const figure = page.getByTestId('bdg-daily-allowance')
    // Precondition: nothing marked yet this month. If a figure is already
    // rendering, some earlier marks exist in this live dataset — the
    // no-marks state can't be demonstrated here.
    if (await figure.count() > 0) test.skip()

    const empty = page.getByTestId('bdg-daily-allowance-empty')
    // dailyAllowance can also be null outright (viewed month isn't the
    // real current month, menunest-185) — DailyAllowanceCard renders
    // nothing at all in that case, so there is no empty-state to assert.
    if (await empty.count() === 0) test.skip()

    await expect(empty).toBeVisible()
    // The defining bug this guards against: hasMarks === false must never
    // render a figure — only the empty invitation copy.
    await expect(figure).toHaveCount(0)
    await expect(empty).toContainText(/no everyday envelopes marked/i)
  })

  test('tapping the card opens the everyday sheet, listing envelopes from more than one group', async ({authedPage: page}) => {
    await page.goto('/budget')
    // Either rendered state of the card opens the same sheet — both wire
    // the identical onOpenMarks callback (DailyAllowanceCard.tsx).
    const trigger = page.getByTestId('bdg-daily-allowance-empty').or(page.getByTestId('bdg-daily-allowance'))
    if (await trigger.count() === 0) test.skip()

    await trigger.first().click()
    const sheet = page.getByTestId('bdg-everyday-sheet')
    await expect(sheet).toBeVisible()

    // The mark lives on the Envelope and is group-independent — a sheet
    // that filtered by group would be a real bug (menunest-181). Assert
    // more than one *distinct* group heading is actually listed, not
    // just that the sheet has more than one row.
    const groupHeaders = sheet.locator('.bdg-everyday-group-header')
    const groupCount = await groupHeaders.count()
    if (groupCount < 2) test.skip()
    const names = await groupHeaders.allTextContents()
    expect(new Set(names).size).toBeGreaterThan(1)

    await expect(sheet.getByTestId('bdg-everyday-row').first()).toBeVisible()

    // Close without changing anything — the ticked state matches how the
    // sheet opened, so Done sends an empty diff (no mutation) rather than
    // leaving the sheet open for the next test.
    await page.getByRole('button', {name: /Done/i}).click()
    await expect(sheet).not.toBeAttached()
  })

  test('ticking two rows and closing commits, and a figure renders in the card', async ({authedPage: page}) => {
    await page.goto('/budget')
    const trigger = page.getByTestId('bdg-daily-allowance-empty').or(page.getByTestId('bdg-daily-allowance'))
    if (await trigger.count() === 0) test.skip()

    await trigger.first().click()
    const sheet = page.getByTestId('bdg-everyday-sheet')
    await expect(sheet).toBeVisible()

    const rows = sheet.getByTestId('bdg-everyday-row')
    if (await rows.count() < 2) test.skip()

    // .check() drives both rows to checked=true regardless of their
    // current state, so the diff against however the sheet opened is
    // guaranteed non-empty and guaranteed to leave >=2 envelopes marked
    // everyday — the outcome this test needs, independent of whatever an
    // earlier run of this live dataset already marked.
    await rows.nth(0).locator('input[type="checkbox"]').check()
    await rows.nth(1).locator('input[type="checkbox"]').check()

    await page.getByRole('button', {name: /Done/i}).click()

    // Closing is the commit point: exactly one bulk request, then the
    // sheet closes and the frozen figure renders.
    await expect(sheet).not.toBeAttached()
    await expect(page.getByTestId('bdg-daily-allowance')).toBeVisible()
    await expect(page.getByTestId('bdg-daily-allowance-empty')).toHaveCount(0)
  })

  test('pressing ‹ to a past month, the card is absent — current-month only', async ({authedPage: page}) => {
    await page.goto('/budget')

    // Unguarded on purpose: assert the card renders in SOME form on the
    // current month BEFORE navigating away. Without this, a total render
    // failure (the component throws, returns null unexpectedly, or loses
    // its test ids) would make the post-navigation absence assertion
    // below trivially true — the exact #97-class swallow this file exists
    // to prevent (menunest-185 only makes sense to check once we know the
    // card can render at all).
    const currentMonthCard = page.getByTestId('bdg-daily-allowance').or(page.getByTestId('bdg-daily-allowance-empty'))
    await expect(currentMonthCard).toBeVisible()

    const monthLabel = page.locator('.bdg-month-label')
    const before = await monthLabel.textContent()

    await page.getByRole('button', {name: 'Previous month'}).click()

    // Confirm navigation actually happened, so an absent card is evidence
    // of the current-month-only rule and not a stuck/broken click.
    await expect(monthLabel).not.toHaveText(before ?? '')

    // menunest-185: dailyAllowance is null for any month that isn't the
    // real current month — DailyAllowanceCard returns null outright, so
    // *neither* rendered state (filled or empty) should be present.
    await expect(page.getByTestId('bdg-daily-allowance')).toHaveCount(0)
    await expect(page.getByTestId('bdg-daily-allowance-empty')).toHaveCount(0)
  })

  test('✎ on an account card opens the reconcile dialog', async ({authedPage: page}) => {
    await page.goto('/budget')
    // Guard only on data availability (does an account exist at all) —
    // same shape as budget.add-entry-points.spec.ts's reconcile-menu test.
    // Guarding on the icon itself would make the test silently skip
    // instead of failing if the icon were hidden, dropped from
    // AccountsStrip.tsx, or broken by DOM restructuring (exactly the kind
    // of change c627f0a just made) — on the newest, least-verified
    // surface in the branch, that is the one guard that must not exist.
    if (await page.getByTestId('bdg-account-card').count() === 0) test.skip()

    const correctIcon = page.getByTestId('bdg-account-correct-icon').first()
    await expect(correctIcon).toBeVisible()
    await correctIcon.click()
    // Un-nested from the account-card's stretched link (c627f0a) — this
    // must open the dialog directly, never navigate to account-detail.
    await expect(page).toHaveURL(/\/budget$/)
    await expect(page.getByTestId('bdg-reconcile-dialog')).toBeVisible()
    await expect(page.locator('.budget-modal h3')).toContainText(/reconcile/i)
  })
})
