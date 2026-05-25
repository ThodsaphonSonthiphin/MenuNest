import {useState} from 'react'
import {Link} from 'react-router-dom'
import type {BudgetAccountDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {AddAccountDialog} from './AddAccountDialog'

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
 */
export function AccountsStrip({accounts}: {accounts: BudgetAccountDto[]}) {
  const [addOpen, setAddOpen] = useState(false)
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
            <span className="bdg-account-chevron">›</span>
            <div className="bdg-account-name">
              <span className={`bdg-account-dot ${DOT_BY_TYPE[a.type]}`} />
              {a.name}
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
    </>
  )
}
