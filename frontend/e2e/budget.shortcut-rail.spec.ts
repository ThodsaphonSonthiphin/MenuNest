import { expect, type Page } from '@playwright/test'
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

    // …and it is indigo. The theme paints .e-btn.e-primary Material pink at
    // the same specificity, which is what the button shipped as until this
    // was measured.
    const paint = await page.getByTestId('bdg-rail-fab').evaluate((el) => {
      const cs = getComputedStyle(el)
      return {
        bg: cs.backgroundColor,
        shadow: cs.boxShadow,
        glyph: getComputedStyle(el.querySelector('.bdg-rail-glyph')!, '::before').content,
      }
    })
    expect(paint.bg).toBe('rgb(79, 70, 229)')
    expect(paint.shadow).toContain('rgba(79, 70, 229, 0.45)')
    expect(paint.glyph).toContain('\u22ee')
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

    const rects = await items.evaluateAll((els) =>
      els.map((el) => {
        const r = el.getBoundingClientRect()
        return { top: r.top, bottom: r.bottom, w: r.width, h: r.height }
      }),
    )
    expect(rects[0].top).toBeGreaterThan(rects[1].top)
    expect(rects[1].top).toBeGreaterThan(rects[2].top)

    // The mock's numbers: 44x44 circles, gap 10 between them, gap 12 to the
    // main button — Syncfusion's own list padding and item margins give 22.
    const fab = (await page.getByTestId('bdg-rail-fab').boundingBox())!
    for (const r of rects) {
      expect(r.w).toBe(44)
      expect(r.h).toBe(44)
      expect(r.top).toBeGreaterThan(0) // on screen at all
    }
    expect(Math.round(fab.y - rects[0].bottom)).toBe(12)
    expect(Math.round(rects[0].top - rects[1].bottom)).toBe(10)
    expect(Math.round(rects[1].top - rects[2].bottom)).toBe(10)

    // The label rides in a pill beside the circle, not as text inside it —
    // Syncfusion's linear mode would otherwise paint the raw text in the 44px
    // circle, which is what the itemTemplate replaces.
    await expect(items.nth(0).locator('.bdg-rail-tag')).toHaveText(/Undo/)
    await expect(items.nth(0).locator('.bdg-rail-ico')).toHaveText('\u21b6')

    // The main button turns into a close affordance while open.
    const glyph = await page
      .getByTestId('bdg-rail-fab')
      .evaluate((el) => getComputedStyle(el.querySelector('.bdg-rail-glyph')!, '::before').content)
    expect(glyph).toContain('\u00d7')

    // …and the scrim actually dims the page behind it (menunest-192).
    const overlay = page.locator('.bdg-rail .e-speeddial-overlay')
    await expect(overlay).toHaveCSS('background-color', 'rgba(15, 23, 42, 0.2)')
  })

  test('the rail hides on a downward flick and comes back', async ({ authedPage: page }) => {
    await page.goto('/budget')
    const fab = page.getByTestId('bdg-rail-fab')
    await expect(fab).toBeVisible()

    await page.evaluate(() => {
      document.body.style.minHeight = '3000px'
      window.scrollTo(0, 600)
    })
    // menunest-192's exact transform, straight off the mock's spec table.
    await expect(page.getByTestId('bdg-rail')).toHaveClass(/is-hidden/)
    await expect(fab).toHaveCSS('transform', 'matrix(0.9, 0, 0, 0.9, 0, 96)')
    await expect(fab).toHaveCSS('opacity', '0')

    // It returns on its own once the flick stops.
    await expect(page.getByTestId('bdg-rail')).not.toHaveClass(/is-hidden/, { timeout: 3_000 })
    await expect(fab).toHaveCSS('opacity', '1')
  })

  /** Opens the rail and the Change history sheet. */
  async function openHistory(page: Page) {
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-rail-fab')).toBeVisible()
    await page.getByTestId('bdg-rail-fab').click()
    await page.getByText('Change history', { exact: false }).click()
    await expect(page.getByTestId('bdg-history-sheet')).toBeVisible()
  }

  test('Change history lists every row, undone and unpressable ones included', async ({
    authedPage: page,
  }) => {
    await openHistory(page)

    // menunest-195: an undone row STAYS on the list so it can be redone.
    // menunest-197 / menunest-216: so does a dead one and somebody else's.
    await expect(page.getByTestId('bdg-history-row')).toHaveCount(5)
    await expect(page.getByTestId('bdg-history-undo')).toHaveCount(3)
    await expect(page.getByTestId('bdg-history-redo')).toHaveCount(2)
  })

  test('undoing from the sheet posts to the undo endpoint', async ({
    authedPage: page,
    capturedRequests,
  }) => {
    await openHistory(page)
    // The caller is an ordinary member, so the first two Undo buttons belong to
    // rows they may not press. chg-2 is the newest row that is theirs.
    await page.getByTestId('bdg-history-row').nth(2).getByTestId('bdg-history-undo').click()

    const req = await capturedRequests.waitFor('POST', /\/api\/budget\/history\/[^/]+\/undo$/)
    expect(req.pathname).toContain('chg-2')
  })

  test('a row you may not undo is disabled but NOT greyed like a dead one', async ({
    authedPage: page,
  }) => {
    // menunest-216 §4. This is the assertion nothing else in the toolchain can
    // make: tsc, the build and vitest are all blind to whether the row renders
    // dimmed, and the decision is precisely that it must not.
    await openHistory(page)

    const foreign = page.getByTestId('bdg-history-row').nth(0)   // มาลี's row
    const dead = page.getByTestId('bdg-history-row').nth(1)      // deleted envelope

    await expect(foreign.getByTestId('bdg-history-undo')).toBeDisabled()
    await expect(dead.getByTestId('bdg-history-undo')).toBeDisabled()

    // Permanent shouts in red (--red #b91c1c) and dims the row…
    await expect(dead).toHaveClass(/is-dead/)
    await expect(dead.getByTestId('bdg-history-blocked'))
      .toHaveCSS('color', 'rgb(185, 28, 28)')

    // …temporary speaks quietly (--text-muted #475569) and keeps full strength.
    await expect(foreign).not.toHaveClass(/is-dead/)
    await expect(foreign.getByTestId('bdg-history-note'))
      .toHaveCSS('color', 'rgb(71, 85, 105)')
  })

  test('the family head\'s undo sticks: the author gets a disabled Redo', async ({
    authedPage: page,
  }) => {
    // menunest-216 §2. chg-0 is the caller's OWN change, undone by มาลี. Under
    // the one-flag shape this button was enabled and the request failed.
    await openHistory(page)

    const stuck = page.getByTestId('bdg-history-row').nth(4)
    await expect(stuck.getByTestId('bdg-history-redo')).toBeDisabled()
    await expect(stuck.getByTestId('bdg-history-note')).toBeVisible()

    // …while a row the caller undid themselves is still redoable.
    await expect(
      page.getByTestId('bdg-history-row').nth(3).getByTestId('bdg-history-redo'),
    ).toBeEnabled()
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
