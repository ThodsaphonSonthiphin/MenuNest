import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

// Smoke coverage for the writing-practice History screen (#97). This is the
// first use of @syncfusion/react-grid anywhere in the app -- the same class
// of Syncfusion asset/rendering gap that shipped a broken RTE toolbar to prod
// for /writing (missing stylesheet import, invisible to tsc/build/vitest).
// This spec exists specifically to catch that class of bug for the grid too,
// including the header-fallback bug where an empty `headerText` on a Column
// falls through to the raw `field` name (`id`) instead of a real label.
const ENTRIES = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    date: '2026-06-01',
    text: '<p>Corrected entry text.</p>',
    elapsedSeconds: 420,
    wordsPerMinute: 30,
    correctedAt: '2026-06-02T10:00:00Z',
    createdAt: '2026-06-01T09:00:00Z',
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    date: '2026-06-03',
    text: '<p>Pending entry text.</p>',
    elapsedSeconds: 400,
    wordsPerMinute: 28,
    correctedAt: null,
    createdAt: '2026-06-03T09:00:00Z',
  },
]

test.describe('Writing — history grid smoke', () => {
  test.beforeEach(async ({ page }) => {
    await page.route('**/api/writing-entries', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(ENTRIES),
      })
    })
  })

  test('renders both rows with status badges and correct column headers', async ({ authedPage: page }) => {
    await page.goto('/writing/history')

    const grid = page.getByRole('grid')
    await expect(grid).toBeVisible({ timeout: 20000 })

    // header + 2 data rows
    await expect(grid.getByRole('row')).toHaveCount(3)

    // Both entries rendered.
    await expect(page.getByText('Corrected entry text.')).toBeVisible()
    await expect(page.getByText('Pending entry text.')).toBeVisible()

    // Status badges for each row.
    await expect(page.getByText('🔒 ตรวจแล้ว').first()).toBeVisible()
    await expect(page.getByText('⏳ รอตรวจ').first()).toBeVisible()

    // Column headers are the real Thai labels -- in particular the action
    // column must NOT fall back to the raw field name `id` (Important 1).
    await expect(page.getByRole('columnheader', { name: 'วันที่' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'ข้อความ' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'สถานะ' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'เปิด' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'id', exact: true })).toHaveCount(0)
  })
})
