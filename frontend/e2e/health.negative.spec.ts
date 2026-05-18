import { test, expect } from '@playwright/test'
import { applyGoogleAuth, mockHealthApiRoutes } from './helpers/healthTestUtils'

test.describe('Health module — negative tests', () => {
  test('disables save when symptom data is missing', async ({ page }) => {
    await applyGoogleAuth(page)
    await mockHealthApiRoutes(page, { symptoms: [] })

    await page.goto('/health/log')

    const saveButton = page.getByRole('button', { name: /บันทึก attack/ })
    await expect(saveButton).toBeDisabled()
  })

  test('shows API error when quick log fails', async ({ page }) => {
    await applyGoogleAuth(page)
    await mockHealthApiRoutes(page, {
      startEpisodeStatus: 500,
      startEpisodeBody: 'เกิดข้อผิดพลาดจากเซิร์ฟเวอร์',
    })

    await page.goto('/health/log')

    const saveButton = page.getByRole('button', { name: /บันทึก attack/ })
    await expect(saveButton).toBeEnabled()
    await saveButton.click()

    await expect(page.getByText('เกิดข้อผิดพลาดจากเซิร์ฟเวอร์')).toBeVisible()
  })

  test('surfaces offline state when take-medication context is unavailable', async ({ page }) => {
    await applyGoogleAuth(page)
    await mockHealthApiRoutes(page, { takeMedicationContextAbort: true })

    await page.goto('/health/take-med/episode-1')

    await expect(page.getByText('ไม่พบข้อมูล take medication context')).toBeVisible()
  })
})
