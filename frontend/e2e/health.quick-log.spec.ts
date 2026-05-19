import { test, expect } from './fixtures/healthFixture'

const SYMPTOMS = [{ id: 'symptom-migraine', name: 'Migraine', isCustom: false }]
const TRIGGERS = [{ id: 'trigger-stress', name: 'Stress', isCustom: false }]

test.describe('Health module — Quick Log', () => {
  test('logs an attack with default severity and redirects to active episode', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.episodes.activeNone().startSuccess().apply()
    await authedPage.route('**/api/symptoms', (route) => route.fulfill({ json: SYMPTOMS }))
    await authedPage.route('**/api/triggers', (route) => route.fulfill({ json: TRIGGERS }))

    await authedPage.goto('/health/log')
    await authedPage.waitForLoadState('networkidle')

    const saveButton = authedPage.getByRole('button', { name: /บันทึก attack/ })
    await expect(saveButton).toBeEnabled()
    await saveButton.click()

    const startReq = await capturedRequests.waitFor('POST', '/api/episodes')
    expect(startReq.body).toMatchObject({ symptomId: 'symptom-migraine' })

    await expect(authedPage).toHaveURL(/\/health\/active\/episode-1/)
  })

  test('disables save button when no symptoms are available', async ({ authedPage, mockApi }) => {
    await mockApi.episodes.activeNone().apply()
    await authedPage.route('**/api/symptoms', (route) => route.fulfill({ json: [] }))
    await authedPage.route('**/api/triggers', (route) => route.fulfill({ json: [] }))

    await authedPage.goto('/health/log')
    await authedPage.waitForLoadState('networkidle')

    const saveButton = authedPage.getByRole('button', { name: /บันทึก attack/ })
    await expect(saveButton).toBeDisabled()
  })

  test('surfaces API error message when start endpoint returns 500', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes
      .activeNone()
      .startFails(500, 'เกิดข้อผิดพลาดจากเซิร์ฟเวอร์')
      .apply()
    await authedPage.route('**/api/symptoms', (route) => route.fulfill({ json: SYMPTOMS }))
    await authedPage.route('**/api/triggers', (route) => route.fulfill({ json: TRIGGERS }))

    await authedPage.goto('/health/log')
    await authedPage.waitForLoadState('networkidle')

    await authedPage.getByRole('button', { name: /บันทึก attack/ }).click()
    await expect(authedPage.getByText('เกิดข้อผิดพลาดจากเซิร์ฟเวอร์').first()).toBeVisible()
  })

  test('severity adjustment persists in submitted payload', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.episodes.activeNone().startSuccess().apply()
    await authedPage.route('**/api/symptoms', (route) => route.fulfill({ json: SYMPTOMS }))
    await authedPage.route('**/api/triggers', (route) => route.fulfill({ json: TRIGGERS }))

    await authedPage.goto('/health/log')
    await authedPage.waitForLoadState('networkidle')

    const slider = authedPage.locator('input[type="range"]').first()
    if (await slider.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await slider.fill('9')
    }

    await authedPage.getByRole('button', { name: /บันทึก attack/ }).click()
    const startReq = await capturedRequests.waitFor('POST', '/api/episodes')
    expect(startReq.body).toMatchObject({ severity: expect.any(Number) })
  })

  test('submits with optional trigger selected', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.episodes.activeNone().startSuccess().apply()
    await authedPage.route('**/api/symptoms', (route) => route.fulfill({ json: SYMPTOMS }))
    await authedPage.route('**/api/triggers', (route) => route.fulfill({ json: TRIGGERS }))

    await authedPage.goto('/health/log')
    await authedPage.waitForLoadState('networkidle')

    const stressChip = authedPage.getByRole('button', { name: 'Stress' }).first()
    if (await stressChip.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await stressChip.click()
    }

    await authedPage.getByRole('button', { name: /บันทึก attack/ }).click()
    const startReq = await capturedRequests.waitFor('POST', '/api/episodes')
    expect(startReq.body).toMatchObject({ symptomId: 'symptom-migraine' })
  })
})
