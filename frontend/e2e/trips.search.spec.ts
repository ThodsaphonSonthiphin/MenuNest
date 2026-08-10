import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

// Regression cover for #90. The Trips grid drives paging/sorting/search through
// the URL. Two things here are easy to get wrong and invisible to tsc/vitest:
//   - Syncfusion reports the search term as `action.value` on a 'Searching'
//     action, or on `key` of a predicate array — never on `search[0].value`.
//   - onDataRequest is raised from inside the Grid's render, so navigating
//     synchronously loops it until the grid freezes under its spinner.
const TRIPS = Array.from({length: 25}, (_, i) => ({
  id: `${i}`.padStart(8, '0') + '-1111-1111-1111-111111111111',
  name: i % 2 === 0 ? `Tokyo trip ${i}` : `Osaka trip ${i}`,
  destination: 'Japan',
  startDate: '2026-03-01',
  dayCount: 3,
  defaultTravelMode: 0,
  isDaily: false,
}))
const TOKYO_COUNT = TRIPS.filter(t => t.name.includes('Tokyo')).length // 13

// Applies search/sort/paging server-side the way the real endpoint does, so the
// assertions cover the whole URL -> query -> render round trip.
async function stubTripsApi(page: import('@playwright/test').Page, calls: string[]) {
  await page.route('**/api/trips**', async (route) => {
    const u = new URL(route.request().url())
    calls.push(u.search)
    const term = u.searchParams.get('search') ?? ''
    const skip = parseInt(u.searchParams.get('skip') ?? '0', 10)
    const take = parseInt(u.searchParams.get('take') ?? '10', 10)
    const result = term ? TRIPS.filter(t => t.name.includes(term)) : [...TRIPS]
    if (u.searchParams.get('sortColumn') === 'name') {
      result.sort((a, b) => a.name.localeCompare(b.name))
      if (/desc/i.test(u.searchParams.get('sortDirection') ?? '')) result.reverse()
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({result: result.slice(skip, skip + take), count: result.length}),
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
    await expect(grid.getByRole('row')).toHaveCount(11) // header + a full page

    const box = page.getByRole('textbox', {name: 'Search'})
    await box.click()
    await box.fill('Tokyo')
    await box.press('Enter')

    await expect(grid.getByRole('row')).toHaveCount(11)
    expect(page.url()).toContain('search=Tokyo')
    expect(calls.some(c => c.includes('search=Tokyo'))).toBe(true)
    const names = (await grid.getByRole('row').allInnerTexts()).slice(1)
    expect(names.every(n => n.includes('Tokyo'))).toBe(true)

    await box.fill('')
    await box.press('Enter')

    await expect(grid.getByRole('row')).toHaveCount(11)
    expect(page.url()).not.toContain('search=')
    const cleared = (await grid.getByRole('row').allInnerTexts()).slice(1)
    expect(cleared.some(n => n.includes('Osaka'))).toBe(true)
  })

  test('a search term in the URL filters the list and seeds the search box', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips?skip=0&take=10&search=Tokyo')
    await expect(page.getByRole('grid')).toBeVisible({timeout: 20000})

    expect(calls[0]).toContain('search=Tokyo')
    // Without seeding, the box renders empty and the filter cannot be cleared.
    await expect(page.getByRole('textbox', {name: 'Search'})).toHaveValue('Tokyo')
  })

  test('a sort in the URL is reflected on the column header', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips?skip=0&take=10&sortColumn=name&sortDirection=Descending')
    await expect(page.getByRole('grid')).toBeVisible({timeout: 20000})

    // The list arrives sorted, so the header must show which column did it.
    await expect(page.getByRole('columnheader', {name: 'Trip Name'}))
      .toHaveAttribute('aria-sort', /desc/i)
  })

  // Paging and sorting events carry no `action`, so the term is recovered from
  // the predicate array instead — the branch the tests above never reach.
  test('paging and sorting keep the active search term', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips?skip=0&take=10')
    const grid = page.getByRole('grid')
    await expect(grid).toBeVisible({timeout: 20000})

    const box = page.getByRole('textbox', {name: 'Search'})
    await box.click()
    await box.fill('Tokyo')
    await box.press('Enter')
    await expect(grid.getByRole('row')).toHaveCount(11)

    await page.getByRole('button', {name: /next page/i}).click()
    await expect(grid.getByRole('row')).toHaveCount(TOKYO_COUNT - 10 + 1)
    expect(page.url()).toContain('search=Tokyo')

    await page.getByRole('columnheader', {name: 'Trip Name'}).click()
    await expect(page).toHaveURL(/sortColumn=name/)
    expect(page.url()).toContain('search=Tokyo')
    expect(calls[calls.length - 1]).toContain('search=Tokyo')
  })

  test('the grid settles without looping or updating state during render', async ({authedPage: page}) => {
    const calls: string[] = []
    const warnings: string[] = []
    await stubTripsApi(page, calls)
    page.on('console', m => {
      if (/while rendering|Maximum update depth/.test(m.text())) warnings.push(m.text())
    })
    page.on('pageerror', e => warnings.push(`pageerror: ${e.message}`))

    await page.goto('/trips?skip=0&take=10')
    const grid = page.getByRole('grid')
    await expect(grid).toBeVisible({timeout: 20000})

    const box = page.getByRole('textbox', {name: 'Search'})
    await box.click()
    await box.fill('Tokyo')
    await box.press('Enter')
    await expect(grid.getByRole('row')).toHaveCount(11)

    // Re-submitting the same term is a no-op navigation; the grid must stay live.
    await box.press('Enter')
    await page.waitForTimeout(1500)
    await box.click({timeout: 5000})

    // An update-during-render loop would issue requests forever; a settled grid stops.
    const settled = calls.length
    await page.waitForTimeout(1500)
    expect(calls.length).toBe(settled)
    expect(settled).toBeLessThanOrEqual(3)
    expect(warnings).toHaveLength(0)
  })

  test('rapid successive searches land on the last term', async ({authedPage: page}) => {
    const calls: string[] = []
    await stubTripsApi(page, calls)

    await page.goto('/trips?skip=0&take=10')
    const grid = page.getByRole('grid')
    await expect(grid).toBeVisible({timeout: 20000})

    const box = page.getByRole('textbox', {name: 'Search'})
    await box.click()
    await box.fill('Tokyo')
    await box.press('Enter')
    await box.fill('Osaka')
    await box.press('Enter')

    await expect(page).toHaveURL(/search=Osaka/)
    const names = (await grid.getByRole('row').allInnerTexts()).slice(1)
    expect(names.every(n => n.includes('Osaka'))).toBe(true)
  })
})
