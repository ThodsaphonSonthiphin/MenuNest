import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'
import { installPausedClock, resetAppStorage } from './helpers/healthTestUtils'

test.describe('Writing — persistence', () => {
  test.beforeEach(async ({ authedPage: page }) => {
    await installPausedClock(page)
    // `once` — clear on the FIRST navigation per test only. A clear that
    // re-fires on every navigation (including `page.reload()`) would wipe
    // the persisted state we're trying to assert on.
    await resetAppStorage(page, { once: true })
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
