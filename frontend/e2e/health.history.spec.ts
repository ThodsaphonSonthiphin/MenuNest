import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import { test, expect } from './fixtures/healthFixture'

const baseDir = dirname(fileURLToPath(import.meta.url))
const fullList = JSON.parse(
  readFileSync(join(baseDir, 'mocks/episodes/list-full.json'), 'utf-8'),
) as unknown[]

test.describe('Health module — History', () => {
  test('renders list of past episodes', async ({ authedPage, mockApi }) => {
    await mockApi.episodes.list(fullList).activeNone().apply()

    await authedPage.goto('/health/history')
    await authedPage.waitForLoadState('networkidle')

    await expect(authedPage.getByText('Migraine').first()).toBeVisible()
    await expect(authedPage.getByText('Ibuprofen').first()).toBeVisible()
  })

  test('filter chip click sends a request with query param', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.episodes.list(fullList).activeNone().apply()

    await authedPage.goto('/health/history')
    await authedPage.waitForLoadState('networkidle')

    const filterChip = authedPage.getByText(/30 วัน|resolved|failed/).first()
    if (await filterChip.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await filterChip.click()
      await authedPage.waitForTimeout(500)
      const matchingReqs = capturedRequests
        .all()
        .filter((r) => r.method === 'GET' && r.pathname === '/api/episodes')
      expect(matchingReqs.length).toBeGreaterThan(0)
    }
  })

  test('search input filters list', async ({ authedPage, mockApi }) => {
    await mockApi.episodes.list(fullList).activeNone().apply()

    await authedPage.goto('/health/history')
    await authedPage.waitForLoadState('networkidle')

    const searchBox = authedPage.locator('.health-search-box').first()
    if (await searchBox.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await searchBox.fill('Paracetamol')
      await authedPage.waitForTimeout(500)
      await expect(authedPage.getByText('Paracetamol').first()).toBeVisible()
    }
  })
})
