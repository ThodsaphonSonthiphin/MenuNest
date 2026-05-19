import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import type { Page } from '@playwright/test'
import { recordRequest, type RequestCapture } from './types'

const baseDir = dirname(fileURLToPath(import.meta.url))
const mocksDir = join(baseDir, '..', '..', 'mocks')

const readJson = <T>(path: string): T => JSON.parse(readFileSync(join(mocksDir, path), 'utf-8')) as T

type ReportConfig = {
  publicReport: unknown
  publicReportStatus: number
}

export const createReportMocks = (page: Page, capture: RequestCapture) => {
  const config: ReportConfig = {
    publicReport: readJson('reports/full-report.json'),
    publicReportStatus: 200,
  }

  const self = {
    publicReport: (data?: unknown) => {
      config.publicReport = data ?? readJson('reports/full-report.json')
      config.publicReportStatus = 200
      return self
    },
    empty: () => {
      config.publicReport = readJson('reports/empty-report.json')
      config.publicReportStatus = 200
      return self
    },
    highRisk: () => {
      config.publicReport = readJson('reports/high-risk-report.json')
      config.publicReportStatus = 200
      return self
    },
    invalidToken: (status = 410) => {
      config.publicReportStatus = status
      return self
    },
    apply: async () => {
      await page.route('**/api/public/report**', async (route, request) => {
        await recordRequest(route, request, capture)
        if (config.publicReportStatus >= 400) {
          return route.fulfill({ status: config.publicReportStatus, body: 'token error' })
        }
        return route.fulfill({ json: config.publicReport })
      })
    },
  }
  return self
}

export type ReportMocks = ReturnType<typeof createReportMocks>
