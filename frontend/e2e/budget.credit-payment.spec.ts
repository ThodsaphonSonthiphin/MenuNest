import {expect, type Locator} from '@playwright/test'
import {test} from './fixtures/healthFixture'
import {
  budgetSummaryFixture,
  CREDIT_ENVELOPE_ID,
  CREDIT_ACCOUNT_ID,
  LOAN_ACCOUNT_ID,
} from './helpers/mockRoutes/budgetRoutes'

/**
 * The progress-bar fill's width is an unrounded float (`paymentProgress` in
 * lib/paymentLabel.ts divides two currency amounts directly — see
 * EnvelopeCard.tsx's `pct`), so pinning an exact string would be fragile.
 * Reading the percentage out of the inline `style` and comparing as a number
 * is stable against that rounding while still catching a bar left green/full
 * on an underfunded card, or orange/empty on a funded one — the mock draws
 * both, and nothing else in this file reads pixel geometry.
 */
async function progressWidthPct(fill: Locator): Promise<number> {
  const style = await fill.getAttribute('style')
  const match = style?.match(/width:\s*([\d.]+)%/)
  if (!match) throw new Error(`Could not read a width percentage from style="${style}"`)
  return Number(match[1])
}

/**
 * Issue #112 — the Payment envelope card (menunest-202/204/205) and the Loan
 * pay path (menunest-207/212/214). Per CLAUDE.md this is the only automatic
 * gate that can catch a rendering bug, and it only covers what a spec
 * exercises — #97 shipped an entirely unstyled page because no spec ever
 * touched it. So every assertion below reads the TEXT a user reads (จ่ายเต็มได้,
 * ขาดอีก ฿20,000.00, รูดบัตร, ✎ Edit) or a concrete DOM fact (an icon button's
 * absence, a disabled+struck-through class) — never just "the container is
 * visible", which would stay green through a badly-broken render.
 *
 * Fixture shapes: `budgetRoutes.ts` puts the Credit account "KBank" funded
 * (shortfall 0) in the `บัตรเครดิต` group by default, and a Loan account
 * "ผ่อนรถ" (no Payment envelope — menunest-206) alongside it. `underfunded()`
 * below overrides both the envelope's and the account's `shortfall`/`balance`
 * to match the mock's second panel (−฿20,500 / ขาดอีก ฿20,000.00).
 */

const underfunded = () => ({
  ...budgetSummaryFixture,
  groups: budgetSummaryFixture.groups.map((g) =>
    g.groupId !== 'grp-credit'
      ? g
      : {
          ...g,
          categories: g.categories.map((c) =>
            c.categoryId !== CREDIT_ENVELOPE_ID ? c : {...c, shortfall: 20000},
          ),
        },
  ),
  accounts: budgetSummaryFixture.accounts.map((a) =>
    a.id !== CREDIT_ACCOUNT_ID ? a : {...a, balance: -20500, shortfall: 20000},
  ),
})

