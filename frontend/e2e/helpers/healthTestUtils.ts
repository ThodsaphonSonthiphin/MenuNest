import { Buffer } from 'node:buffer'
import type { Page } from '@playwright/test'

export interface GoogleTokenPayload {
  sub?: string
  name?: string
  email?: string
}

export const buildGoogleToken = (payload: GoogleTokenPayload = {}): string => {
  const encoded = Buffer.from(
    JSON.stringify({
      sub: payload.sub ?? 'user-1',
      name: payload.name ?? 'Test User',
      email: payload.email ?? 'test@menunest.app',
      exp: Math.floor(Date.now() / 1000) + 3600,
    }),
  ).toString('base64')
  return `header.${encoded}.signature`
}

/**
 * localStorage keys that hold the signed-in identity the auth fixture seeds
 * (`google_id_token`) or that the app itself mints for a durable session
 * (`menunest.session.*`, ADR-161). `resetAppStorage` preserves these; nothing
 * else in localStorage is test-relevant credential state.
 */
export const AUTH_STORAGE_KEYS = [
  'google_id_token',
  'menunest.session.access',
  'menunest.session.refresh',
  'menunest.session.expiresAt',
] as const

export const applyGoogleAuth = async (page: Page, payload?: GoogleTokenPayload) => {
  const token = buildGoogleToken(payload)
  await page.addInitScript((value) => {
    localStorage.setItem('google_id_token', value)
  }, token)
  return token
}

/**
 * Reset the app's localStorage between tests **without signing the test out**.
 *
 * A bare `localStorage.clear()` init script cannot be used by an authed spec.
 * `applyGoogleAuth` seeds the credential from an init script registered by the
 * `authedPage` fixture, and fixtures are set up *before* `beforeEach`, so the
 * seeding script always runs first on every navigation and a spec's own
 * `clear()` wipes the token again. `ProtectedRoute` then finds no session,
 * redirects to `/login`, and every later assertion fails on an element the
 * page was never going to render — the errors name timer test ids, but the
 * cause is auth (#150).
 *
 * @param options.once Clear only on the first navigation of the test, so
 *   `page.reload()` and cross-page navigation preserve the state under test.
 *   The sentinel lives in `sessionStorage`, which is fresh for every test
 *   because Playwright spawns a new BrowserContext per test.
 */
export const resetAppStorage = async (
  page: Page,
  options: { once?: boolean } = {},
) => {
  const once = options.once ?? false
  await page.addInitScript(
    ({ once, keys, sentinel }) => {
      if (once && sessionStorage.getItem(sentinel)) return
      const preserved = keys
        .map((key) => [key, localStorage.getItem(key)] as const)
        .filter(([, value]) => value !== null)
      localStorage.clear()
      for (const [key, value] of preserved) localStorage.setItem(key, value!)
      if (once) sessionStorage.setItem(sentinel, '1')
    },
    { once, keys: AUTH_STORAGE_KEYS as readonly string[], sentinel: '__mn_lsCleared' },
  )
}

/** The instant every clock-driven spec pins itself to. */
export const PINNED_CLOCK_TIME = new Date('2026-06-01T09:00:00Z')

/**
 * Install a fake clock pinned at `time` **and pause it**, so the only thing
 * that ever moves time is an explicit `page.clock.fastForward(...)`.
 *
 * `page.clock.install()` on its own does NOT freeze time — the fake clock
 * keeps ticking along with the wall clock. Every wall-clock second spent
 * loading the page therefore leaks into the timer under test, so a spec that
 * asserts an exact remaining time drifts by however long the navigation took
 * (25s of Vite dev-server cold start on a loaded machine reads as `03:48`
 * where the spec expects `04:00`). `pauseAt` makes the fake clock advance
 * only on demand, which is what these specs have always assumed (#150).
 */
export const installPausedClock = async (page: Page, time: Date = PINNED_CLOCK_TIME) => {
  await page.clock.install({ time })
  await page.clock.pauseAt(time)
}

/**
 * Hide Syncfusion's *unlicensed trial* banner for the duration of a test.
 *
 * `NavBar` renders a `@syncfusion/react-buttons` Button, so Syncfusion is live
 * on every authed route. With no `VITE_SYNCFUSION_LICENSE_KEY`,
 * `registerSyncfusionLicense` registers nothing and Syncfusion appends an
 * unclassed `<div style="position:fixed; top:10px; left:10px; right:10px;
 * z-index:999999999">` to `document.body`. That strip sits exactly over the
 * NavBar, and `document.elementFromPoint()` at the brand link's centre returns
 * the banner's `<span>` — so every NavBar click in the suite is swallowed with
 * "`<span>…</span>` from `<div>…</div>` subtree intercepts pointer events".
 *
 * Prod is unaffected (the Static Web Apps build injects the key from secrets);
 * only the licence-less CI/dev environment grows the banner, so this is an
 * environment artifact rather than behaviour any spec means to exercise.
 * Hiding it changes no assertion — the alternative is to give the Playwright
 * workflow the same `VITE_SYNCFUSION_LICENSE_KEY` secret the deploy workflow
 * already uses, which would make the suite depend on a paid licence secret.
 */
export const hideSyncfusionTrialBanner = async (page: Page) => {
  await page.addInitScript(() => {
    const isTrialBanner = (node: Node): node is HTMLElement =>
      node instanceof HTMLElement &&
      node.style.zIndex === '999999999' &&
      (node.textContent ?? '').includes('trial version of Syncfusion')

    const hide = (el: HTMLElement) => el.style.setProperty('display', 'none', 'important')

    const sweep = () => {
      for (const child of Array.from(document.body.children))
        if (isTrialBanner(child)) hide(child)
    }

    const observer = new MutationObserver((records) => {
      for (const record of records)
        for (const node of Array.from(record.addedNodes)) if (isTrialBanner(node)) hide(node)
    })

    const start = () => {
      sweep()
      observer.observe(document.body, { childList: true })
    }

    if (document.body) start()
    else document.addEventListener('DOMContentLoaded', start, { once: true })
  })
}
