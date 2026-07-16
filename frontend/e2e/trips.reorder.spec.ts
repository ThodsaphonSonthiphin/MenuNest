import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'

// Reads the current Stop order from the itinerary as an array of stop ids.
async function orderOf(page: import('@playwright/test').Page): Promise<string[]> {
  return page.getByTestId('itin-stop-card').evaluateAll((els) =>
    els.map((el) => el.getAttribute('data-stop-id') ?? ''),
  )
}

test.describe('Trips — itinerary reorder', () => {
  test('keyboard reorder moves the first Stop down and persists', async ({authedPage: page}) => {
    await page.goto('/trips')
    // Open the first trip if a trip list is shown; otherwise assume a trip route.
    const firstTrip = page.getByTestId('trip-card').first()
    if (await firstTrip.count()) await firstTrip.click()

    const cards = page.getByTestId('itin-stop-card')
    if (await cards.count() < 2) test.skip(true, 'needs a Day with ≥2 Stops (no seeded backend locally)')

    const before = await orderOf(page)

    // Pick up the first Stop's handle, move it down one slot, drop.
    // Enter reorder mode - the drag handle only renders once the mode is on (#34).
    await page.locator('.reorder-toggle').click()

    const handle = page.getByTestId('stop-drag-handle').first()
    await handle.focus()
    await page.keyboard.press('Space')
    await page.keyboard.press('ArrowDown')
    await page.keyboard.press('Space')

    // Wait for the reorder overlay to appear and clear (recompute round-trip).
    await expect(page.locator('.itin-reorder-overlay')).toBeHidden({timeout: 10_000})

    const after = await orderOf(page)
    expect(after[0]).toBe(before[1])
    expect(after[1]).toBe(before[0])

    // Persists across reload.
    await page.reload()
    const reloaded = await orderOf(page)
    expect(reloaded.slice(0, 2)).toEqual(after.slice(0, 2))
  })
})
