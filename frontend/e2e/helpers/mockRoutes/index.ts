import type { Page } from '@playwright/test'
import type { RequestCapture } from './types'
import { createEpisodeMocks } from './episodeRoutes'
import { createReportMocks } from './reportRoutes'

export const createMockApi = (page: Page, capture: RequestCapture) => ({
  episodes: createEpisodeMocks(page, capture),
  report: createReportMocks(page, capture),
})

export type MockApi = ReturnType<typeof createMockApi>
export { createCapture } from './types'
export type { RequestCapture, CapturedRequest } from './types'
