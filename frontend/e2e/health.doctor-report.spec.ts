import { test, expect } from './fixtures/healthFixture'

test.describe('Health module — Doctor Report', () => {
  test('valid token renders summary, trigger correlation, and efficacy', async ({
    page,
    mockApi,
  }) => {
    await mockApi.report.publicReport().apply()

    await page.goto('/share/valid-token-abc')
    await page.waitForLoadState('networkidle')

    await expect(page.getByText('ทดสอบ ใจดี')).toBeVisible()
    await expect(page.getByText('Stress').first()).toBeVisible()
    await expect(page.getByText('Ibuprofen').first()).toBeVisible()
  })

  test('empty-data report renders without crashing', async ({ page, mockApi }) => {
    await mockApi.report.empty().apply()

    await page.goto('/share/valid-token-empty')
    await page.waitForLoadState('networkidle')

    await expect(page.locator('body')).toBeVisible()
    await expect(page.getByText('ทดสอบ ใจดี')).toBeVisible()
  })

  test('high MOH risk renders a danger/warning flag', async ({ page, mockApi }) => {
    await mockApi.report.highRisk().apply()

    await page.goto('/share/valid-token-risk')
    await page.waitForLoadState('networkidle')

    await expect(page.getByText(/Medication overuse|MOH|HIGH/i).first()).toBeVisible()
  })

  test('invalid token still surfaces error message', async ({ page, mockApi }) => {
    await mockApi.report.invalidToken(410).apply()

    await page.goto('/share/invalid-token-xxx')
    await page.waitForLoadState('networkidle')

    await expect(page.locator('body')).toContainText(/(เพิกถอน|หมดอายุ|ผิดพลาด|ไม่พบ)/)
  })
})
