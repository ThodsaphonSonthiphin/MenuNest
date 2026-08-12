import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

const MANY = Array.from({length: 25}, (_, i) => ({
  id: `${i}`.padStart(8, '0') + '-1111-1111-1111-111111111111',
  name: i % 2 === 0 ? `Tokyo trip ${i}` : `Osaka trip ${i}`,
  destination: 'Japan', startDate: '2026-03-01', dayCount: 3,
  defaultTravelMode: 0, isDaily: false,
}))

async function stub(page, calls) {
  await page.route('**/api/trips**', async (route) => {
    const u = new URL(route.request().url())
    calls.push(u.search)
    const term = u.searchParams.get('search') ?? ''
    const skip = parseInt(u.searchParams.get('skip') ?? '0', 10)
    const take = parseInt(u.searchParams.get('take') ?? '10', 10)
    const dir = u.searchParams.get('sortDirection') ?? ''
    let all = term ? MANY.filter(t => t.name.includes(term)) : [...MANY]
    if (u.searchParams.get('sortColumn') === 'name') {
      all.sort((a, b) => a.name.localeCompare(b.name))
      if (/desc/i.test(dir)) all.reverse()
    }
    await route.fulfill({status: 200, contentType: 'application/json',
      body: JSON.stringify({result: all.slice(skip, skip + take), count: all.length})})
  })
}

test('VERIFY-1: no update-during-render warning on search, page or sort', async ({authedPage: page}) => {
  const calls = [], warnings = []
  await stub(page, calls)
  page.on('console', m => { if (/while rendering|Maximum update depth/.test(m.text())) warnings.push(m.text()) })
  page.on('pageerror', e => warnings.push('PAGEERROR ' + e.message))

  await page.goto('/trips?skip=0&take=10')
  const grid = page.getByRole('grid')
  await expect(grid).toBeVisible({timeout: 20000})

  const box = page.getByRole('textbox', {name: 'Search'})
  await box.click(); await box.fill('Tokyo'); await box.press('Enter')
  await expect(grid.getByRole('row')).toHaveCount(11)

  await page.getByRole('button', {name: /next page/i}).click()
  await expect(grid.getByRole('row')).toHaveCount(4)

  await page.getByRole('columnheader', {name: 'Trip Name'}).click()
  await page.waitForTimeout(2000)

  console.log('[V] url:', page.url())
  console.log('[V] calls:', JSON.stringify(calls))
  console.log('[V] warnings:', warnings.length, JSON.stringify(warnings.slice(0, 2)))
  expect(warnings).toHaveLength(0)
})

test('VERIFY-2: sort state in the URL is reflected on the column header', async ({authedPage: page}) => {
  const calls = []
  await stub(page, calls)
  await page.goto('/trips?skip=0&take=10&sortColumn=name&sortDirection=Descending')
  const grid = page.getByRole('grid')
  await expect(grid).toBeVisible({timeout: 20000})
  await page.waitForTimeout(1500)
  const header = page.getByRole('columnheader', {name: 'Trip Name'})
  console.log('[V2] aria-sort:', await header.getAttribute('aria-sort'))
  console.log('[V2] header class:', await header.getAttribute('class'))
  console.log('[V2] calls:', JSON.stringify(calls))
  const rows = await grid.getByRole('row').allInnerTexts()
  console.log('[V2] first data row:', rows[1])
  expect(await header.getAttribute('aria-sort')).toMatch(/desc/i)
})

test('VERIFY-3: rapid successive searches land on the last term', async ({authedPage: page}) => {
  const calls = []
  await stub(page, calls)
  await page.goto('/trips?skip=0&take=10')
  const grid = page.getByRole('grid')
  await expect(grid).toBeVisible({timeout: 20000})
  const box = page.getByRole('textbox', {name: 'Search'})
  await box.click()
  await box.fill('Tokyo'); await box.press('Enter')
  await box.fill('Osaka'); await box.press('Enter')
  await page.waitForTimeout(2500)
  console.log('[V3] url:', page.url())
  console.log('[V3] calls:', JSON.stringify(calls))
  expect(page.url()).toContain('search=Osaka')
  const rows = await grid.getByRole('row').allInnerTexts()
  expect(rows.slice(1).every(r => r.includes('Osaka'))).toBe(true)
})
