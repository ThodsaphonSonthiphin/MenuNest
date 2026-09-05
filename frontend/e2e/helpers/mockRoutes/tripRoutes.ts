import type {Page} from '@playwright/test'
import {recordRequest, type RequestCapture} from './types'

/**
 * `/trips` is NOT behind FamilyRequiredRoute (src/router.tsx:55-77), so it
 * needs no familyId to render — but the app shell still reads /api/me, and
 * the grid is empty without /api/trips. Both are stubbed here.
 *
 * TravelMode is 'Drive' | 'Walk' | 'Transit' (api.ts:667). Not 'Driving'.
 */

const meResponse = {
  userId: 'user-1',
  email: 'test@menunest.app',
  displayName: 'ทศพล',
  familyId: 'family-1',
  familyName: 'ครอบครัวทดสอบ',
  familyInviteCode: 'TEST01',
  authProvider: 'Google',
  homePath: null,
  uvWarnThreshold: null,
  feelsLikeWarnThreshold: null,
  activeTargetRule: null,
}

export const tripsFixture = [
  {
    id: 'trip-1', name: 'เชียงใหม่ 3 วัน', destination: 'เชียงใหม่',
    startDate: '2026-10-10', dayCount: 3, defaultTravelMode: 'Drive', isDaily: false,
  },
  {
    id: 'trip-2', name: 'ทะเลหัวหิน', destination: 'หัวหิน',
    startDate: '2026-11-02', dayCount: 2, defaultTravelMode: 'Drive', isDaily: false,
  },
  {
    id: 'trip-3', name: 'เดินเล่นเยาวราช', destination: 'กรุงเทพฯ',
    startDate: '2026-09-20', dayCount: 1, defaultTravelMode: 'Transit', isDaily: true,
  },
]

interface TripConfig {
  me: unknown
  trips: unknown[]
}

export const createTripMocks = (page: Page, capture: RequestCapture) => {
  const config: TripConfig = {me: meResponse, trips: tripsFixture}

  const self = {
    me: (data: unknown) => {
      config.me = data
      return self
    },
    /** Pass [] to render the empty state. */
    trips: (rows: unknown[]) => {
      config.trips = rows
      return self
    },
    apply: async () => {
      await page.route(/\/api\/me(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({json: config.me})
      })
      await page.route(/\/api\/trips(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({json: {result: config.trips, count: config.trips.length}})
      })
    },
  }

  return self
}

export type TripMocks = ReturnType<typeof createTripMocks>
