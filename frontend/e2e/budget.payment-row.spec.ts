import {expect} from '@playwright/test'
import {test} from './fixtures/healthFixture'
import {globalTransactionsFixture, PAYMENT_ID} from './helpers/mockRoutes/budgetRoutes'

/**
 * menunest-209's headline outcome — **a payment renders as ONE row** — verified
 * in a browser for the first time.
 *
 * `PaymentTransactionRow` had no rendering coverage anywhere: vitest runs in
 * `environment: 'node'` with no jsdom, the `budgetRoutes.ts` transactions
 * fixture was `items: []`, and no spec visited `/budget/transactions` at all.
 * That is the #97 failure shape CLAUDE.md names: `tsc`, `npm run build` and the
 * unit suite are all blind to a render, and Playwright is the one automatic
 * gate that is not — but only for pages a spec actually opens.
 *
 * So every assertion below reads the TEXT a user reads (จ่ายบัตร KBank,
 * Payment · เงินสด → KBank, ฿500.00, Edit payment) or a concrete DOM fact (row
 * COUNT, `disabled`), never "the container is visible" — which would stay green
 * through a badly broken render.
 *
 * The fixture is one payment (เงินสด → KBank, ฿500) as its two legs sharing
 * `paymentId`, plus one ordinary transaction.
 */
test.describe('Budget — a payment is one row on the global feed (#112)', () => {
  test.beforeEach(async ({mockApi}) => {
    await mockApi.budget.apply()
  })

  test('the two legs of a payment collapse into a single row naming both accounts', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget/transactions')

    const rows = page.getByTestId('global-tx-row')
    // Three transactions arrived; two of them are one payment. THREE rows here
    // would mean the feed is offering two Edit and two Delete buttons for one
    // payment, every one of which the backend refuses.
    await expect(rows).toHaveCount(2)

    const payment = page.locator('[data-payment-id="' + PAYMENT_ID + '"]')
    await expect(payment).toHaveCount(1)
    // menunest-212's action word follows the account being PAID — KBank is a
    // Credit account, so จ่ายบัตร, never จ่ายค่างวด (and never จ่ายหนี้/ชำระ).
    await expect(payment).toContainText('จ่ายบัตร KBank')
    // Both halves are visible, so the subtitle names the direction rather than
    // apologising for a missing leg.
    await expect(payment).toContainText('Payment · เงินสด → KBank')
    // A complete pair has no single sign — the magnitude is shown, unsigned.
    await expect(payment.locator('.bdg-tx-amount')).toHaveText('฿500.00')

    // The ordinary transaction is still an ordinary row beside it.
    const plain = rows.filter({hasText: 'ข้าวมันไก่'})
    await expect(plain).toHaveCount(1)
    await expect(plain).toContainText('ค่ากิน • เงินสด')
    await expect(plain.locator('.bdg-tx-amount')).toHaveText('−฿120.00')
  })

  test('the payment row offers Edit payment ENABLED, because both legs are here', async ({
    authedPage: page,
  }) => {
    await page.goto('/budget/transactions')

    const payment = page.locator('[data-payment-id="' + PAYMENT_ID + '"]')
    await payment.getByTestId('bdg-tx-menu-btn').click()

    const edit = payment.getByTestId('bdg-tx-menu-edit')
    await expect(edit).toContainText('Edit payment')
    // `complete: true` — this is the ONLY feed where a payment can be edited.
    // The account-detail feed sees one leg and must leave this disabled; if the
    // completeness test ever inverted, this is what would catch it.
    await expect(edit).toBeEnabled()

    const del = payment.getByTestId('bdg-tx-menu-delete')
    await expect(del).toContainText('Delete payment')
    await expect(del).toBeEnabled()
  })

  test('a lone leg is still a payment row, with Edit disabled and its reason said', async ({
    authedPage: page,
    mockApi,
  }) => {
    // Only the outflow leg — the shape an account-detail feed always sees.
    await mockApi.budget
      .transactions(globalTransactionsFixture.filter(t => t.id !== 'tx-pay-to'))
      .apply()
    await page.goto('/budget/transactions')

    const payment = page.locator('[data-payment-id="' + PAYMENT_ID + '"]')
    await expect(payment).toHaveCount(1)
    await expect(payment).toContainText('Payment · other half is on the account being paid')
    // One leg shows what actually hit THIS account, signed.
    await expect(payment.locator('.bdg-tx-amount')).toHaveText('−฿500.00')

    await payment.getByTestId('bdg-tx-menu-btn').click()
    await expect(payment.getByTestId('bdg-tx-menu-edit')).toBeDisabled()
    // Delete needs nothing but the paymentId, which a lone leg carries.
    await expect(payment.getByTestId('bdg-tx-menu-delete')).toBeEnabled()
  })
})
