import {useEffect, useRef, useState} from 'react'
import {Link, useNavigate, useParams} from 'react-router-dom'
import '../BudgetPage.css'
import {AccountHero} from './AccountHero'
import {AccountTransactionList} from './AccountTransactionList'
import {TransactionDialog} from '../components/TransactionDialog'
import {ReconcileBalanceDialog} from '../components/ReconcileBalanceDialog'
import {useAccountDetail} from './AccountDetailPage.hooks'
import {useAppSelector} from '../../../store'
import {useGetBudgetSummaryQuery} from '../../../shared/api/api'

export function AccountDetailPage() {
  const {accountId = ''} = useParams<{accountId: string}>()
  const navigate = useNavigate()
  const {account, items, isLoading, error, endSentinelRef, hasMore} = useAccountDetail(accountId)
  const [txOpen, setTxOpen] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [reconcileOpen, setReconcileOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!menuOpen) return
    function onDoc(e: MouseEvent) {
      if (!menuRef.current?.contains(e.target as Node)) setMenuOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [menuOpen])

  const {year, month} = useAppSelector(s => s.budget)
  const {data: summary} = useGetBudgetSummaryQuery({year, month})

  if (isLoading && !account) {
    return <div className="bdg-loading">Loading…</div>
  }
  if (error || !account) {
    return (
      <div className="bdg-error">
        <p>Could not load this account.</p>
        <button type="button" onClick={() => navigate('/budget')}>Back to budget</button>
      </div>
    )
  }

  return (
    <div className="bdg-account-page" data-testid="bdg-account-page">
      <div className="bdg-top-bar">
        <Link to="/budget" className="bdg-back-btn" aria-label="Back">‹</Link>
        <div className="bdg-top-bar-title">{account.name}</div>
        <div ref={menuRef} className="bdg-menu-anchor">
          <button
            type="button"
            className="bdg-menu-btn"
            onClick={() => setMenuOpen(o => !o)}
            aria-label="Account menu"
            data-testid="bdg-account-menu"
          >⋯</button>
          {menuOpen && (
            <div className="bdg-menu-pop">
              <button
                type="button"
                className="bdg-menu-item"
                data-testid="bdg-menu-reconcile"
                onClick={() => { setMenuOpen(false); setReconcileOpen(true) }}
              >
                <span className="icon">⚖</span>
                <span>Reconcile balance</span>
              </button>
              <button type="button" className="bdg-menu-item is-disabled" disabled>
                <span className="icon">✎</span>
                <span>Edit account (soon)</span>
              </button>
              <button type="button" className="bdg-menu-item is-disabled" disabled>
                <span className="icon">🗄</span>
                <span>Close account (soon)</span>
              </button>
            </div>
          )}
        </div>
      </div>

      <AccountHero account={account} />

      <div className="bdg-section-title">
        <h3>Transactions · newest first</h3>
      </div>

      <AccountTransactionList items={items} endSentinelRef={endSentinelRef} />

      {!hasMore && items.length === 0 && (
        <div className="bdg-tx-empty">No transactions yet.</div>
      )}

      <button
        type="button"
        className="bdg-fab"
        onClick={() => setTxOpen(true)}
        aria-label="Add transaction"
        data-testid="bdg-fab"
      >+</button>

      {txOpen && (
        <TransactionDialog
          accounts={summary?.accounts ?? []}
          groups={summary?.groups ?? []}
          preset={{accountId}}
          onClose={() => setTxOpen(false)}
        />
      )}

      {reconcileOpen && (
        <ReconcileBalanceDialog
          accountId={accountId}
          trackedBalance={account.balance}
          onClose={() => setReconcileOpen(false)}
        />
      )}
    </div>
  )
}
