import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

test.describe('Budget — smoke', () => {
  test('authed user reaches /budget and sees the single column', async ({authedPage: page}) => {
    await page.goto('/budget')
    await expect(page.getByTestId('bdg-page')).toBeVisible()
    await expect(page.getByTestId('bdg-month-strip')).toBeVisible()
    await expect(page.getByTestId('bdg-rta-hero')).toBeVisible()
    await expect(page.getByTestId('bdg-accounts-strip')).toBeVisible()
    await expect(page.getByTestId('bdg-envelopes')).toBeVisible()
  })

  test('tap an account card navigates to the account-detail page', async ({authedPage: page}) => {
    await page.goto('/budget')
    const firstCard = page.getByTestId('bdg-account-card').first()
    if (await firstCard.count() === 0) test.skip()

    await firstCard.click()
    await expect(page).toHaveURL(/\/budget\/accounts\/[0-9a-f-]+$/)
    await expect(page.getByTestId('bdg-account-page')).toBeVisible()
    await expect(page.getByTestId('bdg-account-hero')).toBeVisible()
    await expect(page.getByTestId('bdg-fab')).toBeVisible()
  })

  test('account detail back button returns to /budget', async ({authedPage: page}) => {
    await page.goto('/budget')
    const firstCard = page.getByTestId('bdg-account-card').first()
    if (await firstCard.count() === 0) test.skip()

    await firstCard.click()
    await page.getByRole('link', {name: /Back/i}).click()
    await expect(page).toHaveURL(/\/budget$/)
  })
})
