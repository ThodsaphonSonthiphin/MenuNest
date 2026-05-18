import { test, expect } from '@playwright/test'
import { applyGoogleAuth, healthMocks, mockHealthApiRoutes } from './helpers/healthTestUtils'

test.describe('Health module — functional tests', () => {
  test('logs a new migraine attack and navigates to active episode', async ({ page }) => {
    await applyGoogleAuth(page)
    let startPayload: unknown = null

    await mockHealthApiRoutes(page, {
      onStartEpisodeRequest: (body) => {
        startPayload = body
      },
    })

    await page.goto('/health/log')

    const saveButton = page.getByRole('button', { name: /บันทึก attack/ })
    await expect(saveButton).toBeEnabled()
    await saveButton.click()

    await expect(page).toHaveURL(/\/health\/active\/episode-1/)
    await expect(page.getByText('Active Attack')).toBeVisible()

    await expect.poll(() => (startPayload as { symptomId?: string } | null)?.symptomId).toBe(
      'symptom-migraine',
    )
    expect(startPayload).toMatchObject({ severity: 7 })
  })

  test('logs medication intake and shows success toast', async ({ page }) => {
    await applyGoogleAuth(page)
    let intakePayload: unknown = null

    await mockHealthApiRoutes(page, {
      episodeDetail: healthMocks.episodeDetailWithIntake,
      onLogIntakeRequest: (body) => {
        intakePayload = body
      },
    })

    await page.goto('/health/take-med/episode-1')

    const takeOne = page.getByRole('button', { name: 'กิน 1 เม็ด' })
    await expect(takeOne).toBeEnabled()
    await takeOne.click()

    await expect(page.locator('.health-toast')).toContainText('บันทึก')
    await expect(page).toHaveURL(/\/health\/active\/episode-1/)
    await expect(page.getByText('✅ บันทึกเวลากินยาแล้ว')).toBeVisible()

    await expect.poll(() => (intakePayload as { drugId?: string } | null)?.drugId).toBe(
      'drug-ibuprofen',
    )
    expect(intakePayload).toMatchObject({ doseAmount: 1, symptomEpisodeId: 'episode-1' })
  })
})
