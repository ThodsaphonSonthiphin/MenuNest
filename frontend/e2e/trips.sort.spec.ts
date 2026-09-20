import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

// Regression cover for the Trips grid's Date column. Three things here are
// invisible to tsc/vitest and were all broken at once:
//   - Syncfusion's UrlAdaptor lower-cases the sort direction, but the Grid's
//     header only draws its indicator for 'Ascending'/'Descending'. With no
//     indicator, `initiateSort` re-derives 'Ascending' on every click, the URL
//     never changes, the deferred data request is never resolved, and the grid
//     hangs behind its spinner overlay — unclickable from then on.
//   - the column's `format="yMd"` rendered 2026-01-10 as `1/10/2026` — US
//     month-first, which a Thai reader reads as 1 October — and the Grid's date
//     parsing shifts the day west of Greenwich (see the timezone test below).
//   - Sorting is server-side, so the assertions have to follow the URL through
//     to the request the grid actually issues.
const TRIPS = [
  {name: 'Trip A', startDate: '2026-01-10'},
  {name: 'Trip B', startDate: '2026-02-11'},
  {name: 'Trip C', startDate: '2026-03-12'},
].map((t, i) => ({
  id: `${i}`.padStart(8, '0') + '-1111-1111-1111-111111111111',
  destination: 'Japan',
  dayCount: 3,
  defaultTravelMode: 0,
  isDaily: false,
  ...t,
}))

async function stubTripsApi(page: import('@playwright/test').Page, calls: string[]) {
  await page.route('**/api/trips**', async (route) => {
    const u = new URL(route.request().url())
    calls.push(u.search)
    const result = [...TRIPS]
    if (u.searchParams.get('sortColumn') === 'startDate') {
      result.sort((a, b) => a.startDate.localeCompare(b.startDate))
      // Stricter than the real server, which compares case-insensitively. The
      // point is the Grid, not the server: matched case-sensitively, a regression
      // to the adaptor's lower-cased direction shows up as unreversed rows here
      // instead of passing quietly.
      if (u.searchParams.get('sortDirection') === 'Descending') result.reverse()
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({result, count: result.length}),
    })
  })
}

test.describe('Trips — Date column', () => {
  test('renders each start date as dd/MM/yyyy', async ({authedPage: page}) => {
    await stubTripsApi(page, [])

    await page.goto('/trips?skip=0&take=10')
    const grid = page.getByRole('grid')
    await expect(grid).toBeVisible({timeout: 20000})

    const cells = await grid.getByRole('row').nth(1).getByRole('gridcell').allInnerTexts()
    expect(cells).toContain('10/01/2026')
    // The unformatted ISO string must not leak through.
    expect(cells.join(' ')).not.toContain('2026-01-10')
  })

  test('the Date header toggles ascending then descending', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips?skip=0&take=10')
    const grid = page.getByRole('grid')
    await expect(grid).toBeVisible({timeout: 20000})
    const header = page.getByRole('columnheader', {name: 'Date'})

    await header.click()
    await expect(page).toHaveURL(/sortColumn=startDate&sortDirection=Ascending/)
    // Without the indicator the grid can never derive 'Descending' on the next click.
    await expect(header).toHaveAttribute('aria-sort', 'ascending')

    await header.click()
    await expect(page).toHaveURL(/sortColumn=startDate&sortDirection=Descending/)
    await expect(header).toHaveAttribute('aria-sort', 'descending')

    const dates = (await grid.getByRole('row').allInnerTexts()).slice(1)
    expect(dates[0]).toContain('12/03/2026')
    expect(dates[2]).toContain('10/01/2026')
    expect(calls[calls.length - 1]).toContain('sortDirection=Descending')
  })

  test('the grid stays interactive after sorting by Date', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips?skip=0&take=10')
    await expect(page.getByRole('grid')).toBeVisible({timeout: 20000})

    await page.getByRole('columnheader', {name: 'Date'}).click()
    await expect(page).toHaveURL(/sortDirection=Ascending/)

    // A stuck spinner overlay swallows every later click, so a second header
    // still responding is the assertion that the grid did not deadlock.
    await page.getByRole('columnheader', {name: 'Trip Name'}).click({timeout: 10000})
    await expect(page).toHaveURL(/sortColumn=name/)
  })

  // The obvious fix for the format is `format="dd/MM/yyyy" type="date"` on the
  // Column, and it looks right on a UTC machine. It is wrong: the Grid parses the
  // `yyyy-MM-dd` string as UTC midnight and renders it in the viewer's timezone, so
  // west of Greenwich every trip shows the day before it starts. Measured under
  // America/Los_Angeles: a 2026-01-10 trip rendered 09/01/2026. This is the guard
  // against someone simplifying the valueAccessor back into a `format` attribute.
  test.describe('west of Greenwich', () => {
    test.use({timezoneId: 'America/Los_Angeles'})

    test('the day does not shift with the viewer timezone', async ({authedPage: page}) => {
      await stubTripsApi(page, [])

      await page.goto('/trips?skip=0&take=10')
      await expect(page.getByRole('grid')).toBeVisible({timeout: 20000})

      const cells = await page.getByRole('grid').getByRole('row').nth(1).getByRole('gridcell').allInnerTexts()
      expect(cells).toContain('10/01/2026')
      expect(cells).not.toContain('09/01/2026')
    })
  })

  test('the unsorted list asks the server for its own default order', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips')
    await expect(page.getByRole('grid')).toBeVisible({timeout: 20000})

    // No sortColumn — the server answers most-recently-modified first.
    expect(calls[0]).not.toContain('sortColumn')
  })
})
