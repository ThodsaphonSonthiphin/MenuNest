import { test as base, type Page } from '@playwright/test'
import { applyGoogleAuth, type GoogleTokenPayload } from '../helpers/healthTestUtils'
import { createMockApi, createCapture, type MockApi, type RequestCapture } from '../helpers/mockRoutes'

export type HealthFixtures = {
  authedPage: Page
  mockApi: MockApi
  capturedRequests: RequestCapture
  googleAuth: (payload?: GoogleTokenPayload) => Promise<void>
}

export const test = base.extend<HealthFixtures>({
  capturedRequests: async ({}, use) => {
    const capture = createCapture()
    await use(capture)
  },

  mockApi: async ({ page, capturedRequests }, use) => {
    const api = createMockApi(page, capturedRequests)
    await use(api)
  },

  googleAuth: async ({ page }, use) => {
    const apply = async (payload?: GoogleTokenPayload) => {
      await applyGoogleAuth(page, payload)
    }
    await use(apply)
  },

  authedPage: async ({ page, googleAuth }, use) => {
    await googleAuth()
    await use(page)
  },
})

export { expect } from '@playwright/test'
