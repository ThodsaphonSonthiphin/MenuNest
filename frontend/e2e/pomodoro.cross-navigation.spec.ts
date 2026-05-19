import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

test.describe('Pomodoro — cross-page navigation', () => {
  test.beforeEach(async ({ authedPage: page }) => {
    await page.clock.install({ time: new Date('2026-06-01T09:00:00Z') })
    // Sentinel pattern — clear LS once per test. The naive init script
    // would wipe persisted state on every page navigation, hiding the
    // cross-page survival behaviour we're testing.
    await page.addInitScript(() => {
      if (!sessionStorage.getItem('__pomo_lsCleared')) {
        localStorage.clear()
        sessionStorage.setItem('__pomo_lsCleared', '1')
      }
    })
  })

  test('timer state survives navigating to /health and back', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(2 * 60_000)
    await expect(page.getByTestId('pomo-time')).toHaveText('23:00')

    await page.goto('/health')
    await page.clock.fastForward(60_000)
    await page.goto('/pomodoro')

    // 2 + 1 = 3 min elapsed since start, regardless of which page was
    // mounted in between.
    await expect(page.getByTestId('pomo-time')).toHaveText('22:00')
    await expect(page.getByTestId('pomo-pause')).toBeVisible()
  })

  test('clicking the MenuNest brand link does not lose timer state', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(60_000)

    await page.getByRole('link', { name: /MenuNest/i }).first().click()
    // Brand goes to '/', which the protected redirect resolves to '/health'.
    await expect(page).toHaveURL(/\/health$/)

    await page.clock.fastForward(60_000)
    await page.goto('/pomodoro')
    await expect(page.getByTestId('pomo-time')).toHaveText('23:00')
  })
})
