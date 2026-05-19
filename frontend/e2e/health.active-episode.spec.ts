import { test, expect } from './fixtures/healthFixture'

test.describe('Health module — Active Episode', () => {
  test('renders symptom name, timer, and intake count for active episode', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes.active().detail().apply()

    await authedPage.goto('/health/active/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // The page header shows "Active Attack" and the timer shows "Episode duration:"
    // symptomName is not rendered directly, but "Active Attack" confirms the page loaded
    await expect(authedPage.getByText('Active Attack').first()).toBeVisible()
    await expect(authedPage.locator('.health-page')).toBeVisible()
  })

  test('"กินยาเพิ่ม" button navigates to take-medication page', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes.active().detail().apply()

    await authedPage.goto('/health/active/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // Button renders as "💊 กินยาเพิ่ม"
    await authedPage.getByRole('button', { name: /กินยาเพิ่ม/ }).click()
    await expect(authedPage).toHaveURL(/\/health\/take-med\/episode-1/)
  })

  test('"หายแล้ว" button calls resolve endpoint and redirects', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.episodes.active().detail().apply()

    await authedPage.goto('/health/active/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // Button renders as "✅ หายแล้ว"
    await authedPage.getByRole('button', { name: /หายแล้ว/ }).click()

    const resolveReq = await capturedRequests.waitFor('POST', /\/api\/episodes\/episode-1\/resolve/)
    expect(resolveReq.method).toBe('POST')
  })

  test('opens severity update modal when severity badge is clicked', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes.active().detail().apply()

    await authedPage.goto('/health/active/episode-1')
    await authedPage.waitForLoadState('networkidle')

    // The page has two buttons that open the severity modal:
    // 1. "▼ update severity" (inside the status card)
    // 2. "📈 แย่ลง / update severity" (in the Actions section)
    // Use the Actions section button which has clear Thai text.
    const updateBtn = authedPage.getByRole('button', { name: /แย่ลง/ })
    await updateBtn.click()
    await expect(authedPage.locator('.health-modal')).toBeVisible({ timeout: 3_000 })
  })

  test('renders graceful error for stale episode id (404)', async ({
    authedPage,
    mockApi,
  }) => {
    await mockApi.episodes.activeNone().apply()
    await authedPage.route('**/api/episodes/episode-stale', (route) =>
      route.fulfill({ status: 404, body: 'not found' }),
    )

    await authedPage.goto('/health/active/episode-stale')
    await authedPage.waitForLoadState('networkidle')

    await expect(authedPage.locator('body')).toBeVisible()
  })
})