test.describe('Budget — the payment envelope and its shortfall line (#112)', () => {
  test.beforeEach(async ({mockApi}) => {
    await mockApi.budget.apply()
  })

  test('the บัตรเครดิต group renders in its own group, separate from ordinary categories', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget')
    const header = page.locator('.bdg-env-group-header', {hasText: 'บัตรเครดิต'})
    await expect(header).toBeVisible()
    // menunest-202: the group carries the totals for JUST the Payment
    // envelope, not folded into another group's numbers.
    await expect(header).toContainText('฿500.00')
  })

  test('the payment envelope shows จ่ายเต็มได้ when funded, with no everyday dot and no ＋ button', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget')
    const card = page.getByTestId('bdg-envelope-card').filter({hasText: 'จ่ายบัตร KBank'})
    await expect(card).toBeVisible()
    await expect(card).toContainText('ยอดบัตร −฿500.00')
    await expect(card).toContainText('จ่ายเต็มได้')

    // menunest-205: a Payment envelope can never be Everyday — the dot is not
    // merely unset, it must never render at all.
    await expect(card.getByTestId('bdg-env-everyday-dot')).toHaveCount(0)
    // menunest-204: no plain-transaction ＋ on a Payment envelope — the
    // payment action replaces it entirely.
    await expect(card.getByTestId('bdg-env-add-icon')).toHaveCount(0)
    // ⇄ Move stays reachable even collapsed.
    await expect(card.getByTestId('bdg-env-move-icon')).toBeVisible()

    // Symmetric with the underfunded test below: `paymentPillTone` and
    // `shortfallLine` must agree on "funded" the same way they must agree on
    // "short" — a pill/text desync (#46's shape: the logic is right, the
    // colour is wrong) would sail through if only the text were checked here.
    const pill = card.locator('.bdg-env-pill')
    await expect(pill).toHaveClass(/is-green/)
    const shortfallText = card.locator('.bdg-env-row2 b')
    await expect(shortfallText).not.toHaveClass(/short/)

    // The mock draws this card's bar FULL and green — the funded twin of the
    // underfunded test's "nearly-empty orange bar" assertion.
    const fill = card.locator('.bdg-env-progress-fill')
    await expect(fill).toHaveClass(/is-green/)
    expect(await progressWidthPct(fill)).toBeGreaterThanOrEqual(99)
  })

  test('an underfunded card names the gap in red, and the pill turns orange', async ({
    authedPage: page,
    mockApi,
  }) => {
    await mockApi.budget.summary(underfunded()).apply()
    await page.goto('/budget')
    const card = page.getByTestId('bdg-envelope-card').filter({hasText: 'จ่ายบัตร KBank'})
    await expect(card).toBeVisible()
    await expect(card).toContainText('ยอดบัตร −฿20,500.00')
    await expect(card).toContainText('ขาดอีก ฿20,000.00')

    // The pill still reads the envelope's OWN available (฿500, unchanged) —
    // it is the shortfall that turns it orange, per lib/paymentLabel's
    // `paymentPillTone`.
    const pill = card.locator('.bdg-env-pill')
    await expect(pill).toHaveText('฿500.00')
    await expect(pill).toHaveClass(/is-orange/)

    const shortfallText = card.locator('.bdg-env-row2 b')
    await expect(shortfallText).toHaveClass(/short/)

    // The mock draws this exact state's bar nearly-empty and orange (2.4%) —
    // unguarded by anything but a now-deleted throwaway screenshot script.
    // `paymentProgress`'s pct is an unrounded float, so the width is read as
    // a number rather than pinned to a string (see `progressWidthPct`).
    const fill = card.locator('.bdg-env-progress-fill')
    await expect(fill).toHaveClass(/is-orange/)
    expect(await progressWidthPct(fill)).toBeLessThan(10)
  })

  test('expanding the card shows รูดบัตร / คงเหลือ and a struck-through disabled ✎ Edit', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget')
    const card = page.getByTestId('bdg-envelope-card').filter({hasText: 'จ่ายบัตร KBank'})
    await card.click()
    await expect(card).toHaveClass(/is-expanded/)

    // R-1: รูดบัตร/คงเหลือ replace the ordinary Activity/Available meta row on
    // a Payment envelope — `assigned + activity` alone does not explain a
    // categorised card purchase.
    await expect(card).toContainText('รูดบัตร')
    await expect(card).toContainText('คงเหลือ')

    // menunest-205: name/group/delete/hide are all refused — the Edit action
    // renders struck-through and disabled rather than merely absent.
    const editBtn = card.getByRole('button', {name: '✎ Edit'})
    await expect(editBtn).toBeVisible()
    await expect(editBtn).toBeDisabled()
    await expect(editBtn).toHaveClass(/is-off/)
  })

  test('the ฿ จ่ายบัตร action opens the payment sheet, with no funding-envelope picker (Credit)', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget')
    const card = page.getByTestId('bdg-envelope-card').filter({hasText: 'จ่ายบัตร KBank'})
    await card.click()
    await expect(card).toHaveClass(/is-expanded/)

    await card.getByTestId('bdg-env-pay').click()
    const dialog = page.getByTestId('bdg-payment-dialog')
    await expect(dialog).toBeVisible()
    await expect(dialog.locator('h3')).toHaveText('จ่ายบัตร')
    await expect(dialog).toContainText('KBank')

    // menunest-214: the funding-envelope picker is a LOAN-only field — a
    // categorised outflow leg on a Credit payment would double-spend one
    // payment across two envelopes.
    await expect(dialog.getByText('ซองที่ใช้จ่ายค่างวด')).toHaveCount(0)
  })

  test('a Loan account offers จ่ายค่างวด from its ⋯ menu, and that dialog carries the funding-envelope picker', async ({
    authedPage: page,
  }) => {
    // menunest-207: a late review found the SPA had no way to pay a loan at
    // all — this is the only create-path entry point for it (a Loan gets no
    // Payment envelope, so there is no card to carry a ฿ action).
    await page.goto('/budget')
    const loanCard = page.getByTestId('bdg-account-card').filter({hasText: 'ผ่อนรถ'})
    await expect(loanCard).toBeVisible()
    await loanCard.click()
    await expect(page).toHaveURL(new RegExp(`/budget/accounts/${LOAN_ACCOUNT_ID}$`))
    await expect(page.getByTestId('bdg-account-page')).toBeVisible()

    await page.getByTestId('bdg-account-menu').click()
    const payItem = page.getByTestId('bdg-menu-pay')
    await expect(payItem).toBeVisible()
    await expect(payItem).toContainText('จ่ายค่างวด')
    await payItem.click()

    const dialog = page.getByTestId('bdg-payment-dialog')
    await expect(dialog).toBeVisible()
    await expect(dialog.locator('h3')).toHaveText('จ่ายค่างวด')

    // menunest-214: REQUIRED on a Loan — the funding Envelope is the only
    // thing a loan payment ever spends.
    await expect(dialog.getByText('ซองที่ใช้จ่ายค่างวด')).toBeVisible()
    // The Payment envelope itself must never be offered as the funding
    // source (it is derived, not spendable) — only ordinary envelopes like
    // ค่ากิน/ค่าไฟ show up in the picker's dropdown data, which we can't
    // click into without opening the Syncfusion popup, so this is asserted
    // at the unit level (lib/paymentOptions.test.ts) rather than here.
  })

  test('a Credit account also offers จ่ายบัตร from its ⋯ menu', async ({authedPage: page}) => {
    await page.goto('/budget')
    const creditCard = page.getByTestId('bdg-account-card').filter({hasText: 'KBank'})
    await creditCard.click()
    await expect(page).toHaveURL(new RegExp(`/budget/accounts/${CREDIT_ACCOUNT_ID}$`))

    await page.getByTestId('bdg-account-menu').click()
    const payItem = page.getByTestId('bdg-menu-pay')
    await expect(payItem).toBeVisible()
    await expect(payItem).toContainText('จ่ายบัตร')
  })
})
