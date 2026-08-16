import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

test.describe('Writing — persistence', () => {
  test.beforeEach(async ({ authedPage: page }) => {
    await page.clock.install({ time: new Date('2026-06-01T09:00:00Z') })
    // Clear localStorage on the FIRST navigation per test only. The naïve
    // `localStorage.clear()` init script re-fires on every navigation —
    // including `page.reload()` — which wipes the persisted state we're
    // trying to assert on. The sentinel in sessionStorage (reset for
    // every test because Playwright spawns a fresh BrowserContext) makes
    // the clear happen exactly once.
    await page.addInitScript(() => {
      if (!sessionStorage.getItem('__writing_lsCleared')) {
        localStorage.clear()
        sessionStorage.setItem('__writing_lsCleared', '1')
      }
    })
  })

  test('reload while timer is running restores the correct remaining time', async ({
    authedPage: page,
  }) => {
    await page.goto('/writing')
    await page.clock.fastForward(3 * 60_000) // 3 min elapsed
    await expect(page.getByTestId('writing-timer')).toHaveText('04:00')

    await page.reload()
    await expect(page.getByTestId('writing-timer')).toHaveText('04:00')
  })

  test('a new day starts a fresh 7-minute session', async ({ authedPage: page }) => {
    await page.goto('/writing')
    await page.clock.fastForward(3 * 60_000) // 3 min elapsed
    await expect(page.getByTestId('writing-timer')).toHaveText('04:00')

    // Jump to the next day then reload — the persisted session is scoped
    // to yesterday's date, so it should be treated as absent and a fresh
    // 7:00 session should start.
    await page.clock.setSystemTime(new Date('2026-06-02T09:00:00Z'))
    await page.reload()
    await expect(page.getByTestId('writing-timer')).toHaveText('07:00')
  })
})
