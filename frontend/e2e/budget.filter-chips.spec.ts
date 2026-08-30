import {expect, type Locator, type Page} from '@playwright/test'
import {test} from './fixtures/healthFixture'
import {budgetSummaryFixture} from './helpers/mockRoutes/budgetRoutes'

/**
 * Regression cover for issue #110 — "ไม่แสดงสถานะว่าเลือกแท็บอะไรอยู่".
 *
 * Per CLAUDE.md, Playwright is the only automatic gate that can catch a
 * rendering bug, and the /budget filter row had no spec at all — which is why
 * #110 reached prod straight through tsc, vitest and `npm run build`. Both
 * halves of that bug were INVISIBLE TO CLASS NAMES: `is-active` was applied
 * correctly the whole time, only its paint was wrong. So everything below
 * reads COMPUTED COLOUR and never `toHaveClass` — a class assertion would
 * have stayed green through the entire bug.
 *
 * The two failures being pinned:
 *   1. the selected chip painted #ffffff on the #f8fafc page — 1.05:1, i.e.
 *      no perceptible selection at all;
 *   2. `.bdg-chip.is-danger` sat after `.bdg-chip.is-active` at equal
 *      specificity and won on source order, so selecting "⚠ Overspent"
 *      changed nothing on screen.
 */

/**
 * WCAG contrast, over the `rgb()` / `rgba()` strings getComputedStyle returns.
 *
 * Alpha has to be composited before comparing, not dropped: the resting danger
 * chip is `rgba(220, 38, 38, 0.1)`, a pale wash that reads as almost the page
 * colour, yet its bare RGB triplet is a vivid red. Treating that triplet as
 * opaque makes a plainly visible change measure as a 1.34 non-difference.
 */
const rgba = (colour: string): [number, number, number, number] => {
  const parts = colour.match(/\d+(\.\d+)?/g)
  if (!parts || parts.length < 3) throw new Error(`Unparseable colour: ${colour}`)
  return [Number(parts[0]), Number(parts[1]), Number(parts[2]), parts[3] ? Number(parts[3]) : 1]
}
/** Composite a possibly-translucent colour over an opaque backdrop. */
const over = (colour: string, backdrop: string): [number, number, number] => {
  const [r, g, b, a] = rgba(colour)
  const [br, bg, bb] = rgba(backdrop)
  return [r * a + br * (1 - a), g * a + bg * (1 - a), b * a + bb * (1 - a)]
}
const luminance = (channels: [number, number, number]) => {
  const weights = [0.2126, 0.7152, 0.0722]
  return channels
    .map((v) => v / 255)
    .map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4))
    .reduce((acc, v, i) => acc + weights[i] * v, 0)
}
/** Contrast of `colour` against `backdrop`, compositing `colour` onto it first. */
const contrastOver = (colour: string, backdrop: string) => {
  const [hi, lo] = [luminance(over(colour, backdrop)), luminance(over(backdrop, backdrop))].sort(
    (x, y) => y - x,
  )
  return (hi + 0.05) / (lo + 0.05)
}

/** Everything that distinguishes one chip's paint from another's. */
const paintOf = (chip: Locator) =>
  chip.evaluate((el) => {
    const s = getComputedStyle(el)
    return {bg: s.backgroundColor, fg: s.color, border: s.borderColor, weight: s.fontWeight}
  })

const chip = (page: Page, name: RegExp) =>
  page.getByTestId('bdg-filters').getByRole('button', {name})

test.describe('Budget — filter chips show which one is selected (#110)', () => {
  test.beforeEach(async ({mockApi}) => {
    // Drive one category negative so `overspentCount > 0` and the danger chip
    // actually renders its `.is-danger` variant — under the stock fixture every
    // category is in the black and that branch never runs.
    const [first, ...rest] = budgetSummaryFixture.groups
    const overspent = {
      ...budgetSummaryFixture,
      groups: [
        {
          ...first,
          categories: first.categories.map((c, i) => (i === 0 ? {...c, available: -250} : c)),
        },
        ...rest,
      ],
    }
    await mockApi.budget.summary(overspent).apply()
  })

  test('the selected chip is perceptibly distinct from the page behind it', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget')
    const all = chip(page, /^All$/)
    await expect(all).toBeVisible()

    const pageBg = await page
      .getByTestId('bdg-page')
      .evaluate((el) => getComputedStyle(el).backgroundColor)
    const {bg} = await paintOf(all)

    // 3:1 is the WCAG non-text contrast floor. The bug shipped at 1.05:1.
    expect(
      contrastOver(bg, pageBg),
      `selected chip ${bg} is indistinguishable from the page ${pageBg}`,
    ).toBeGreaterThanOrEqual(3)
  })

  test('selecting a chip repaints it and un-paints the previous one', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget')
    const all = chip(page, /^All$/)
    const snoozed = chip(page, /^Snoozed$/)
    await expect(all).toBeVisible()

    const selectedPaint = await paintOf(all)
    const restingPaint = await paintOf(snoozed)
    expect(selectedPaint).not.toEqual(restingPaint)

    await snoozed.click()

    expect(await paintOf(snoozed)).toEqual(selectedPaint)
    expect(await paintOf(all)).toEqual(restingPaint)
  })

  test('the danger chip changes appearance when it becomes the selected one', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget')
    const overspent = chip(page, /Overspent/)
    await expect(overspent).toBeVisible()

    const pageBg = await page
      .getByTestId('bdg-page')
      .evaluate((el) => getComputedStyle(el).backgroundColor)

    const resting = await paintOf(overspent)
    await overspent.click()
    const selected = await paintOf(overspent)

    // The exact regression: `.is-danger` used to out-rank `.is-active` on
    // source order, so these two reads were identical.
    expect(selected).not.toEqual(resting)
    // …and the selected danger chip has to clear the same bar as any other
    // selected chip, not merely differ by a shade.
    expect(
      contrastOver(selected.bg, pageBg),
      `selected danger chip ${selected.bg} is indistinguishable from the page ${pageBg}`,
    ).toBeGreaterThanOrEqual(3)
  })

  test('aria-pressed tracks the selection for assistive tech', async ({authedPage: page}) => {
    await page.goto('/budget')
    const all = chip(page, /^All$/)
    const snoozed = chip(page, /^Snoozed$/)
    await expect(all).toBeVisible()

    await expect(all).toHaveAttribute('aria-pressed', 'true')
    await expect(snoozed).toHaveAttribute('aria-pressed', 'false')

    await snoozed.click()

    await expect(all).toHaveAttribute('aria-pressed', 'false')
    await expect(snoozed).toHaveAttribute('aria-pressed', 'true')
  })
})
