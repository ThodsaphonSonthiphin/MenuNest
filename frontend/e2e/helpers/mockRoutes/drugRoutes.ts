import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import type { Page } from '@playwright/test'
import { recordRequest, type RequestCapture } from './types'

const baseDir = dirname(fileURLToPath(import.meta.url))
const mocksDir = join(baseDir, '..', '..', 'mocks')

const readJson = <T>(p: string): T => JSON.parse(readFileSync(join(mocksDir, p), 'utf-8')) as T

type DrugConfig = {
  listResponse: unknown
  createStatus: number
  createBody: unknown
  updateStatus: number
  deleteStatus: number
  sasStatus: number
  sasBody: unknown
}

export const createDrugMocks = (page: Page, capture: RequestCapture) => {
  const config: DrugConfig = {
    listResponse: readJson('drugs/list.json'),
    createStatus: 200,
    // createBody must match DrugDetailDto (photos: [] required by PhotoUploader after redirect to edit)
    createBody: {
      id: 'drug-new',
      name: 'New Drug',
      activeIngredient: null,
      drugType: 9,
      doseStrength: '100mg',
      effectDurationMinHours: 4,
      effectDurationMaxHours: 6,
      maxDailyDose: 4,
      stockCount: 0,
      expirationDate: null,
      usageNote: null,
      treatsSymptomIds: [],
      photos: [],
      createdAt: '2026-01-01T00:00:00.000Z',
      updatedAt: null,
    },
    updateStatus: 200,
    deleteStatus: 204,
    sasStatus: 200,
    sasBody: readJson('drugs/sas-success.json'),
  }

  const self = {
    list: (data?: unknown) => {
      config.listResponse = data ?? readJson('drugs/list.json')
      return self
    },
    createSuccess: (body?: unknown) => {
      config.createStatus = 200
      if (body) config.createBody = body
      return self
    },
    updateFails: (status: number) => {
      config.updateStatus = status
      return self
    },
    sasFails: (status: number) => {
      config.sasStatus = status
      return self
    },
    apply: async () => {
      // IMPORTANT: register catchall first, specific routes last (Playwright last-wins)
      // Order: /drugs (least specific) → /drugs/* → /drugs/*/photos (most specific)
      await page.route('**/api/drugs**', async (route, request) => {
        await recordRequest(route, request, capture)
        const method = request.method()
        if (method === 'POST') {
          if (config.createStatus >= 400) {
            return route.fulfill({ status: config.createStatus, body: 'create error' })
          }
          return route.fulfill({ json: config.createBody })
        }
        return route.fulfill({ json: config.listResponse })
      })
      await page.route('**/api/drugs/*', async (route, request) => {
        await recordRequest(route, request, capture)
        const method = request.method()
        if (method === 'PUT')
          return route.fulfill({ status: config.updateStatus, body: '' })
        if (method === 'DELETE')
          return route.fulfill({ status: config.deleteStatus, body: '' })
        if (method === 'GET') {
          // Return a DrugDetailDto (includes photos:[]) so PhotoUploader does not crash
          const list = config.listResponse as Record<string, unknown>[]
          const base = list[0] ?? {}
          return route.fulfill({
            json: {
              ...base,
              usageNote: null,
              photos: [],
              createdAt: '2026-01-01T00:00:00.000Z',
              updatedAt: null,
            },
          })
        }
        return route.fallback()
      })
      await page.route('**/api/drugs/*/photos', async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ status: 200, body: '' })
      })
      // unrelated path — register anywhere
      await page.route('**/api/photos/upload-sas', async (route, request) => {
        await recordRequest(route, request, capture)
        if (config.sasStatus >= 400) {
          return route.fulfill({ status: config.sasStatus, body: 'sas error' })
        }
        return route.fulfill({ json: config.sasBody })
      })
    },
  }
  return self
}

export type DrugMocks = ReturnType<typeof createDrugMocks>
