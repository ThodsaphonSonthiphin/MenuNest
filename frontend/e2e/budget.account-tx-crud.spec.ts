import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

async function goToAccountWithRows(page: import('@playwright/test').Page): Promise<boolean> {
  await page.goto('/budget')
  const card = page.getByTestId('bdg-account-card').first()
  if (await card.count() === 0) return false
  await card.click()
  await expect(page).toHaveURL(/\/budget\/accounts\/[0-9a-f-]+$/)
  const rows = page.getByTestId('bdg-tx-row')
  return (await rows.count()) > 0
}

test.describe('Budget — account transaction CRUD', () => {
  test('row menu opens with Edit and Delete', async ({authedPage: page}) => {
    if (!await goToAccountWithRows(page)) test.skip()
    const firstRow = page.getByTestId('bdg-tx-row').first()
    await firstRow.getByTestId('bdg-tx-menu-btn').click()
    await expect(page.getByTestId('bdg-tx-menu-edit')).toBeVisible()
    await expect(page.getByTestId('bdg-tx-menu-delete')).toBeVisible()
  })

  test('Edit opens TransactionDialog in edit mode', async ({authedPage: page}) => {
    if (!await goToAccountWithRows(page)) test.skip()
    const firstRow = page.getByTestId('bdg-tx-row').first()
    await firstRow.getByTestId('bdg-tx-menu-btn').click()
    await page.getByTestId('bdg-tx-menu-edit').click()
    await expect(page.locator('.budget-modal h3')).toContainText(/edit/i)
    // Dismiss
    await page.getByRole('button', {name: /Cancel/i}).click()
  })

  test('Delete + Undo restores the row', async ({authedPage: page}) => {
    if (!await goToAccountWithRows(page)) test.skip()
    const rowsBefore = await page.getByTestId('bdg-tx-row').count()
    const firstRow = page.getByTestId('bdg-tx-row').first()
    const firstId = await firstRow.getAttribute('data-tx-id')
    await firstRow.getByTestId('bdg-tx-menu-btn').click()
    await page.getByTestId('bdg-tx-menu-delete').click()
    // Row removed, toast visible.
    await expect(page.locator(`[data-tx-id="${firstId}"]`)).toHaveCount(0)
    await expect(page.getByTestId('bdg-undo-toast')).toBeVisible()
    // Undo
    await page.getByTestId('bdg-undo-btn').click()
    await expect(page.getByTestId('bdg-undo-toast')).toBeHidden()
    await expect(page.getByTestId('bdg-tx-row')).toHaveCount(rowsBefore)
    await expect(page.locator(`[data-tx-id="${firstId}"]`)).toHaveCount(1)
  })

  test('Delete commits after 5 seconds and survives reload', async ({authedPage: page}) => {
    if (!await goToAccountWithRows(page)) test.skip()
    const firstRow = page.getByTestId('bdg-tx-row').first()
    const firstId = await firstRow.getAttribute('data-tx-id')
    await firstRow.getByTestId('bdg-tx-menu-btn').click()
    await page.getByTestId('bdg-tx-menu-delete').click()
    // Wait past the 5s undo window plus a small buffer for the API commit.
    await page.waitForTimeout(5500)
    await page.reload()
    // Wait for the page to settle.
    await expect(page.getByTestId('bdg-account-page')).toBeVisible()
    await expect(page.locator(`[data-tx-id="${firstId}"]`)).toHaveCount(0)
  })
})
