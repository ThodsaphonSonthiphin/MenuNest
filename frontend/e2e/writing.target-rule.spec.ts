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
})
