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
 * was only reachable through the account-detail page's ⋯ menu.
 *
 * The card itself is a plain <div>, not a <Link> — a <button> nested
 * inside a native <a> is invalid HTML5 and unreliable for assistive
 * tech, even though the click handling can be made to work. Instead
 * `.bdg-account-card-link` is an invisible "stretched link"
 * (position: absolute; inset: 0) that makes the whole card tappable,
 * and ✎ is a true DOM sibling stacked above it (higher z-index) so it
 * intercepts its own clicks without ever touching the link's — no
 * preventDefault/stopPropagation needed, because the two are no longer
 * ancestor/descendant.
 */
export function AccountsStrip({accounts, readyToAssign}: {accounts: BudgetAccountDto[], readyToAssign: number}) {
  const [addOpen, setAddOpen] = useState(false)
  const [reconcileFor, setReconcileFor] = useState<BudgetAccountDto | null>(null)

  const totalAccounts = accounts.reduce((sum, a) => sum + a.balance, 0)
  const overage = readyToAssign < 0 ? Math.abs(readyToAssign) : 0

  return (
    <>
      <div className="bdg-section-title">
        <h3 style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          บัญชีรวม {formatTHB(totalAccounts)}
          {overage > 0 && (
            <span className="bdg-accounts-overage">
              (ตั้งงบเกิน -{formatTHB(overage)})
            </span>
          )}
        </h3>
      </div>
      <div className="bdg-accounts-strip" data-testid="bdg-accounts-strip">
        {accounts.map(a => (
          <div key={a.id} className="bdg-account-card" data-testid="bdg-account-card">
            <Link
              to={`/budget/accounts/${a.id}`}
              className="bdg-account-card-link"
              aria-label={`${a.name} — ${formatTHB(a.balance)}`}
            />
            <div className="bdg-account-topline">
              <div className="bdg-account-name">
                <span className={`bdg-account-dot ${DOT_BY_TYPE[a.type]}`} />
                <span className="bdg-account-name-text">{a.name}</span>
              </div>
              <span className="bdg-account-chevron">›</span>
            </div>
            <div className="bdg-account-balance">{formatTHB(a.balance)}</div>
            <button
              type="button"
              className="bdg-env-icon-btn is-accent bdg-account-correct-btn"
              onClick={() => setReconcileFor(a)}
              aria-label="Correct balance"
              data-testid="bdg-account-correct-icon"
            >✎</button>
          </div>
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
