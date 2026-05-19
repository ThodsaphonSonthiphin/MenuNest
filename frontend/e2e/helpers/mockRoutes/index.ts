import type { Page } from '@playwright/test'
import type { RequestCapture } from './types'

// Temporarily empty until Task 4 lands the episode mock builder
export const createMockApi = (page: Page, capture: RequestCapture) => ({} as Record<string, never>)

export type MockApi = ReturnType<typeof createMockApi>
export { createCapture } from './types'
export type { RequestCapture, CapturedRequest } from './types'
