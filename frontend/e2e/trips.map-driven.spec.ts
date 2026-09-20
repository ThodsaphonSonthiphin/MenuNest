import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

// Cover for the map-driven trip screen (#154, menunest-234…241).
//
// This repo has NO component/visual test harness — vitest runs in `environment: 'node'`,
// so `tsc`, `npm run build` and the unit suite cannot see a rendering bug (CLAUDE.md;
// learned on #33, #46, #97). Playwright is the one automated gate that renders anything.
//
// TWO deliberate limits:
//   1. CI has no `VITE_GOOGLE_MAPS_BROWSER_KEY`, so `TripMap` renders its fallback there.
//      Nothing here may assert on pins, tiles or `fitBounds` — that is the interactive
//      check the local machine still owes before merge (spec §9.3).
//   2. Everything is stubbed at `page.route`, the way `trips.search.spec.ts` stubs
//      `/api/trips**`, so the spec runs with no backend and no seeded data.

const TRIP_ID = '11111111-1111-1111-1111-111111111111'
const DAY_1 = '22222222-2222-2222-2222-222222222221'
const DAY_2 = '22222222-2222-2222-2222-222222222222'
const PLACE_A = '33333333-3333-3333-3333-333333333331'
const PLACE_B = '33333333-3333-3333-3333-333333333332'
const PLACE_C = '33333333-3333-3333-3333-333333333333'

const TRIP = {
  id: TRIP_ID,
  name: 'พัทยา',
  destination: 'ชลบุรี',
  startDate: '2026-09-20',
  dayCount: 2,
  defaultTravelMode: 'Drive',
  isDaily: false,
}

const PLACES = [
  {
    id: PLACE_A, tripId: TRIP_ID, googlePlaceId: null, name: 'สวนน้ำพัทยาปาร์ค',
    lat: 12.9276, lng: 100.8829, address: null, category: 'See', priceLevel: null,
    photoUrl: null, openingHoursJson: null, feeNote: null, notes: null,
    reviewLinks: [], hasProfile: false, seasonPeriods: [], bestTimeWindows: [],
  },
  {
    id: PLACE_B, tripId: TRIP_ID, googlePlaceId: null, name: 'The View Ferris Wheel Pattaya',
    lat: 12.9412, lng: 100.8901, address: null, category: 'See', priceLevel: null,
    photoUrl: null, openingHoursJson: null, feeNote: null, notes: null,
    reviewLinks: [], hasProfile: false, seasonPeriods: [], bestTimeWindows: [],
  },
  // Scheduled on NO Day — this is the Ghost pin case issue #6 is about.
  {
    id: PLACE_C, tripId: TRIP_ID, googlePlaceId: null, name: 'ตลาดน้ำ 4 ภาค',
    lat: 12.8910, lng: 100.9013, address: null, category: 'Shop', priceLevel: null,
    photoUrl: null, openingHoursJson: null, feeNote: null, notes: null,
    reviewLinks: [], hasProfile: false, seasonPeriods: [], bestTimeWindows: [],
  },
]

function stop(id: string, tripPlaceId: string, sequence: number) {
  return {
    id, tripPlaceId, sequence, dwellMinutes: 60, travelModeToReach: 'Drive',
    legToReach: sequence === 1 ? null : {seconds: 1200, meters: 5800, encodedPolyline: null, source: 'Estimated'},
    isVisited: false, checklist: [],
  }
}

const ITINERARY = [
  {
    id: DAY_1, date: '2026-09-20', dayStartTime: '09:00:00', useCurrentTimeAsStart: false,
    stops: [
      stop('44444444-4444-4444-4444-444444444441', PLACE_A, 1),
      stop('44444444-4444-4444-4444-444444444442', PLACE_B, 2),
    ],
  },
  {id: DAY_2, date: '2026-09-21', dayStartTime: '09:00:00', useCurrentTimeAsStart: false, stops: []},
]

const ME = {
  userId: '55555555-5555-5555-5555-555555555555',
  email: 'e2e@example.com',
  displayName: 'E2E',
  familyId: null,
  familyName: null,
  familyInviteCode: null,
  authProvider: 'Google',
  homePath: null,
  uvWarnThreshold: null,
  feelsLikeWarnThreshold: null,
  activeTargetRule: null,
}

const json = (body: unknown) => ({status: 200, contentType: 'application/json', body: JSON.stringify(body)})

async function stubTripApi(page: import('@playwright/test').Page) {
  // Geolocation is never granted in these runs, so no Approach leg is drawn — which also
  // keeps the itinerary request's lat/lng out of the URL and the stubs simple.
  await page.route('**/api/me', (route) => route.fulfill(json(ME)))
  await page.route('**/api/trips/weather**', (route) => route.fulfill(json([])))
  await page.route(`**/api/trips/${TRIP_ID}/itinerary**`, (route) => route.fulfill(json(ITINERARY)))
  await page.route(`**/api/trips/${TRIP_ID}/places`, (route) => route.fulfill(json(PLACES)))
  await page.route(`**/api/trips/${TRIP_ID}`, (route) => route.fulfill(json(TRIP)))
  await page.route('**/api/trips?**', (route) => route.fulfill(json({result: [TRIP], count: 1})))
}

