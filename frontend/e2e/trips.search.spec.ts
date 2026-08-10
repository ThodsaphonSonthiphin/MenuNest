import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

// Regression cover for #90. The Trips grid drives paging/sorting/search through
// the URL, and Syncfusion reports the search term in a shape that is easy to
// misread (`action.requestType: 'Searching'` + `action.value`, or a predicate
// array whose term is on `key`). Reading it wrongly leaves the URL untouched,
// so nothing refetches and the grid hangs under its loading spinner.
const TRIPS = [
  {id: '11111111-1111-1111-1111-111111111111', name: 'Tokyo Spring', destination: 'Japan', startDate: '2026-03-01', dayCount: 5, defaultTravelMode: 0, isDaily: false},
  {id: '22222222-2222-2222-2222-222222222222', name: 'Osaka Food', destination: 'Japan', startDate: '2026-04-01', dayCount: 3, defaultTravelMode: 0, isDaily: false},
  {id: '33333333-3333-3333-3333-333333333333', name: 'Chiang Mai', destination: 'Thailand', startDate: '2026-05-01', dayCount: 4, defaultTravelMode: 0, isDaily: false},
]

// Stubs GET /api/trips and applies `search` server-side, the way the real
// backend does, so the test asserts the whole URL -> query -> render round trip.
async function stubTripsApi(page: import('@playwright/test').Page, calls: string[]) {
  await page.route('**/api/trips**', async (route) => {
    const url = route.request().url()
    calls.push(url)
    const term = new URL(url).searchParams.get('search') ?? ''
    const result = term
      ? TRIPS.filter(t => t.name.includes(term) || (t.destination ?? '').includes(term))
      : TRIPS
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({result, count: result.length}),
    })
  })
}

test.describe('Trips — grid search', () => {
  test('searching refetches from the server and clearing restores the full list', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips?skip=0&take=10')
    const grid = page.getByRole('grid')
    await expect(grid).toBeVisible({timeout: 20000})
    await expect(grid.getByRole('row')).toHaveCount(4) // header + 3 trips

    const box = page.getByRole('textbox', {name: 'Search'})
    await box.click()
    await box.fill('Tokyo')
    await box.press('Enter')

    await expect(grid.getByRole('row')).toHaveCount(2) // header + Tokyo Spring
    expect(page.url()).toContain('search=Tokyo')
    expect(calls.some(u => u.includes('search=Tokyo'))).toBe(true)

    await box.fill('')
    await box.press('Enter')

    await expect(grid.getByRole('row')).toHaveCount(4)
    expect(page.url()).not.toContain('search=')
  })

  test('a search term in the URL filters the list and seeds the search box', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips?skip=0&take=10&search=Tokyo')
    const grid = page.getByRole('grid')
    await expect(grid).toBeVisible({timeout: 20000})

    await expect(grid.getByRole('row')).toHaveCount(2)
    expect(calls[0]).toContain('search=Tokyo')
    // Without seeding, the box renders empty and the filter cannot be cleared.
    await expect(page.getByRole('textbox', {name: 'Search'})).toHaveValue('Tokyo')
  })

  // Paging/sorting events carry no `action`, so the term is recovered from the
  // predicate array instead — the one branch the tests above never reach.
  test('paging keeps the active search term', async ({authedPage: page}) => {
    const many = Array.from({length: 25}, (_, i) => ({
      id: `${i}`.padStart(8, '0') + '-1111-1111-1111-111111111111',
      name: i % 2 === 0 ? `Tokyo trip ${i}` : `Osaka trip ${i}`,
      destination: 'Japan', startDate: '2026-03-01', dayCount: 3,
      defaultTravelMode: 0, isDaily: false,
    }))
    const calls: string[] = []
    await page.route('**/api/trips**', async (route) => {
      const u = new URL(route.request().url())
      calls.push(u.href)
      const term = u.searchParams.get('search') ?? ''
      const skip = parseInt(u.searchParams.get('skip') ?? '0', 10)
      const take = parseInt(u.searchParams.get('take') ?? '10', 10)
      const all = term ? many.filter(t => t.name.includes(term)) : many
      await route.fulfill({status: 200, contentType: 'application/json',
        body: JSON.stringify({result: all.slice(skip, skip + take), count: all.length})})
    })

    await page.goto('/trips?skip=0&take=10')
    const grid = page.getByRole('grid')
    await expect(grid).toBeVisible({timeout: 20000})

    const box = page.getByRole('textbox', {name: 'Search'})
    await box.click()
    await box.fill('Tokyo')
    await box.press('Enter')
    await expect(grid.getByRole('row')).toHaveCount(11) // header + 10 of 13

    await page.getByRole('button', {name: /next page/i}).click()
    await expect(grid.getByRole('row')).toHaveCount(4) // header + the last 3
    expect(page.url()).toContain('search=Tokyo')
    expect(calls[calls.length - 1]).toContain('search=Tokyo')
  })

  test('the grid settles instead of looping onDataRequest', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips?skip=0&take=10')
    await expect(page.getByRole('grid')).toBeVisible({timeout: 20000})

    const box = page.getByRole('textbox', {name: 'Search'})
    await box.click()
    await box.fill('Tokyo')
    await box.press('Enter')
    await expect(page.getByRole('grid').getByRole('row')).toHaveCount(2)

    // An update-during-render loop would keep issuing requests forever; a settled
    // grid stops. Two loads (initial + search) is the whole budget.
    const settled = calls.length
    await page.waitForTimeout(2000)
    expect(calls.length).toBe(settled)
    expect(settled).toBeLessThanOrEqual(3)
  })
})
