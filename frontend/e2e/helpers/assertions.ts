import { expect, type Page } from '@playwright/test'

export const expectToastContains = async (page: Page, text: string | RegExp) => {
  await expect(page.locator('.health-toast').first()).toContainText(text, { timeout: 5_000 })
}

export const expectErrorBannerContains = async (page: Page, text: string | RegExp) => {
  await expect(page.getByText(text).first()).toBeVisible({ timeout: 5_000 })
}

export const expectNoConsoleErrors = (page: Page) => {
  const errors: string[] = []
  page.on('pageerror', (err) => errors.push(err.message))
  return {
    assert: () => expect(errors, `page errors detected:\n${errors.join('\n')}`).toEqual([]),
  }
}
