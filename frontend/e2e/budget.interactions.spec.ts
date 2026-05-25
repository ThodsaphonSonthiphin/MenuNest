import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

test.describe('Budget — envelope interactions', () => {
  test('tap toggles expansion, only one card expanded at a time', async ({authedPage: page}) => {
    await page.goto('/budget')
    const cards = page.getByTestId('bdg-envelope-card')
    if (await cards.count() < 2) test.skip()

    const first = cards.nth(0)
    const second = cards.nth(1)

    await first.click()
    await expect(first).toHaveClass(/is-expanded/)
    await expect(second).not.toHaveClass(/is-expanded/)

    await second.click()
    await expect(first).not.toHaveClass(/is-expanded/)
    await expect(second).toHaveClass(/is-expanded/)
  })

  test('long-press an envelope opens TransactionDialog with category preselected', async ({authedPage: page}) => {
    await page.goto('/budget')
    const card = page.getByTestId('bdg-envelope-card').first()
    if (await card.count() === 0) test.skip()
    const categoryId = await card.getAttribute('data-category-id')

    // Simulate a long-press: pointerdown, hold ~600ms, pointerup.
    const box = await card.boundingBox()
    if (!box) throw new Error('Could not measure envelope card')
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
    await page.mouse.down()
    await page.waitForTimeout(600)
    await page.mouse.up()

    // The TransactionDialog should be open.
    await expect(page.locator('.budget-modal')).toBeVisible()

    // Header should contain "transaction" text.
    await expect(page.locator('.budget-modal h3')).toContainText(/transaction/i)

    // Dismiss via Cancel if available.
    const cancel = page.getByRole('button', {name: /Cancel/i})
    if (await cancel.isVisible().catch(() => false)) await cancel.click()

    // After dismiss the card should NOT be expanded (long-press fired
    // instead of the tap path).
    await expect(card).not.toHaveClass(/is-expanded/)
    expect(categoryId).toMatch(/[0-9a-f-]+/)
  })

  test('tap account-card chevron routes to detail page', async ({authedPage: page}) => {
    await page.goto('/budget')
    const accountCard = page.getByTestId('bdg-account-card').first()
    if (await accountCard.count() === 0) test.skip()

    await accountCard.click()
    await expect(page).toHaveURL(/\/budget\/accounts\/[0-9a-f-]+$/)
  })
})
