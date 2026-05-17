import { test, expect } from '@playwright/test'

/**
 * Health module — smoke tests (Phase 1).
 *
 * What this verifies:
 *   1. The frontend builds and the Vite dev server can serve the SPA.
 *   2. The public, token-gated doctor-report page (`/share/:token`)
 *      renders an error state gracefully when the token is invalid /
 *      the backend is unreachable. This is the only health-module
 *      route that's reachable without authentication, so it's the
 *      only one we can drive without stubbing MSAL.
 *   3. The unauthenticated landing flow (anonymous user hits `/`) does
 *      not crash; the ProtectedRoute bounces them to `/login` and the
 *      login page renders without throwing.
 *   4. PWA wiring is intact: `manifest.json` is reachable and the
 *      service worker (`/sw.js`) registers on first load. The
 *      migraine tracker depends on the service worker for follow-up
 *      push notifications, so a broken registration is a real bug.
 *
 * What this does NOT verify (deliberately):
 *   - The full attack flow (Quick Log → Active Episode → Take
 *     Medication → resolved). That path requires a signed-in user
 *     and a live API. Stubbing MSAL + RTK Query for the entire
 *     handler graph is fragile; the unit + integration test suite
 *     covers the business logic, and a true end-to-end run will
 *     happen once we have a deployable env with test credentials.
 */
test.describe('Health module — smoke tests', () => {
  test('public share page handles invalid token gracefully', async ({ page }) => {
    // `/share/invalid-token-xxx` is a real route (`PublicReportPage`),
    // routed outside `ProtectedRoute` so a doctor on a fresh device
    // can open the QR-code URL without signing in. With no token
    // match in the backend (or with the backend offline) RTK Query
    // surfaces an error and the page renders one of the Thai error
    // strings from PublicReportPage.tsx:
    //   - "ลิงก์นี้ถูกเพิกถอนแล้ว"   (401/403)
    //   - "ไม่พบรายงาน หรือลิงก์หมดอายุ" (404/410)
    //   - "เกิดข้อผิดพลาด ลองรีเฟรชหน้า" (other / network)
    await page.goto('/share/invalid-token-xxx')
    await expect(page.locator('body')).toContainText(
      /(เพิกถอน|หมดอายุ|ผิดพลาด|ไม่พบ)/,
    )
  })

  test('app renders without crashing for anonymous user', async ({ page }) => {
    // ProtectedRoute redirects unauthenticated visitors at `/` to
    // `/login`. We just want to confirm the SPA renders something —
    // no white screen, no thrown JS error, no service worker bomb.
    const errors: string[] = []
    page.on('pageerror', (err) => errors.push(err.message))

    await page.goto('/')
    await page.waitForLoadState('networkidle')

    await expect(page.locator('body')).toBeVisible()
    // Page-level JS errors fail the test. (Console warnings from
    // Syncfusion license / MSAL config in dev mode are fine.)
    expect(errors).toEqual([])
  })

  test('login page renders', async ({ page }) => {
    // Direct hit on `/login` should render LoginPage without redirect.
    await page.goto('/login')
    await page.waitForLoadState('networkidle')
    await expect(page.locator('body')).toBeVisible()
    // The "MenuNest" brand text is always shown in the login card.
    await expect(page.locator('body')).toContainText('MenuNest')
  })

  test('service worker registers on first load', async ({ page }) => {
    // main.tsx registers `/sw.js` after the window `load` event. The
    // migraine tracker's follow-up push notifications rely on it.
    await page.goto('/')
    await page.waitForLoadState('load')

    // SW registration is async-after-load; poll briefly rather than
    // sleeping a fixed amount.
    const swReady = await page.waitForFunction(
      async () => {
        if (!('serviceWorker' in navigator)) return false
        const reg = await navigator.serviceWorker.getRegistration()
        return reg !== undefined && reg !== null
      },
      undefined,
      { timeout: 10_000 },
    ).then(() => true).catch(() => false)

    expect(swReady).toBe(true)
  })

  test('manifest.json is reachable and well-formed', async ({ page }) => {
    // The PWA manifest must be served from `/manifest.json` for
    // "Add to Home Screen" + the iOS install prompt that the
    // migraine tracker depends on (web-push doesn't work on iOS
    // Safari outside an installed PWA).
    const response = await page.goto('/manifest.json')
    expect(response?.status()).toBe(200)
    const json = await response?.json()
    expect(json).toHaveProperty('name')
    expect(json).toHaveProperty('icons')
    expect(Array.isArray(json.icons)).toBe(true)
    expect(json.icons.length).toBeGreaterThan(0)
  })
})
