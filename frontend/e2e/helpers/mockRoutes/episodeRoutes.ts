import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import type { Page } from '@playwright/test'
import { recordRequest, type RequestCapture } from './types'

const baseDir = dirname(fileURLToPath(import.meta.url))
const mocksDir = join(baseDir, '..', '..', 'mocks')

const readJson = <T>(relativePath: string): T => {
  const raw = readFileSync(join(mocksDir, relativePath), 'utf-8')
  return JSON.parse(raw) as T
}

type EpisodeConfig = {
  listResponse: unknown
  activeResponse: unknown
  detailResponse: unknown
  takeMedContextResponse: unknown | null
  startResponseStatus: number
  startResponseBody: unknown
  resolveResponseStatus: number
  deleteResponseStatus: number
  updateResponseStatus: number
}

export const createEpisodeMocks = (page: Page, capture: RequestCapture) => {
  const config: EpisodeConfig = {
    listResponse: readJson('episodes/empty-list.json'),
    activeResponse: readJson('episodes/active-none.json'),
    detailResponse: readJson('episodes/detail-with-intakes.json'),
    takeMedContextResponse: null,
    startResponseStatus: 200,
    startResponseBody: readJson('episodes/start-success.json'),
    resolveResponseStatus: 200,
    deleteResponseStatus: 204,
    updateResponseStatus: 200,
  }

  const self = {
    list: (data?: unknown) => {
      config.listResponse = data ?? readJson('episodes/empty-list.json')
      return self
    },
    active: (data?: unknown) => {
      config.activeResponse = data ?? readJson('episodes/active-single.json')
      return self
    },
    activeNone: () => {
      config.activeResponse = readJson('episodes/active-none.json')
      return self
    },
    detail: (data?: unknown) => {
      config.detailResponse = data ?? readJson('episodes/detail-with-intakes.json')
      return self
    },
    takeMedicationContext: (variant: 'all-takeable' | 'mixed' | 'all-blocked' | 'all-active') => {
      config.takeMedContextResponse = readJson(`contexts/${variant}.json`)
      return self
    },
    startSuccess: (data?: unknown) => {
      config.startResponseStatus = 200
      config.startResponseBody = data ?? readJson('episodes/start-success.json')
      return self
    },
    startFails: (status: number, body?: string) => {
      config.startResponseStatus = status
      config.startResponseBody = body ?? 'start failed'
      return self
    },
    resolveFails: (status: number) => {
      config.resolveResponseStatus = status
      return self
    },
    deleteFails: (status: number) => {
      config.deleteResponseStatus = status
      return self
    },
    apply: async () => {
      // Register least-specific first; Playwright uses last-registered-wins,
      // so specific routes registered later take priority over the catchall.
      await page.route('**/api/episodes', async (route, request) => {
        await recordRequest(route, request, capture)
        const method = request.method()
        if (method === 'POST') {
          if (config.startResponseStatus >= 400) {
            return route.fulfill({
              status: config.startResponseStatus,
              contentType: 'text/plain',
              body: String(config.startResponseBody),
            })
          }
          return route.fulfill({ json: config.startResponseBody })
        }
        return route.fulfill({ json: config.listResponse })
      })
      await page.route('**/api/episodes/*', async (route, request) => {
        await recordRequest(route, request, capture)
        const pathname = new URL(request.url()).pathname
        const parts = pathname.split('/').filter(Boolean)
        if (parts.length !== 3 || parts[2] === 'active') {
          return route.fallback()
        }
        const method = request.method()
        if (method === 'GET') return route.fulfill({ json: config.detailResponse })
        if (method === 'PUT') return route.fulfill({ status: config.updateResponseStatus, body: '' })
        if (method === 'DELETE') return route.fulfill({ status: config.deleteResponseStatus, body: '' })
        return route.fallback()
      })
      await page.route('**/api/episodes/*/resolve', async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ status: config.resolveResponseStatus, body: '' })
      })
      await page.route(
        '**/api/episodes/*/take-medication-context',
        async (route, request) => {
          await recordRequest(route, request, capture)
          if (config.takeMedContextResponse === null) {
            return route.fulfill({ status: 404, body: 'no context configured' })
          }
          await route.fulfill({ json: config.takeMedContextResponse })
        },
      )
      await page.route('**/api/episodes/active', async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ json: config.activeResponse })
      })
      return self
    },
  }
  return self
}

export type EpisodeMocks = ReturnType<typeof createEpisodeMocks>
