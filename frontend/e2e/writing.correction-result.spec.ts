import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

// Rendering coverage for the ผลตรวจ screen (ADR-177/178). The frontend unit
// suite runs in node with no DOM, so this spec is the only automated check that
// the five blocks actually render — the exact gap that shipped an unstyled RTE
// toolbar to prod on this same feature.
const THAI_ONLY_ID = '33333333-3333-3333-3333-333333333333'
const ENGLISH_ID = '44444444-4444-4444-4444-444444444444'

// The real production correction: a Thai-only night, nothing markable.
const thaiOnly = {
  id: THAI_ONLY_ID,
  date: '2026-08-16',
  text: '<p>[วันนี้พาลูกสาวไปกินข้าวเย็น]</p>',
  elapsedSeconds: 41,
  wordsPerMinute: 5.9,
  correctedAt: '2026-08-17T14:57:23Z',
  createdAt: '2026-08-16T15:00:00Z',
  correction: {
    targetRule: 'articles (a/an/the)',
    markedText: '<p><span class="th">[วันนี้พาลูกสาวไปกินข้าวเย็น]</span></p>',
    hitCount: 0,
    missCount: 0,
    thaiWhyLine: 'คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ ห้ามลอยเปล่า',
    sentenceCombiningItems: [],
    stuckWords: [
      { thai: 'วันนี้พาลูกสาวไปกินข้าวเย็น', english: 'Today I took my daughter out for dinner.' },
    ],
    errorsPer100Words: 0,
  },
}

const englishNight = {
  id: ENGLISH_ID,
  date: '2026-08-15',
  text: '<p>Today my daughter go to school.</p>',
  elapsedSeconds: 420,
  wordsPerMinute: 8.1,
  correctedAt: '2026-08-16T02:00:00Z',
  createdAt: '2026-08-15T15:00:00Z',
  correction: {
    targetRule: 'กริยาเติม -s',
    markedText:
      '<p>Today my daughter <span class="miss">go</span> <span class="fix">→ goes</span> to school.</p>',
    hitCount: 1,
    missCount: 8,
    thaiWhyLine: 'ประธานเป็น he / she / it → กริยาต้องเติม -s เสมอ',
    sentenceCombiningItems: [
      { source: 'Traffic is very bad. + We arrive late.', combined: 'Traffic was very bad, so we arrived late.' },
    ],
    stuckWords: [{ thai: 'ข้าวต้ม', english: 'rice porridge / congee' }],
    errorsPer100Words: 14,
  },
}

