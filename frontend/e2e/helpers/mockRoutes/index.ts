import type { Page } from '@playwright/test'
import type { RequestCapture } from './types'
import { createEpisodeMocks } from './episodeRoutes'
import { createReportMocks } from './reportRoutes'
import { createDrugMocks } from './drugRoutes'
import { createSettingsMocks } from './settingsRoutes'

export const createMockApi = (page: Page, capture: RequestCapture) => ({
  episodes: createEpisodeMocks(page, capture),
  report: createReportMocks(page, capture),
  drugs: createDrugMocks(page, capture),
  settings: createSettingsMocks(page, capture),
})

export type MockApi = ReturnType<typeof createMockApi>
export { createCapture } from './types'
export type { RequestCapture, CapturedRequest } from './types'
