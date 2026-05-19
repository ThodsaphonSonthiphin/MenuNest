import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

test.describe('Pomodoro — persistence', () => {
  test.beforeEach(async ({ authedPage: page }) => {
    await page.clock.install({ time: new Date('2026-06-01T09:00:00Z') })
    // Clear localStorage on the FIRST navigation per test only. The naïve
    // `localStorage.clear()` init script re-fires on every navigation —
    // including `page.reload()` — which wipes the persisted state we're
    // trying to assert on. The sentinel in sessionStorage (reset for
    // every test because Playwright spawns a fresh BrowserContext) makes
    // the clear happen exactly once.
    await page.addInitScript(() => {
      if (!sessionStorage.getItem('__pomo_lsCleared')) {
        localStorage.clear()
        sessionStorage.setItem('__pomo_lsCleared', '1')
      }
    })
  })

  test('reload while running restores the correct remaining time', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(3 * 60_000) // 3 min elapsed
    await expect(page.getByTestId('pomo-time')).toHaveText('22:00')

    await page.reload()
    await expect(page.getByTestId('pomo-time')).toHaveText('22:00')
    // Still running — there should be a Pause button.
    await expect(page.getByTestId('pomo-pause')).toBeVisible()
  })

  test('reload while paused restores the paused remaining', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(3 * 60_000)
    await page.getByTestId('pomo-pause').click()
    await expect(page.getByTestId('pomo-time')).toHaveText('22:00')

    await page.reload()
    await expect(page.getByTestId('pomo-time')).toHaveText('22:00')
    // Paused — the primary action should be Resume (label, same testid).
    await expect(page.getByTestId('pomo-start')).toHaveText('Resume')
  })

  test('reload while idle keeps the timer at full duration', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(2 * 60_000)
    await page.getByTestId('pomo-reset').click()

    await page.reload()
    await expect(page.getByTestId('pomo-time')).toHaveText('25:00')
    await expect(page.getByTestId('pomo-start')).toHaveText('Start')
  })

  test('daily count resets when the local date changes', async ({ authedPage: page }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(25 * 60_000) // 1 focus complete
    await expect(page.getByTestId('pomo-daily-count')).toContainText('Pomodoros today: 1')

    // Jump 24h forward then reload — loadState() should rotate the daily
    // count for the new date.
    await page.clock.setSystemTime(new Date('2026-06-02T09:00:00Z'))
    await page.reload()
    await expect(page.getByTestId('pomo-daily-count')).toContainText('Pomodoros today: 0')
  })

  test('corrupt localStorage falls back to defaults instead of crashing', async ({
    authedPage: page,
  }) => {
    await page.addInitScript(() => {
      localStorage.setItem('menunest:pomodoro:v1', '{not json')
    })
    await page.goto('/pomodoro')
    await expect(page.getByTestId('pomo-time')).toHaveText('25:00')
    await expect(page.getByTestId('pomo-mode-badge')).toHaveText('Focus')
  })
})
