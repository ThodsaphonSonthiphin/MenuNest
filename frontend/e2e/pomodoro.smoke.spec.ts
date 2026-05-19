import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

test.describe('Pomodoro — smoke', () => {
  test('authed user reaches /pomodoro without errors', async ({ authedPage: page }) => {
    const consoleErrors: string[] = []
    page.on('console', (msg) => {
      if (msg.type() === 'error') consoleErrors.push(msg.text())
    })

    await page.goto('/pomodoro')

    await expect(page.getByTestId('pomo-page')).toBeVisible()
    await expect(page.getByTestId('pomo-time')).toContainText('25:00')

    // Ignore noise unrelated to the Pomodoro page itself:
    //   - manifest/favicon/workbox: PWA chrome, covered by other tests
    //   - ERR_CONNECTION_REFUSED / "Failed to load resource": app-wide
    //     background fetches (RTK Query, health ping) hitting a backend
    //     that is not running during E2E smoke. The Pomodoro page makes
    //     no API calls of its own, so these are environmental, not a
    //     Pomodoro regression.
    expect(
      consoleErrors.filter(
        (e) =>
          !/manifest|favicon|workbox|ERR_CONNECTION_REFUSED|Failed to load resource/i.test(
            e,
          ),
      ),
    ).toEqual([])
  })

  test('manifest.json is still reachable', async ({ authedPage: page }) => {
    const res = await page.request.get('/manifest.json')
    expect(res.ok()).toBe(true)
  })

  test('the existing /sw.js still registers (migraine SW not broken)', async ({
    authedPage: page,
  }) => {
    await page.goto('/health')
    const registered = await page.evaluate(async () => {
      const reg = await navigator.serviceWorker.ready
      return Boolean(reg && reg.active && reg.active.scriptURL.endsWith('/sw.js'))
    })
    expect(registered).toBe(true)
  })
})
