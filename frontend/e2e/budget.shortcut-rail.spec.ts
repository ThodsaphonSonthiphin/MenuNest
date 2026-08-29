import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

/**
 * Per CLAUDE.md this is the ONLY automatic gate that can catch a rendering bug,
 * and only for what it exercises — #97 shipped a page rendered as raw unstyled
 * checkboxes because no spec ever touched it, and #46 shipped a mockup-backed
 * screen that diverged from its approved mock through every other gate. So the
 * geometry test below asserts the mock's numbers, not just "something rendered".
 *
 * The routes are mocked (helpers/mockRoutes/budgetRoutes.ts) because
 * playwright.config.ts deliberately serves the SPA with no backend behind it.
 *
 * Note `[data-testid="bdg-rail"]` is `display: contents` and has no box of its
 * own — Syncfusion positions the button itself. Assert presence on that, and
 * anything geometric on `bdg-rail-fab`.
 */
test.describe('Budget — shortcut rail', () => {
  test.beforeEach(async ({ mockApi }) => {
    await mockApi.budget.apply()
  })

  test('the rail renders on /budget', async ({ authedPage: page }) => {
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-page')).toBeVisible()
    await expect(page.getByTestId('bdg-rail-fab')).toBeVisible()
  })

  test('the resting button matches the approved mock', async ({ authedPage: page }) => {
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-rail-fab')).toBeVisible()

    // docs/mocks/budget-shortcut-rail-mock.html: 52x52, right 16, bottom 22.
    // Syncfusion's own .e-fab-bottom is 16px, so this fails the moment the
    // override in BudgetPage.css stops winning on specificity.
    const box = (await page.getByTestId('bdg-rail-fab').boundingBox())!
    const viewport = page.viewportSize()!
    expect(box.width).toBe(52)
    expect(box.height).toBe(52)
    expect(viewport.width - (box.x + box.width)).toBe(16)
    expect(viewport.height - (box.y + box.height)).toBe(22)
  })

  test('pressing the rail expands upward to three actions in order', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-rail-fab')).toBeVisible()
    await page.getByTestId('bdg-rail-fab').click()

    // menunest-191 fixes both the contents and the order: undo nearest the
    // thumb, then redo, then change history, stacking UPWARD (menunest-192).
    const items = page.locator('.bdg-rail .e-speeddial-li')
    await expect(items).toHaveCount(3)
    await expect(items.nth(0)).toContainText('Undo')
    await expect(items.nth(1)).toContainText('Redo')
    await expect(items.nth(2)).toContainText('Change history')

    const boxes = await items.evaluateAll((els) =>
      els.map((el) => el.getBoundingClientRect().top),
    )
    expect(boxes[0]).toBeGreaterThan(boxes[1])
    expect(boxes[1]).toBeGreaterThan(boxes[2])
    // …and every one of them is actually on screen, which is what broke when
    // the wrapper tried to own the positioning instead of Syncfusion.
    const fab = (await page.getByTestId('bdg-rail-fab').boundingBox())!
    for (const top of boxes) {
      expect(top).toBeGreaterThan(0)
      expect(top).toBeLessThan(fab.y)
    }
  })

  test('Change history opens a sheet listing both rows', async ({ authedPage: page }) => {
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-rail-fab')).toBeVisible()
    await page.getByTestId('bdg-rail-fab').click()
    await page.getByText('Change history', { exact: false }).click()

    await expect(page.getByTestId('bdg-history-sheet')).toBeVisible()
    // menunest-195: an undone row STAYS on the list so it can be redone, so the
    // sheet shows one Undo button and one Redo button, not one row.
    await expect(page.getByTestId('bdg-history-row')).toHaveCount(2)
    await expect(page.getByTestId('bdg-history-undo')).toHaveCount(1)
    await expect(page.getByTestId('bdg-history-redo')).toHaveCount(1)
  })

  test('undoing from the sheet posts to the undo endpoint', async ({
    authedPage: page,
    capturedRequests,
  }) => {
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-rail-fab')).toBeVisible()
    await page.getByTestId('bdg-rail-fab').click()
    await page.getByText('Change history', { exact: false }).click()
    await page.getByTestId('bdg-history-undo').click()

    const req = await capturedRequests.waitFor('POST', /\/api\/budget\/history\/[^/]+\/undo$/)
    expect(req.pathname).toContain('chg-2')
  })

  test('the rail does NOT render on the account detail page', async ({ authedPage: page }) => {
    // menunest-199: opt-in means AccountDetailPage declares no rail, which is
    // why the .bdg-fab corner never had to be negotiated. This click also
    // proves the speed dial's scrim is not swallowing presses while at rest.
    await page.goto('/budget')
    await page.getByTestId('bdg-account-card').first().click()

    await expect(page.getByTestId('bdg-account-page')).toBeVisible()
    await expect(page.getByTestId('bdg-fab')).toBeVisible()
    await expect(page.getByTestId('bdg-rail')).toHaveCount(0)
  })
})
