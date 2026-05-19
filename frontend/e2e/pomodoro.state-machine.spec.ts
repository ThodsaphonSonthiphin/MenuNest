import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

test.describe('Pomodoro — state machine', () => {
  test.beforeEach(async ({ authedPage: page }) => {
    await page.clock.install({ time: new Date('2026-06-01T09:00:00Z') })
    await page.addInitScript(() => localStorage.clear())
  })

  test('idle → Start → running, time counts down', async ({ authedPage: page }) => {
    await page.goto('/pomodoro')
    await expect(page.getByTestId('pomo-time')).toHaveText('25:00')

    await page.getByTestId('pomo-start').click()
    await expect(page.getByTestId('pomo-mode-badge')).toHaveText('Focus')

    await page.clock.fastForward(1000)
    await expect(page.getByTestId('pomo-time')).toHaveText('24:59')

    await page.clock.fastForward(59_000)
    await expect(page.getByTestId('pomo-time')).toHaveText('24:00')
  })

  test('running → Pause freezes the displayed time', async ({ authedPage: page }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(5_000)
    await page.getByTestId('pomo-pause').click()

    const frozen = await page.getByTestId('pomo-time').textContent()
    await page.clock.fastForward(10_000)
    await expect(page.getByTestId('pomo-time')).toHaveText(frozen!)
  })

  test('paused → Resume continues from the paused remaining', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(60_000) // 1 min elapsed
    await page.getByTestId('pomo-pause').click()
    await expect(page.getByTestId('pomo-time')).toHaveText('24:00')

    await page.clock.fastForward(120_000) // 2 min while paused — must not count
    await page.getByTestId('pomo-start').click() // Resume reuses the testid
    await page.clock.fastForward(1_000)
    await expect(page.getByTestId('pomo-time')).toHaveText('23:59')
  })

  test('Reset returns to full duration and idle status', async ({ authedPage: page }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(5 * 60_000)
    await page.getByTestId('pomo-reset').click()

    await expect(page.getByTestId('pomo-time')).toHaveText('25:00')
    await expect(page.getByTestId('pomo-start')).toBeVisible()
  })

  test('Reset from paused also returns to full duration', async ({ authedPage: page }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(2 * 60_000)
    await page.getByTestId('pomo-pause').click()
    await page.getByTestId('pomo-reset').click()

    await expect(page.getByTestId('pomo-time')).toHaveText('25:00')
  })
})
