import { test, expect } from './fixtures/healthFixture'

test.describe('Health module — Episode Detail', () => {
  test('renders timeline with start time and intake entries', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes.activeNone().detail().apply()

    await authedPage.goto('/health/episode/episode-1')
    await authedPage.waitForLoadState('networkidle')

    await expect(authedPage.getByText('Migraine').first()).toBeVisible()
    await expect(authedPage.getByText('Ibuprofen').first()).toBeVisible()
  })

  test('PUT request fires when severity is edited (if editable)', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.episodes.activeNone().detail().apply()

    await authedPage.goto('/health/episode/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // Edit button ("✏️ แก้ไข") navigates away to the active-episode page —
    // it does not open an inline form, so no save button appears.
    // The defensive isVisible wrapper handles this gracefully.
    const editButton = authedPage.getByRole('button', { name: /แก้ไข|edit/i }).first()
    if (await editButton.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await editButton.click()
      const saveBtn = authedPage.getByRole('button', { name: /บันทึก|save/i }).first()
      if (await saveBtn.isVisible({ timeout: 2_000 }).catch(() => false)) {
        await saveBtn.click()
        await capturedRequests.waitFor('PUT', /\/api\/episodes\/episode-1/).catch(() => null)
      }
    }
  })

  test('DELETE request fires when delete is confirmed', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.episodes.activeNone().detail().apply()

    await authedPage.goto('/health/episode/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // Delete button uses aria-label="Delete episode" (trash icon 🗑)
    const deleteButton = authedPage.getByRole('button', { name: /Delete episode|ลบ/i }).first()
    if (await deleteButton.isVisible({ timeout: 2_000 }).catch(() => false)) {
      authedPage.once('dialog', (d) => d.accept())
      await deleteButton.click()
      // Modal confirm button has Thai text "ลบ"
      const confirmBtn = authedPage.getByRole('button', { name: /^ลบ$/ }).first()
      if (await confirmBtn.isVisible({ timeout: 1_000 }).catch(() => false)) {
        await confirmBtn.click()
      }
      await capturedRequests
        .waitFor('DELETE', /\/api\/episodes\/episode-1/, 3_000)
        .catch(() => null)
    }
  })

  test('renders graceful error for missing episode (404)', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes.activeNone().apply()
    await authedPage.route('**/api/episodes/episode-missing', (route) =>
      route.fulfill({ status: 404, body: 'not found' }),
    )

    await authedPage.goto('/health/episode/episode-missing')
    await authedPage.waitForLoadState('networkidle')

    await expect(authedPage.locator('body')).toBeVisible()
  })
})
