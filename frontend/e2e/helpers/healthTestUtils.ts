import { readFileSync } from 'node:fs'
import { Buffer } from 'node:buffer'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import type { Page } from '@playwright/test'

type JsonValue = Record<string, unknown> | Array<Record<string, unknown>>

const baseDir = dirname(fileURLToPath(import.meta.url))
const mocksDir = join(baseDir, '..', 'mocks')

const readMock = <T extends JsonValue>(name: string): T => {
  const raw = readFileSync(join(mocksDir, name), 'utf-8')
  return JSON.parse(raw) as T
}

export const healthMocks = {
  me: readMock<Record<string, unknown>>('me.json'),
  symptoms: readMock<Array<Record<string, unknown>>>('symptoms.json'),
  triggers: readMock<Array<Record<string, unknown>>>('triggers.json'),
  episodes: readMock<Array<Record<string, unknown>>>('episodes-empty.json'),
  startEpisode: readMock<Record<string, unknown>>('episode-start.json'),
  episodeDetail: readMock<Record<string, unknown>>('episode-detail.json'),
  episodeDetailWithIntake: readMock<Record<string, unknown>>('episode-detail-with-intake.json'),
  takeMedicationContext: readMock<Record<string, unknown>>('take-medication-context.json'),
  intake: readMock<Record<string, unknown>>('intake.json'),
}

export interface GoogleTokenPayload {
  sub?: string
  name?: string
  email?: string
}

export const buildGoogleToken = (payload: GoogleTokenPayload = {}): string => {
  const encoded = Buffer.from(
    JSON.stringify({
      sub: payload.sub ?? 'user-1',
      name: payload.name ?? 'Test User',
      email: payload.email ?? 'test@menunest.app',
    }),
  ).toString('base64')
  return `header.${encoded}.signature`
}

export const applyGoogleAuth = async (page: Page, payload?: GoogleTokenPayload) => {
  const token = buildGoogleToken(payload)
  await page.addInitScript((value) => {
    sessionStorage.setItem('google_id_token', value)
  }, token)
  return token
}

export interface HealthMockOptions {
  me?: Record<string, unknown>
  symptoms?: Array<Record<string, unknown>>
  triggers?: Array<Record<string, unknown>>
  listEpisodes?: Array<Record<string, unknown>>
  activeEpisodes?: Array<Record<string, unknown>>
  startEpisode?: Record<string, unknown>
  episodeDetail?: Record<string, unknown>
  takeMedicationContext?: Record<string, unknown>
  intake?: Record<string, unknown>
  startEpisodeStatus?: number
  startEpisodeBody?: string
  takeMedicationContextStatus?: number
  takeMedicationContextBody?: string
  takeMedicationContextAbort?: boolean
  onStartEpisodeRequest?: (body: unknown) => void
  onLogIntakeRequest?: (body: unknown) => void
}

const parseRequestBody = (request: { postDataJSON: () => unknown; postData: () => string | null }) => {
  try {
    return request.postDataJSON()
  } catch {
    return request.postData()
  }
}

export const mockHealthApiRoutes = async (page: Page, options: HealthMockOptions = {}) => {
  const me = options.me ?? healthMocks.me
  const symptoms = options.symptoms ?? healthMocks.symptoms
  const triggers = options.triggers ?? healthMocks.triggers
  const listEpisodes = options.listEpisodes ?? healthMocks.episodes
  const activeEpisodes = options.activeEpisodes ?? healthMocks.episodes
  const startEpisode = options.startEpisode ?? healthMocks.startEpisode
  const episodeDetail = options.episodeDetail ?? healthMocks.episodeDetail
  const takeMedicationContext =
    options.takeMedicationContext ?? healthMocks.takeMedicationContext
  const intake = options.intake ?? healthMocks.intake

  await page.route('**/api/me', (route) => route.fulfill({ json: me }))
  await page.route('**/api/symptoms', (route) => route.fulfill({ json: symptoms }))
  await page.route('**/api/triggers', (route) => route.fulfill({ json: triggers }))
  await page.route('**/api/episodes/active', (route) =>
    route.fulfill({ json: activeEpisodes }),
  )
  await page.route('**/api/episodes/*/take-medication-context', (route) => {
    if (options.takeMedicationContextAbort) {
      return route.abort('failed')
    }
    if (typeof options.takeMedicationContextStatus === 'number') {
      return route.fulfill({
        status: options.takeMedicationContextStatus,
        contentType: 'text/plain',
        body: options.takeMedicationContextBody ?? 'take-medication error',
      })
    }
    return route.fulfill({ json: takeMedicationContext })
  })
  await page.route('**/api/episodes/*', (route) => {
    const pathname = new URL(route.request().url()).pathname
    const parts = pathname.split('/').filter(Boolean)
    if (parts.length !== 3 || parts[2] === 'active') {
      return route.fallback()
    }
    if (route.request().method() !== 'GET') {
      return route.fallback()
    }
    return route.fulfill({ json: episodeDetail })
  })
  await page.route('**/api/episodes**', (route) => {
    const pathname = new URL(route.request().url()).pathname
    if (pathname !== '/api/episodes') {
      return route.fallback()
    }
    const method = route.request().method()
    if (method === 'POST') {
      const body = parseRequestBody(route.request())
      options.onStartEpisodeRequest?.(body)
      if (typeof options.startEpisodeStatus === 'number') {
        return route.fulfill({
          status: options.startEpisodeStatus,
          contentType: 'text/plain',
          body: options.startEpisodeBody ?? 'start episode error',
        })
      }
      return route.fulfill({ json: startEpisode })
    }
    return route.fulfill({ json: listEpisodes })
  })
  await page.route('**/api/intakes', (route) => {
    const body = parseRequestBody(route.request())
    options.onLogIntakeRequest?.(body)
    return route.fulfill({ json: intake })
  })
}
