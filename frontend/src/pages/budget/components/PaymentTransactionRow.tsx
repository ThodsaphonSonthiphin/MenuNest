import type {BudgetAccountType} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {paymentRowLabel, type PaymentTxRow} from '../lib/paymentRows'

/**
 * menunest-209 / correction #3 — one payment, ONE row.
 *
 * Shared by `GlobalTransactionList` and `AccountTransactionList` so the two
 * feeds cannot drift into offering different controls for the same object.
 * Edit and Delete go to `PUT`/`DELETE /api/budget/payments/{paymentId}`, never
 * to a transaction route: the backend refuses a single leg, by design.
 *
 * Edit is disabled when only ONE leg is visible (an account-detail feed is
 * filtered to one account, so it always is). Delete is not — it needs nothing
 * but the `paymentId`, which a lone leg carries.
 */
export function PaymentTransactionRow({
  row,
  toAccountType,
  isOpen,
  onToggleMenu,
  onEditPayment,
  onDeletePayment,
  testId,
}: {
  row: PaymentTxRow
  toAccountType: BudgetAccountType | null
  isOpen: boolean
  onToggleMenu: (key: string | null) => void
  onEditPayment: (row: PaymentTxRow) => void
  onDeletePayment: (row: PaymentTxRow) => void
  testId: string
}) {
  const {title, subtitle} = paymentRowLabel(row, toAccountType)
  // A complete pair is a transfer between two accounts, so it has no single
  // sign — show the magnitude. A lone leg shows what actually hit THIS account.
  const signedForThisFeed = row.complete ? null : row.legs[0]?.amount ?? 0

  return (
    <div
      className="bdg-tx-row is-payment"
      data-testid={testId}
      data-payment-id={row.paymentId}
    >
      <div className="bdg-tx-icon">💳</div>
      <div className="bdg-tx-body">
        <div className="bdg-tx-title">{title}</div>
        <div className="bdg-tx-sub">{subtitle}</div>
      </div>
      <div className={`bdg-tx-amount ${signedForThisFeed !== null && signedForThisFeed > 0 ? 'is-income' : ''}`}>
        {signedForThisFeed === null
          ? formatTHB(row.amount)
          : `${signedForThisFeed > 0 ? '+' : ''}${formatTHB(signedForThisFeed)}`}
      </div>
      <div className="bdg-tx-menu-anchor">
        <button
          type="button"
          className="bdg-tx-menu-btn"
          aria-label={`Menu for ${title}`}
          aria-haspopup="menu"
          aria-expanded={isOpen}
          data-testid="bdg-tx-menu-btn"
          onClick={() => onToggleMenu(isOpen ? null : row.key)}
        >
          ⋯
        </button>
        {isOpen && (
          <div className="bdg-tx-menu-pop" role="menu">
            <button
              type="button"
              className="bdg-tx-menu-item"
              data-testid="bdg-tx-menu-edit"
              role="menuitem"
              disabled={!row.complete}
              title={row.complete
                ? undefined
                : 'การจ่ายหนี้แก้ได้ทั้งคู่เท่านั้น — เปิดหน้า Transactions'}
              onClick={() => { onToggleMenu(null); onEditPayment(row) }}
            >
              <span className="icon">✎</span>
              <span>Edit payment</span>
            </button>
            <button
              type="button"
              className="bdg-tx-menu-item is-destructive"
              data-testid="bdg-tx-menu-delete"
              role="menuitem"
              onClick={() => { onToggleMenu(null); onDeletePayment(row) }}
            >
              <span className="icon">🗑</span>
              <span>Delete payment</span>
            </button>
          </div>
        )}
      </div>
    </div>
  )
}
