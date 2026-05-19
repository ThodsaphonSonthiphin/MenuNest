import { expect, test as base } from '@playwright/test'
import { test as authed } from './fixtures/healthFixture'

base.describe('Pomodoro — access (anonymous)', () => {
  base('anonymous visit is bounced to /login', async ({ page }) => {
    await page.goto('/pomodoro')
    await expect(page).toHaveURL(/\/login$/)
  })
})

authed.describe('Pomodoro — access (authed)', () => {
  authed('authed user without a family can reach /pomodoro', async ({ authedPage: page }) => {
    // The healthFixture's googleAuth helper does NOT inject any family
    // context — it only mints a Google id-token in sessionStorage. That
    // mirrors a real user who has signed in but never joined a family,
    // which is exactly the access shape we need to verify.
    await page.goto('/pomodoro')
    await expect(page.getByTestId('pomo-page')).toBeVisible()
  })

  authed('NavBar shows the Pomodoro link on desktop', async ({ authedPage: page }) => {
    await page.setViewportSize({ width: 1280, height: 800 })
    await page.goto('/health')
    const link = page.getByRole('link', { name: /Pomodoro/i })
    await expect(link).toBeVisible()
    await link.click()
    await expect(page).toHaveURL(/\/pomodoro$/)
  })

  authed('Mobile drawer lists the Pomodoro link', async ({ authedPage: page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/health')
    await page.getByRole('button', { name: /Toggle menu/i }).click()
    const drawerLink = page.getByRole('link', { name: /Pomodoro/i })
    await expect(drawerLink).toBeVisible()
    await drawerLink.click()
    await expect(page).toHaveURL(/\/pomodoro$/)
  })
})
