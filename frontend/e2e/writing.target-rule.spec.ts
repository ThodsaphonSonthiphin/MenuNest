import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

// Smoke coverage for the in-app active-target-rule control (#97, Path 1 —
// the writer's everyday route, as opposed to the MCP chat path). This is
// the same class of "did the gate even render styled markup" check as
// writing.history.spec.ts: tsc/build/vitest cannot see rendering, layout,
// or DOM-interaction bugs (frontend/vite.config.ts runs vitest in
// environment: 'node', no jsdom/RTL), so this spec is the one mechanism
// that can catch a broken render of this control before it ships.
const ME_BASE = {
  userId: 'user-1',
  email: 'test@menunest.app',
  displayName: 'Test User',
  familyId: null,
  familyName: null,
  familyInviteCode: null,
  authProvider: 'Google',
  homePath: '/budget',
  uvWarnThreshold: null,
  feelsLikeWarnThreshold: null,
}

test.describe('Writing — active target rule (settings control)', () => {
  test('unset rule renders the placeholder, styled (not raw controls)', async ({ authedPage: page }) => {
    await page.route('**/api/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...ME_BASE, activeTargetRule: null }),
      })
    })

    await page.goto('/settings')
    await page.waitForLoadState('domcontentloaded')

    const input = page.locator('.settings-rule-input')
    await expect(input).toBeVisible()
    await expect(input).toHaveValue('')
    await expect(input).toHaveAttribute('placeholder', 'ยังไม่ได้ตั้ง — AI จะถามก่อนตรวจ')

    // Styled markup, not a raw unstyled control list -- the section title
    // and at least one preset button must be present and visible.
    await expect(page.getByText('กฎเป้าหมายเดือนนี้')).toBeVisible()
    const preset = page.locator('.settings-rule-preset', { hasText: 'third-person singular -s' })
    await expect(preset).toBeVisible()
  })

  test('clicking a preset fires PUT /api/me/target-rule with the preset value', async ({ authedPage: page }) => {
    await page.route('**/api/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...ME_BASE, activeTargetRule: null }),
      })
    })

    let putBody: unknown = null
    let putCalled = false
    await page.route('**/api/me/target-rule', async (route, request) => {
      putCalled = true
      putBody = request.postDataJSON()
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ activeTargetRule: 'third-person singular -s' }),
      })
    })

    await page.goto('/settings')
    await page.waitForLoadState('domcontentloaded')

    const preset = page.locator('.settings-rule-preset', { hasText: 'third-person singular -s' })
    await expect(preset).toBeVisible()
    await preset.click()

    await expect.poll(() => putCalled).toBe(true)
    expect(putBody).toEqual({ rule: 'third-person singular -s' })

    // The input reflects the applied preset immediately (optimistic cache patch).
    await expect(page.locator('.settings-rule-input')).toHaveValue('third-person singular -s')
  })

  test('typing free text and blurring fires PUT with exactly that string', async ({ authedPage: page }) => {
    // Free-text entry via the plain <input> is the entire reason this control
    // is not a closed-option dropdown -- a regression that broke onChange/onBlur
    // wiring while leaving preset clicks intact would pass tsc/build/vitest and
    // the preset-only test above, so it needs its own coverage.
    await page.route('**/api/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...ME_BASE, activeTargetRule: null }),
      })
    })

    let putBody: unknown = null
    let putCalled = false
    await page.route('**/api/me/target-rule', async (route, request) => {
      putCalled = true
      putBody = request.postDataJSON()
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ activeTargetRule: 'present perfect have/has + V3' }),
      })
    })

    await page.goto('/settings')
    await page.waitForLoadState('domcontentloaded')

    const input = page.locator('.settings-rule-input')
    await input.fill('present perfect have/has + V3')
    // Blur programmatically (relatedTarget = null) rather than pressing Tab --
    // Tab would move focus to the first preset button right after the input
    // in DOM order, which is exactly the case the blur-race guard treats as
    // "let the button's click handle it" and would skip this persist.
    await input.evaluate((el) => (el as HTMLInputElement).blur())

    await expect.poll(() => putCalled).toBe(true)
    expect(putBody).toEqual({ rule: 'present perfect have/has + V3' })
  })

  test('typing unsaved text then clicking a preset sends exactly one PUT, for the preset', async ({
    authedPage: page,
  }) => {
    // Regression coverage for the blur-race: standard DOM order fires the
    // input's blur BEFORE the preset button's click. Without the
    // relatedTarget guard in SettingsPage.tsx, both handlers would call
    // persistRule independently and could resolve out of order, leaving the
    // server holding the typed text while the UI shows the preset.
    await page.route('**/api/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...ME_BASE, activeTargetRule: null }),
      })
    })

    const putBodies: unknown[] = []
    await page.route('**/api/me/target-rule', async (route, request) => {
      putBodies.push(request.postDataJSON())
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ activeTargetRule: 'third-person singular -s' }),
      })
    })

    await page.goto('/settings')
    await page.waitForLoadState('domcontentloaded')

    const input = page.locator('.settings-rule-input')
    await input.fill('unsaved draft text')

    const preset = page.locator('.settings-rule-preset', { hasText: 'third-person singular -s' })
    await preset.click()

    await expect.poll(() => putBodies.length).toBeGreaterThan(0)
    // Give a possible (buggy) second PUT a moment to land before asserting the count.
    await page.waitForTimeout(300)
    expect(putBodies).toEqual([{ rule: 'third-person singular -s' }])
  })

  test('renders usably at phone width (390px) -- no horizontal overflow', async ({ authedPage: page }) => {
    // #97 already shipped one rendering bug (missing RTE stylesheet import)
    // that was invisible to tsc/build/vitest. The writer uses this control
    // one-handed on a phone, so assert actual geometry, not just visibility.
    await page.setViewportSize({ width: 390, height: 844 })

    await page.route('**/api/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...ME_BASE, activeTargetRule: null }),
      })
    })

    await page.goto('/settings')
    await page.waitForLoadState('domcontentloaded')

    const input = page.locator('.settings-rule-input')
    await expect(input).toBeVisible()
    const inputBox = await input.boundingBox()
    expect(inputBox).not.toBeNull()
    expect(inputBox!.width).toBeGreaterThan(0)
    expect(inputBox!.x + inputBox!.width).toBeLessThanOrEqual(390)

    const preset = page.locator('.settings-rule-preset', { hasText: 'third-person singular -s' })
    await expect(preset).toBeVisible()
    await expect(preset).toBeEnabled()
    const presetBox = await preset.boundingBox()
    expect(presetBox).not.toBeNull()
    expect(presetBox!.width).toBeGreaterThan(0)
    expect(presetBox!.x + presetBox!.width).toBeLessThanOrEqual(390)

    // The page itself must not scroll horizontally at this width.
    const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth)
    expect(scrollWidth).toBeLessThanOrEqual(390)
  })
})
