import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

// The mid-edit lock defect (WMCP-26). A correction landing over MCP while the
// writer is editing used to leave Save enabled, fail the PUT, and show a
// "try again" message for something that can never succeed. This spec proves
// the live poll notices the correction and swaps the page to ผลตรวจ without a
// reload (ADR-177: the corrected page IS the result screen, so the old
// "ตรวจแล้ว — แก้ข้อความไม่ได้" note no longer exists).
const ENTRY_ID = '22222222-2222-2222-2222-222222222222'

const pending = {
  id: ENTRY_ID,
  date: '2026-08-16',
  text: '<p>Pending entry text.</p>',
  elapsedSeconds: 420,
  wordsPerMinute: 28,
  correctedAt: null,
  createdAt: '2026-08-16T09:00:00Z',
  correction: null,
}

const corrected = {
  ...pending,
  correctedAt: '2026-08-17T02:00:00Z',
  correction: {
    targetRule: 'articles (a/an/the)',
    markedText: '<p>Pending <span class="hit">entry</span> text.</p>',
    hitCount: 1,
    missCount: 0,
    thaiWhyLine: 'คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ',
    sentenceCombiningItems: [],
    stuckWords: [],
    errorsPer100Words: 0,
  },
}

test.describe('Writing — live lock while editing', () => {
  test('swaps to ผลตรวจ and drops Save when a correction lands mid-edit', async ({ authedPage: page }) => {
    let hasBeenCorrected = false
    await page.route(`**/api/writing-entries/${ENTRY_ID}`, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(hasBeenCorrected ? corrected : pending),
      })
    })

    await page.goto(`/writing/history/${ENTRY_ID}`)
    await page.getByRole('button', { name: 'แก้ไข' }).click()
    await expect(page.getByRole('button', { name: 'บันทึก' })).toBeVisible()

    // The correction lands over MCP; the page must notice on its own via the poll.
    hasBeenCorrected = true

    await expect(page.getByText('ผลตรวจ ·')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('button', { name: 'บันทึก' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'แก้ไข' })).toHaveCount(0)
    // Delete stays available even when locked (ADR-169).
    await expect(page.getByRole('button', { name: 'ลบ' })).toBeVisible()
    // No "try again" message should ever have appeared in this flow.
    await expect(page.getByText('ลองอีกครั้ง')).toHaveCount(0)
  })
})
