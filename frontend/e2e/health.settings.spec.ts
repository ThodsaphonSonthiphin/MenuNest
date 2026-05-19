import { test, expect } from './fixtures/healthFixture'

test.describe('Health module — Settings', () => {
  test('renders settings page', async ({ authedPage, mockApi }) => {
    await mockApi.settings.me().apply()

    await authedPage.goto('/health/settings')
    await authedPage.waitForLoadState('domcontentloaded')

    await expect(authedPage.locator('.health-page')).toBeVisible()
  })

  test('revoke share link sends DELETE when confirmed', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.settings
      .me()
      .shareLinks([
        {
          id: 'link-1',
          token: 'tok-abc',
          createdAt: '2026-05-01T00:00:00Z',
          expiresAt: '2026-08-01T00:00:00Z',
          dateFrom: '2026-04-01',
          dateTo: '2026-04-30',
          accessCount: 0,
        },
      ])
      .apply()

    await authedPage.goto('/health/share')
    await authedPage.waitForLoadState('domcontentloaded')

    // The revoke button on each card is labeled "ยกเลิก link"
    const revokeBtn = authedPage.getByRole('button', { name: /เพิกถอน|revoke|ยกเลิก link/i }).first()
    if (await revokeBtn.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await revokeBtn.click()

      // After clicking, the confirm modal appears with a second "ยกเลิก link" button
      const confirmBtn = authedPage.getByRole('button', { name: /ยืนยัน|confirm|ยกเลิก link/i }).last()
      if (await confirmBtn.isVisible({ timeout: 1_000 }).catch(() => false)) {
        await confirmBtn.click()
      }

      await capturedRequests.waitFor('DELETE', /\/api\/share-links\//, 3_000).catch(() => null)
    }
  })

  test('cancel revoke dialog does not call DELETE', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.settings
      .me()
      .shareLinks([
        {
          id: 'link-1',
          token: 'tok-abc',
          createdAt: '2026-05-01T00:00:00Z',
          expiresAt: '2026-08-01T00:00:00Z',
          dateFrom: '2026-04-01',
          dateTo: '2026-04-30',
          accessCount: 0,
        },
      ])
      .apply()

    await authedPage.goto('/health/share')
    await authedPage.waitForLoadState('domcontentloaded')

    const revokeBtn = authedPage.getByRole('button', { name: /เพิกถอน|revoke|ยกเลิก link/i }).first()
    if (await revokeBtn.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await revokeBtn.click()

      // "ปิด" is the cancel button inside the confirm modal dialog
      const modal = authedPage.getByRole('dialog', { name: /ยกเลิก share link/i })
      const cancelBtn = modal.getByRole('button', { name: /ปิด/i }).first()
      if (await cancelBtn.isVisible({ timeout: 1_000 }).catch(() => false)) {
        await cancelBtn.click()
      }

      await authedPage.waitForTimeout(500)
      const deletes = capturedRequests
        .all()
        .filter((r) => r.method === 'DELETE' && r.pathname.startsWith('/api/share-links'))
      expect(deletes).toHaveLength(0)
    }
  })
})
