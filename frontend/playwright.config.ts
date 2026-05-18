import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright config for MenuNest frontend E2E smoke tests.
 *
 * Scope (Phase 1, Task 19 of the migraine tracker plan):
 *   - Verify the Vite dev server boots and serves the SPA.
 *   - Verify public, unauthenticated routes render gracefully (the
 *     token-gated `/share/:token` doctor report and the login page).
 *   - Verify PWA wiring (`manifest.json` reachable, service worker
 *     registers on first load).
 *
 * Out of scope: the full attack flow (Quick Log → Active Episode →
 * Take Medication → resolution ping). That path is gated by real MSAL
 * / Google OAuth, and stubbing those reliably across tests is brittle
 * without test-only credentials or a backend test-token endpoint. It
 * will be added once we have a deployable env (subscription is
 * currently disabled — see `feedback_azure_subscription_scope.md`).
 *
 * The config does NOT spin up the backend webServer — the smoke tests
 * here only exercise statically-served public pages, and starting
 * `dotnet run` would require Azure SQL access which is also blocked
 * by the disabled subscription.
 */
export default defineConfig({
  testDir: './e2e',
  // Health tests share state (service worker registration, etc.) — keep
  // them serial so one test doesn't unregister something another expects.
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: process.env.CI ? 'html' : 'list',
  use: {
    baseURL: 'http://localhost:5173',
    // Always capture trace/screenshot/video so CI artifacts always include
    // a full record of each run — useful for diagnosing flaky tests and
    // for visual regression review even when everything passes.
    trace: 'on',
    screenshot: 'on',
    video: 'on',
    // Thai locale — the app's user-facing copy (including the public
    // share page error messages we assert on) is Thai.
    locale: 'th-TH',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: [
    {
      command: 'npm run dev',
      url: 'http://localhost:5173',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    // NOTE: no backend webServer. The smoke tests intentionally do not
    // hit `/api/*`. When the doctor-report token is invalid the page
    // renders an error state purely from RTK Query's fetch failure —
    // either a network error (backend down) or an HTTP 404/410 (backend
    // up) both resolve to the same Thai error copy we assert on.
  ],
})
