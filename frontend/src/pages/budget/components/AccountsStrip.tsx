import {useState} from 'react'
import {Link} from 'react-router-dom'
import type {BudgetAccountDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {AddAccountDialog} from './AddAccountDialog'
import {ReconcileBalanceDialog} from './ReconcileBalanceDialog'

const DOT_BY_TYPE: Record<BudgetAccountDto['type'], string> = {
  Cash: '',
  Credit: 'credit',
  Loan: 'loan',
  Closed: 'closed',
}

/**
 * Horizontal-scroll list of accounts at the top of /budget, sorted
 * server-side by CreatedAt DESC. Tapping a card routes to the
 * account-detail page; the trailing card opens AddAccountDialog.
 *
 * The ✎ button opens ReconcileBalanceDialog directly for that account
 * (menunest one-tap-affordances rework) — previously balance correction
 * was only reachable through the account-detail page's ⋯ menu. It shares
 * the ⋯ menu's chevron corner (grouped into one flow row, not two
 * absolutely-positioned elements fighting for the same pixels) and stops
 * propagation + prevents the default navigation, since the card itself is
 * a <Link> and a nested <button> click would otherwise both open the
 * dialog AND navigate to account-detail.
 */
export function AccountsStrip({accounts}: {accounts: BudgetAccountDto[]}) {
  const [addOpen, setAddOpen] = useState(false)
  const [reconcileFor, setReconcileFor] = useState<BudgetAccountDto | null>(null)
  return (
    <>
      <div className="bdg-section-title">
        <h3>Accounts · newest first</h3>
      </div>
      <div className="bdg-accounts-strip" data-testid="bdg-accounts-strip">
        {accounts.map(a => (
          <Link
            key={a.id}
            to={`/budget/accounts/${a.id}`}
            className="bdg-account-card"
            data-testid="bdg-account-card"
          >
            <div className="bdg-account-topline">
              <div className="bdg-account-name">
                <span className={`bdg-account-dot ${DOT_BY_TYPE[a.type]}`} />
                <span className="bdg-account-name-text">{a.name}</span>
              </div>
              <div className="bdg-account-topline-right">
                <button
                  type="button"
                  className="bdg-env-icon-btn"
                  onClick={(e) => { e.preventDefault(); e.stopPropagation(); setReconcileFor(a) }}
                  aria-label="Correct balance"
                  data-testid="bdg-account-correct-icon"
                >✎</button>
                <span className="bdg-account-chevron">›</span>
              </div>
            </div>
            <div className="bdg-account-balance">{formatTHB(a.balance)}</div>
          </Link>
        ))}
        <button
          type="button"
          className="bdg-account-card bdg-account-card--add"
          onClick={() => setAddOpen(true)}
          data-testid="bdg-add-account"
        >
          + Add
        </button>
      </div>
      {addOpen && <AddAccountDialog onClose={() => setAddOpen(false)} />}
      {reconcileFor && (
        <ReconcileBalanceDialog
          accountId={reconcileFor.id}
          onClose={() => setReconcileFor(null)}
        />
      )}
    </>
  )
}
