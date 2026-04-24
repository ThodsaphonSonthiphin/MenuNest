import {useState} from 'react'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import type {BudgetAccountDto, BudgetAccountType} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {AddAccountDialog} from './AddAccountDialog'

type Group = {label: string; type: BudgetAccountType; dotClass: string}
const GROUPS: Group[] = [
  {label: 'CASH',   type: 'Cash',   dotClass: ''},
  {label: 'CREDIT', type: 'Credit', dotClass: 'credit'},
  {label: 'LOANS',  type: 'Loan',   dotClass: 'loan'},
]

export function AccountsSidebar({accounts, inDrawer = false}: {accounts: BudgetAccountDto[]; inDrawer?: boolean}) {
  const [addOpen, setAddOpen] = useState(false)
  const open = accounts.filter(a => !a.isClosed)
  const closed = accounts.filter(a => a.isClosed)

  return (
    <aside className={inDrawer ? '' : 'budget-accounts-sidebar'}>
      {GROUPS.map(g => {
        const items = open.filter(a => a.type === g.type)
        const total = items.reduce((s, a) => s + a.balance, 0)
        return (
          <div key={g.type}>
            <div className="budget-account-group-header">
              <span className="budget-account-group-label">{g.label}</span>
              <span className="budget-account-group-label">{formatTHB(total)}</span>
            </div>
            {items.map(a => (
              <div key={a.id} className="budget-account-row">
                <span className="budget-account-name">
                  <span className={`budget-account-dot ${g.dotClass}`} />{a.name}
                </span>
                <span>{formatTHB(a.balance)}</span>
              </div>
            ))}
          </div>
        )
      })}

      {closed.length > 0 && (
        <div className="budget-account-group-header">
          <span className="budget-account-group-label" style={{color: '#444'}}>CLOSED</span>
        </div>
      )}

      <div className="budget-sidebar-footer">
        <Button variant={Variant.Outlined} color={Color.Secondary} onClick={() => setAddOpen(true)}>
          + Add Account
        </Button>
      </div>

      {addOpen && <AddAccountDialog onClose={() => setAddOpen(false)} />}
    </aside>
  )
}
