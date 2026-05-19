import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

test.describe('Pomodoro — cycle transitions', () => {
  test.beforeEach(async ({ authedPage: page }) => {
    await page.clock.install({ time: new Date('2026-06-01T09:00:00Z') })
    await page.addInitScript(() => localStorage.clear())
  })

  test('focus → break flip when the focus cycle ends', async ({ authedPage: page }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(25 * 60_000)

    await expect(page.getByTestId('pomo-mode-badge')).toHaveText('Break')
    await expect(page.getByTestId('pomo-daily-count')).toContainText('Pomodoros today: 1')
    // The break cycle is automatically running with a fresh startedAt at
    // the focus-end boundary, so the displayed time should be the break
    // duration minus zero elapsed.
    await expect(page.getByTestId('pomo-time')).toHaveText('05:00')
  })

  test('break → focus flip does NOT increment the daily count', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(25 * 60_000) // finish focus
    await page.clock.fastForward(5 * 60_000) // finish break

    await expect(page.getByTestId('pomo-mode-badge')).toHaveText('Focus')
    await expect(page.getByTestId('pomo-daily-count')).toContainText('Pomodoros today: 1')
  })

  test('three full focus cycles → daily count = 3', async ({ authedPage: page }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    // 3 full Focus + 2 intervening Breaks (the 3rd Break is irrelevant).
    await page.clock.fastForward(25 * 60_000) // F1 done
    await page.clock.fastForward(5 * 60_000) // B1 done
    await page.clock.fastForward(25 * 60_000) // F2 done
    await page.clock.fastForward(5 * 60_000) // B2 done
    await page.clock.fastForward(25 * 60_000) // F3 done

    await expect(page.getByTestId('pomo-daily-count')).toContainText('Pomodoros today: 3')
  })
})
