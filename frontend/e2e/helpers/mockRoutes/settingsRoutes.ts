import type { Page } from '@playwright/test'
import { recordRequest, type RequestCapture } from './types'

type SettingsConfig = {
  meResponse: unknown
  vapidKeyStatus: number
  vapidKeyBody: unknown
  shareLinksList: unknown
  revokeStatus: number
}

export const createSettingsMocks = (page: Page, capture: RequestCapture) => {
  const config: SettingsConfig = {
    meResponse: { id: 'user-1', displayName: 'Test User', email: 'test@menunest.app' },
    vapidKeyStatus: 200,
    vapidKeyBody: { publicKey: 'BFakeVapidPublicKey1234567890abcdef==' },
    shareLinksList: [],
    revokeStatus: 204,
  }

  const self = {
    me: (data?: unknown) => {
      if (data) config.meResponse = data
      return self
    },
    shareLinks: (data: unknown[]) => {
      config.shareLinksList = data
      return self
    },
    revokeFails: (status: number) => {
      config.revokeStatus = status
      return self
    },
    apply: async () => {
      // Register catchall/general routes FIRST, specific paths LAST (last-wins).
      // `**/api/share-links` matches both /api/share-links and /api/share-links/X with glob.
      // We want DELETE /api/share-links/X to hit the specific handler.
      await page.route('**/api/share-links', async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ json: config.shareLinksList })
      })
      await page.route('**/api/share-links/*', async (route, request) => {
        await recordRequest(route, request, capture)
        if (request.method() === 'DELETE') {
          return route.fulfill({ status: config.revokeStatus, body: '' })
        }
        return route.fallback()
      })
      // Push-subscription routes
      await page.route('**/api/push-subscriptions', async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ status: 200, body: '' })
      })
      await page.route('**/api/push-subscriptions/vapid-public-key', async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ json: config.vapidKeyBody })
      })
      // /api/me (separate path)
      await page.route('**/api/me', async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({ json: config.meResponse })
      })
    },
  }
  return self
}

export type SettingsMocks = ReturnType<typeof createSettingsMocks>
