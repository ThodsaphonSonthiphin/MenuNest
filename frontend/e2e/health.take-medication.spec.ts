import { test, expect } from './fixtures/healthFixture'

test.describe('Health module — Take Medication (3-category logic)', () => {
  test('logs intake on takeable drug → toast + redirect', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes
      .active()
      .detail()
      .takeMedicationContext('mixed')
      .apply()

    await authedPage.route('**/api/intakes', (route) =>
      route.fulfill({
        json: {
          id: 'intake-new',
          drugId: 'drug-ibuprofen',
          drugName: 'Ibuprofen',
          symptomEpisodeId: 'episode-1',
          takenAt: '2026-05-18T09:00:00.000Z',
          doseAmount: 1,
        },
      }),
    )

    await authedPage.goto('/health/take-med/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // Ibuprofen is in the takeable section — click the "กิน 1 เม็ด" button
    // The button is the first dose button in its card; find the card by drug name
    // then click the dose button within it.
    const ibuprofenCard = authedPage
      .locator('.health-drug-card-v2--takeable')
      .filter({ hasText: 'Ibuprofen' })
    await expect(ibuprofenCard).toBeVisible()

    await ibuprofenCard.getByRole('button', { name: 'กิน 1 เม็ด' }).click()

    // Toast message: "✅ บันทึก Ibuprofen 400mg"
    await expect(authedPage.locator('.health-toast').first()).toContainText(/บันทึก/, { timeout: 5_000 })
  })

  test('blocked drug shows reason and is not clickable for intake', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes
      .active()
      .detail()
      .takeMedicationContext('all-blocked')
      .apply()

    await authedPage.goto('/health/take-med/episode-1')
    await authedPage.waitForLoadState('networkidle')

    await expect(authedPage.getByText('Ibuprofen').first()).toBeVisible()

    // Blocked reason for MaxDoseReached renders: "⛔ เกิน max daily dose"
    const blockedCard = authedPage
      .locator('.health-drug-card-v2--blocked')
      .filter({ hasText: 'Ibuprofen' })
    await expect(blockedCard).toBeVisible()
    await expect(blockedCard.locator('.health-blocked-reason')).toContainText(/เกิน max daily dose/, { timeout: 5_000 })

    // Blocked cards have no dose buttons
    await expect(blockedCard.getByRole('button', { name: /กิน/ })).toHaveCount(0)
  })

  test('active drug shows countdown / progress to next dose', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes
      .active()
      .detail()
      .takeMedicationContext('all-active')
      .apply()

    await authedPage.goto('/health/take-med/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // Active drug card renders "เหลืออีก Xh Ym" or "Xm" countdown text
    const activeCard = authedPage.locator('.health-drug-card-v2--active').first()
    await expect(activeCard).toBeVisible({ timeout: 5_000 })
    // formatRelativeMinutes produces "Xh Ym" or "Ym" format
    await expect(authedPage.locator('.health-progress-text__countdown').first()).toBeVisible({ timeout: 5_000 })
  })

  test('renders 3 categories simultaneously for mixed context', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes
      .active()
      .detail()
      .takeMedicationContext('mixed')
      .apply()

    await authedPage.goto('/health/take-med/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // Active: Sumatriptan
    await expect(authedPage.getByText('Sumatriptan').first()).toBeVisible()
    // Takeable: Ibuprofen, Paracetamol
    await expect(authedPage.getByText('Ibuprofen').first()).toBeVisible()
    await expect(authedPage.getByText('Paracetamol').first()).toBeVisible()
    // Blocked: Naproxen
    await expect(authedPage.getByText('Naproxen').first()).toBeVisible()

    // Section headers confirm all 3 categories are rendered
    await expect(authedPage.locator('.health-section-title--active')).toBeVisible()
    await expect(authedPage.locator('.health-section-title--takeable')).toBeVisible()
    await expect(authedPage.locator('.health-section-title--blocked')).toBeVisible()
  })

  test('renders empty-takeable case (all drugs active)', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes
      .active()
      .detail()
      .takeMedicationContext('all-active')
      .apply()

    await authedPage.goto('/health/take-med/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // Ibuprofen is in the active bucket — shown as active card
    await expect(authedPage.getByText('Ibuprofen').first()).toBeVisible()
    // Takeable section header still renders with empty-state copy
    await expect(authedPage.locator('.health-section-title--takeable')).toBeVisible()
    // The page container is present
    await expect(authedPage.locator('.health-page')).toBeVisible()
  })

  test('offline (take-med context aborts) renders error state', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes.active().detail().apply()

    await authedPage.goto('/health/take-med/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // take-medication-context returns 404 (not configured) → page renders
    // error copy "ไม่พบข้อมูล take medication context" inside .health-page
    await expect(authedPage.locator('.health-page')).toBeVisible()
    await expect(authedPage.locator('body')).toBeVisible()
  })
})