test.describe('Writing — ผลตรวจ screen', () => {
  test('a Thai-only night renders all five blocks, empty ones saying why', async ({ authedPage: page }) => {
    await page.route(`**/api/writing-entries/${THAI_ONLY_ID}`, async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(thaiOnly) })
    })

    await page.goto(`/writing/history/${THAI_ONLY_ID}`)

    await expect(page.getByText('ผลตรวจ ·')).toBeVisible()
    // The raw text block is gone: block 1's marked text IS that text (ADR-177).
    await expect(page.locator('.writing-detail-text')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'แก้ไข' })).toHaveCount(0)

    // All five blocks, in order.
    await expect(page.locator('.correction-block')).toHaveCount(5)
    await expect(page.getByText('เป้าหมายตอนนี้ · articles (a/an/the)')).toBeVisible()
    await expect(page.getByText('ทำไม (ภาษาไทย)')).toBeVisible()
    await expect(page.getByText('ต่อประโยค (จากประโยคของคุณเอง)')).toBeVisible()
    await expect(page.getByText('คำที่นึกไม่ออก (จาก [วงเล็บ])')).toBeVisible()
    await expect(page.getByText('ตัวเลขวันนี้')).toBeVisible()

    // The empty blocks say why they are empty rather than disappearing.
    await expect(page.getByText('ต้องเติม 0 ที่ · ถูก 0 · พลาด 0')).toBeVisible()
    await expect(page.getByText('คืนนี้ไม่มีจุดไหนเข้ากฎนี้')).toBeVisible()
    await expect(page.getByText('คืนนี้ไม่มีประโยคอังกฤษให้ต่อ')).toBeVisible()

    // The stuck word is a two-line card, not a pill.
    await expect(page.locator('.correction-stuck__thai')).toHaveText('วันนี้พาลูกสาวไปกินข้าวเย็น')
    await expect(page.locator('.correction-stuck__english')).toHaveText(
      'Today I took my daughter out for dinner.',
    )

    // The marked Thai bracket survived the sanitizer with its class intact.
    await expect(page.locator('.correction-marked span.th')).toHaveCount(1)

    await expect(page.getByText('5.9')).toBeVisible()
    await expect(page.getByText('0.0')).toBeVisible()
    await expect(page.getByText('สิ่งที่ระบบจะไม่ทำเด็ดขาด')).toBeVisible()
    await expect(page.getByRole('button', { name: 'ลบ' })).toBeVisible()
  })

  test('an English night renders the marks and the populated blocks', async ({ authedPage: page }) => {
    await page.route(`**/api/writing-entries/${ENGLISH_ID}`, async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(englishNight) })
    })

    await page.goto(`/writing/history/${ENGLISH_ID}`)

    await expect(page.locator('.correction-marked span.miss')).toHaveText('go')
    await expect(page.locator('.correction-marked span.fix')).toHaveText('→ goes')
    await expect(page.getByText('ต้องเติม 9 ที่ · ถูก 1 · พลาด 8')).toBeVisible()
    await expect(page.getByText('Traffic was very bad, so we arrived late.')).toBeVisible()
    await expect(page.locator('.correction-stuck__thai')).toHaveText('ข้าวต้ม')
    await expect(page.getByText('14.0')).toBeVisible()
    // No empty-state line should appear on a fully populated night.
    await expect(page.getByText('คืนนี้ไม่มีประโยคอังกฤษให้ต่อ')).toHaveCount(0)
  })

  test('a pending night still shows the text, the edit button and the badge', async ({ authedPage: page }) => {
    const pendingId = '55555555-5555-5555-5555-555555555555'
    await page.route(`**/api/writing-entries/${pendingId}`, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...thaiOnly, id: pendingId, correctedAt: null, correction: null }),
      })
    })

    await page.goto(`/writing/history/${pendingId}`)

    await expect(page.getByText('⏳ รอตรวจ')).toBeVisible()
    await expect(page.locator('.writing-detail-text')).toBeVisible()
    await expect(page.getByRole('button', { name: 'แก้ไข' })).toBeVisible()
    await expect(page.locator('.correction-block')).toHaveCount(0)
  })

  // Two authorised additions beyond the brief (SDD Task 6 controller note).
  //
  // 1. The error-branch copy. A fix round to Task 5 split the entry guard
  // in WritingEntryDetailPage.tsx into two branches keyed off whether the
  // query error status is 404 -- nothing covered either branch, which would
  // let a refactor silently re-merge them and re-ship the regression that
  // told a writer their night may have been deleted after a merely
  // transient failure.
  test('a 404 says the entry may have been deleted', async ({ authedPage: page }) => {
    const notFoundId = '77777777-7777-7777-7777-777777777777'
    await page.route(`**/api/writing-entries/${notFoundId}`, async (route) => {
      await route.fulfill({
        status: 404,
        contentType: 'application/json',
        body: JSON.stringify({ title: 'Not Found', status: 404 }),
      })
    })

    await page.goto(`/writing/history/${notFoundId}`)

    await expect(page.getByText('ไม่พบรายการนี้ (อาจถูกลบไปแล้ว)')).toBeVisible()
  })

  test('a 500 says loading failed, not that the entry is gone', async ({ authedPage: page }) => {
    const errorId = '88888888-8888-8888-8888-888888888888'
    await page.route(`**/api/writing-entries/${errorId}`, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ title: 'Internal Server Error', status: 500 }),
      })
    })

    await page.goto(`/writing/history/${errorId}`)

    await expect(page.getByText('โหลดไม่สำเร็จ')).toBeVisible()
  })

  // 2. The poll actually stops (ADR-179). Counting requests AFTER the
  // correction lands is the only way to prove the poll stopped rather than
  // merely appeared to -- the component sets pollingInterval to 0 once
  // entry.correctedAt is truthy (WritingEntryDetailPage.tsx); the interval
  // itself is 15 seconds, so the wait below must clear a full interval with
  // room to spare.
  test('the poll stops firing once correctedAt appears', async ({ authedPage: page }) => {
    test.setTimeout(60_000)
    const pollId = '66666666-6666-6666-6666-666666666666'
    let requestCount = 0
    let isCorrected = false

    await page.route(`**/api/writing-entries/${pollId}`, async (route) => {
      requestCount += 1
      const body = isCorrected
        ? { ...thaiOnly, id: pollId }
        : { ...thaiOnly, id: pollId, correctedAt: null, correction: null }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
    })

    await page.goto(`/writing/history/${pollId}`)
    await expect(page.getByText('⏳ รอตรวจ')).toBeVisible()

    // The correction lands over MCP; give the 15s poll interval a comfortable
    // margin to notice it.
    isCorrected = true
    await expect(page.getByText('ผลตรวจ ·')).toBeVisible({ timeout: 25_000 })

    const countAfterCorrection = requestCount
    // Comfortably longer than one full 15s poll interval. If pollingInterval
    // had NOT dropped to 0, this window would catch at least one more fetch.
    await page.waitForTimeout(20_000)
    expect(requestCount).toBe(countAfterCorrection)
  })
})
