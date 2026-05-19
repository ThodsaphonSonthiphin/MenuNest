import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

const setRangeValue = async (locator: import('@playwright/test').Locator, value: number) => {
  await locator.evaluate((el, v) => {
    const input = el as HTMLInputElement
    const setter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype,
      'value',
    )!.set!
    setter.call(input, String(v))
    input.dispatchEvent(new Event('input', { bubbles: true }))
  }, value)
}

test.describe('Pomodoro — settings', () => {
  test.beforeEach(async ({ authedPage: page }) => {
    await page.clock.install({ time: new Date('2026-06-01T09:00:00Z') })
    // Sentinel — clear LS once per test so reload preserves saved settings.
    await page.addInitScript(() => {
      if (!sessionStorage.getItem('__pomo_lsCleared')) {
        localStorage.clear()
        sessionStorage.setItem('__pomo_lsCleared', '1')
      }
    })
  })

  test('changing focus duration while idle updates the displayed time', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-settings-toggle').click()
    await setRangeValue(page.getByTestId('pomo-focus-slider'), 5)
    await expect(page.getByTestId('pomo-time')).toHaveText('05:00')
  })

  test('changing focus duration while running preserves the current remaining', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(5 * 60_000)
    await expect(page.getByTestId('pomo-time')).toHaveText('20:00')

    await page.getByTestId('pomo-settings-toggle').click()
    await setRangeValue(page.getByTestId('pomo-focus-slider'), 50)
    // Same MM:SS as before — the running cycle absorbs the change.
    await expect(page.getByTestId('pomo-time')).toHaveText('20:00')

    // Reset and the NEW duration takes effect for the next cycle.
    await page.getByTestId('pomo-reset').click()
    await expect(page.getByTestId('pomo-time')).toHaveText('50:00')
  })

  test('settings round-trip across reload', async ({ authedPage: page }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-settings-toggle').click()
    await setRangeValue(page.getByTestId('pomo-break-slider'), 10)
    await page.getByTestId('pomo-sound-toggle').click() // turn off
    await page.getByTestId('pomo-notif-toggle').click() // turn off

    await page.reload()
    await page.getByTestId('pomo-settings-toggle').click()
    await expect(page.getByTestId('pomo-break-slider')).toHaveValue('10')
    await expect(page.getByTestId('pomo-sound-toggle')).not.toBeChecked()
    await expect(page.getByTestId('pomo-notif-toggle')).not.toBeChecked()
  })
})
