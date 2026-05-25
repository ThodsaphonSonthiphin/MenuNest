import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

test.describe('Budget — add entry points', () => {
  test('+ Cat button on group header opens AddCategoryDialog', async ({authedPage: page}) => {
    await page.goto('/budget')
    const btn = page.getByTestId('bdg-add-cat-btn').first()
    if (await btn.count() === 0) test.skip()
    await btn.click()
    await expect(page.locator('.budget-modal h3')).toContainText(/category/i)
  })

  test('+ Add Group button opens AddGroupDialog', async ({authedPage: page}) => {
    await page.goto('/budget')
    const btn = page.getByTestId('bdg-add-group-btn')
    if (await btn.count() === 0) test.skip()
    await btn.click()
    await expect(page.getByTestId('bdg-add-group-dialog')).toBeVisible()
    await expect(page.locator('.budget-modal h3')).toContainText(/group/i)
  })

  test('tap RTA hero opens SetIncomeDialog', async ({authedPage: page}) => {
    await page.goto('/budget')
    await page.getByTestId('bdg-rta-hero').click()
    await expect(page.getByTestId('bdg-set-income-dialog')).toBeVisible()
    await expect(page.locator('.budget-modal h3')).toContainText(/income/i)
  })

  test('Reconcile menu item on account detail opens ReconcileBalanceDialog', async ({authedPage: page}) => {
    await page.goto('/budget')
    const firstAccount = page.getByTestId('bdg-account-card').first()
    if (await firstAccount.count() === 0) test.skip()
    await firstAccount.click()
    await page.getByTestId('bdg-account-menu').click()
    await page.getByTestId('bdg-menu-reconcile').click()
    await expect(page.getByTestId('bdg-reconcile-dialog')).toBeVisible()
    await expect(page.locator('.budget-modal h3')).toContainText(/reconcile/i)
  })
})
