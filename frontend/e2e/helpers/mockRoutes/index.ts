import type { Page } from '@playwright/test'
import type { RequestCapture } from './types'
import { createEpisodeMocks } from './episodeRoutes'

export const createMockApi = (page: Page, capture: RequestCapture) => ({
  episodes: createEpisodeMocks(page, capture),
})

export type MockApi = ReturnType<typeof createMockApi>
export { createCapture } from './types'
export type { RequestCapture, CapturedRequest } from './types'
