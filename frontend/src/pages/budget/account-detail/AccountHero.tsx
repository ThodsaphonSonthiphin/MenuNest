import type {AccountSummaryDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'

const TYPE_LABEL: Record<AccountSummaryDto['type'], string> = {
  Cash: 'Cash account',
  Credit: 'Credit account',
  Loan: 'Loan account',
  Closed: 'Closed account',
}

export function AccountHero({account}: {account: AccountSummaryDto}) {
  return (
    <div className="bdg-account-hero" data-testid="bdg-account-hero">
      <div className="bdg-account-hero-type">{TYPE_LABEL[account.type]}</div>
      <div className="bdg-account-hero-name">{account.name}</div>
      <div className="bdg-account-hero-balance" data-testid="bdg-account-balance">
        {formatTHB(account.balance)}
      </div>
      <div className="bdg-account-hero-meta">
        <span>📈 {formatTHB(account.monthInflow)} in</span>
        <span>📉 {formatTHB(account.monthOutflow)} out</span>
      </div>
    </div>
  )
}