test.describe('Trips — the map-driven trip screen', () => {
  test.beforeEach(async ({authedPage: page}) => {
    await stubTripApi(page)
  })

  test('mobile: opens at the summary detent and the handle cycles all three', async ({authedPage: page}) => {
    await page.setViewportSize({width: 390, height: 780})
    await page.goto(`/trips/${TRIP_ID}`)

    const sheet = page.getByTestId('plan-sheet')
    const handle = page.getByTestId('plan-sheet-handle')

    // Every Trip, every Day, every visit opens at `summary` — the detent is working state,
    // not a preference (spec §4.1).
    await expect(sheet).toHaveAttribute('data-detent', 'summary')
    await expect(handle).toHaveAttribute('aria-expanded', 'false')
    await expect(page.getByTestId('plan-summary')).toBeVisible()

    await handle.click()
    await expect(sheet).toHaveAttribute('data-detent', 'half')
    await expect(handle).toHaveAttribute('aria-expanded', 'true')

    await handle.click()
    await expect(sheet).toHaveAttribute('data-detent', 'full')

    // Material 3's rule: the third tap returns to summary rather than dead-ending at full.
    await handle.click()
    await expect(sheet).toHaveAttribute('data-detent', 'summary')
    await expect(handle).toHaveAttribute('aria-expanded', 'false')
  })

  test('mobile: the Stop list and its reorder affordance are reachable from the sheet', async ({authedPage: page}) => {
    await page.setViewportSize({width: 390, height: 780})
    await page.goto(`/trips/${TRIP_ID}`)

    const cards = page.getByTestId('itin-stop-card')
    await expect(cards).toHaveCount(2)

    // The drag handle only renders once reorder mode is on (#34) — the contract
    // `trips.reorder.spec.ts` depends on, asserted here so a rename cannot pass unnoticed.
    await page.getByTestId('plan-sheet-handle').click()
    await page.locator('.reorder-toggle').click()
    await expect(page.getByTestId('stop-drag-handle').first()).toBeVisible()
  })

  test('mobile: the Day chips switch the active Day', async ({authedPage: page}) => {
    await page.setViewportSize({width: 390, height: 780})
    await page.goto(`/trips/${TRIP_ID}`)

    await expect(page.locator('.day-chip.on')).toHaveText('วัน 1')
    await page.locator('.day-chip', {hasText: 'วัน 2'}).click()
    await expect(page.locator('.day-chip.on')).toHaveText('วัน 2')
    // Day 2 has no Stops, so its list is the empty state, not Day 1's cards.
    await expect(page.getByTestId('itin-stop-card')).toHaveCount(0)
  })

  test('mobile: the + control arms the one map instead of opening a second one', async ({authedPage: page}) => {
    await page.setViewportSize({width: 390, height: 780})
    await page.goto(`/trips/${TRIP_ID}`)

    await page.getByRole('button', {name: 'เพิ่มสถานที่'}).click()

    // menunest-240: the armed map IS the map already on screen. `.capture-overlay` was the
    // second, full-screen `TripMap` this redesign deletes — it must never come back.
    await expect(page.locator('.capture-overlay')).toHaveCount(0)
    // Armed, capture owns every tap (ADR-163), so the plan container steps aside rather
    // than fighting the capture surface for the same gestures.
    await expect(page.getByTestId('plan-sheet')).toHaveCount(0)

    // The capture surface ITSELF (`.add-capture-banner`, the search bar, the preview card)
    // lives inside `TripMap`, which renders its no-API-key fallback here — asserting on it
    // needs a real Maps key, so it belongs to the interactive check (spec §9.3), not to CI.

    await page.getByRole('button', {name: 'ออกจากโหมดเพิ่มสถานที่'}).click()
    await expect(page.getByTestId('plan-sheet')).toBeVisible()
  })

  test('desktop: the Plan panel renders and its rail collapses it', async ({authedPage: page}) => {
    await page.setViewportSize({width: 1440, height: 900})
    await page.goto(`/trips/${TRIP_ID}`)

    const panel = page.getByTestId('plan-panel')
    const rail = page.getByTestId('plan-panel-rail')
    await expect(panel).toBeVisible()
    await expect(panel).not.toHaveClass(/collapsed/)
    await expect(rail).toHaveAttribute('aria-expanded', 'true')
    // Desktop gets the panel, never the sheet (menunest-236).
    await expect(page.getByTestId('plan-sheet')).toHaveCount(0)

    await rail.click()
    await expect(panel).toHaveClass(/collapsed/)
    await expect(rail).toHaveAttribute('aria-expanded', 'false')

    await rail.click()
    await expect(panel).not.toHaveClass(/collapsed/)
  })
})
