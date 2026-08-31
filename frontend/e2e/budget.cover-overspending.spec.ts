import {expect, type Page} from '@playwright/test'
import {test} from './fixtures/healthFixture'
import {budgetSummaryFixture} from './helpers/mockRoutes/budgetRoutes'

/**
 * Rendering + wire cover for issue #115 / menunest-215 — "cover overspending
 * ต้องมีเงินที่ยังไม่ budget ให้เลือกด้วย".
 *
 * `CoverOverspendingDialog` had NO spec of any kind. Per CLAUDE.md, Playwright
 * is the only automatic gate that can catch a rendering bug, and vitest here
 * runs in `environment: 'node'` — so `coverSourceOptions`' unit tests prove the
 * list is BUILT right and prove nothing at all about it being SEEN. A Syncfusion
 * `DropDownList` renders its list into a popup outside the form; #33 shipped
 * exactly such a popup clipped invisible by an `overflow:hidden` ancestor, green
 * through every other gate.
 *
 * The wire half matters just as much: `fromCategoryId: null` is what tells
 * `CoverOverspendingHandler` to increment the overspent envelope alone rather
 * than move money out of a source. Nothing between the dropdown and the request
 * body is typed against that sentinel, so it is asserted on the request itself.
 */

/** #115's state: ค่ากิน overspent by ฿110 with ฿893.81 still to place. */
const overspentSummary = (readyToAssign: number) => {
  const [first, ...rest] = budgetSummaryFixture.groups
  return {
    ...budgetSummaryFixture,
    readyToAssign,
    groups: [
      {
        ...first,
        categories: first.categories.map((c, i) =>
          i === 0 ? {...c, available: -110, assigned: 0} : c),
      },
      ...rest,
    ],
  }
}

/** The source dropdown's popup list items, which render OUTSIDE the dialog. */
const sourceOptions = (page: Page) => page.locator('.sf-list-item, li[role="option"]')

/**
 * The Ready to Assign entry, matched on its label rather than on a position or
 * a total — the fixture carries a Payment envelope in its own group that is
 * also a legal source, so any count assertion here pins the fixture's shape
 * instead of #115's behaviour.
 */
const readyToAssignOption = (page: Page) =>
  sourceOptions(page).filter({hasText: 'เงินที่ยังไม่ได้จัดสรร'})

const openCoverDialog = async (page: Page) => {
  await page.goto('/budget')
  await page.getByTestId('bdg-env-cover-icon').first().click()
  await expect(page.getByRole('heading', {name: 'Cover Overspending'})).toBeVisible()
  // The Syncfusion input intercepts its own pointer events; the wrapper is what
  // a real finger lands on.
  await page.locator('.budget-modal-field').first().locator('.sf-input-group').first().click()
}

test.describe('Budget — Cover Overspending can draw on Ready to Assign (#115)', () => {
  test('lists Ready to Assign first, with its amount, and renders it unclipped', async ({
    mockApi, authedPage: page,
  }) => {
    await mockApi.budget.summary(overspentSummary(893.81)).apply()
    await openCoverDialog(page)

    // Before #115 the list was envelopes only and this entry did not exist.
    const rta = readyToAssignOption(page)
    await expect(rta).toHaveCount(1)
    await expect(rta).toBeVisible()
    await expect(rta).toContainText('฿893.81')
    // It LEADS: unplaced money is the first thing offered, ahead of any envelope.
    await expect(sourceOptions(page).first()).toContainText('เงินที่ยังไม่ได้จัดสรร')

    // The #33 failure mode: present in the DOM, painted nowhere. A zero-area or
    // off-viewport box is the shape a clipped popup takes.
    const box = await rta.boundingBox()
    expect(box, 'the Ready to Assign option has no layout box at all').not.toBeNull()
    expect(box!.width).toBeGreaterThan(0)
    expect(box!.height).toBeGreaterThan(0)
    const viewport = page.viewportSize()!
    expect(box!.y).toBeGreaterThanOrEqual(0)
    expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height)
  })

  test('picking it sends fromCategoryId: null, not a category id', async ({
    mockApi, authedPage: page, capturedRequests,
  }) => {
    await mockApi.budget.summary(overspentSummary(893.81)).apply()
    await openCoverDialog(page)

    await readyToAssignOption(page).click()
    await page.getByRole('button', {name: 'Cover', exact: true}).click()

    const req = await capturedRequests.waitFor('POST', '/api/budget/monthly/cover')
    const body = req.body as {fromCategoryId: unknown; amount: number}
    expect(body.fromCategoryId, 'a sentinel string here would 400 at the validator').toBeNull()
    expect(body.amount).toBe(110)
  })

  test('offers only envelopes when every baht already has a job', async ({
    mockApi, authedPage: page,
  }) => {
    await mockApi.budget.summary(overspentSummary(0)).apply()
    await openCoverDialog(page)

    // Envelopes are still offered; an empty Ready to Assign is not — an entry
    // reading "(฿0.00)" would be a source that can fund nothing.
    await expect(sourceOptions(page).first()).toBeVisible()
    await expect(readyToAssignOption(page)).toHaveCount(0)
  })
})
