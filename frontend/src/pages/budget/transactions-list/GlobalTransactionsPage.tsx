import {Link} from 'react-router-dom'
import {GlobalTransactionList} from './GlobalTransactionList'
import {TransactionDialog} from '../components/TransactionDialog'
import {useGlobalTransactionsPage} from './GlobalTransactionsPage.hooks'
import '../BudgetPage.css' // to ensure .bdg-page and standard styles are available

export function GlobalTransactionsPage() {
  const {
    txs,
    summary,
    isLoading,
    editingTx,
    setEditingTx,
    isAdding,
    setIsAdding,
    handleDelete,
  } = useGlobalTransactionsPage()

  if (isLoading || !summary) {
    return <div className="bdg-page bdg-loading">Loading transactions…</div>
  }

  const dialogOpen = isAdding || editingTx != null

  return (
    <div className="bdg-page" data-testid="global-transactions-page">
      <div className="bdg-acc-header">
        <div style={{display: 'flex', alignItems: 'center', gap: '8px'}}>
          <Link to="/budget" className="bdg-back-btn" aria-label="Back to budget">‹</Link>
          <div className="bdg-acc-title">Transactions</div>
        </div>
        <button
          type="button"
          className="bdg-add-cat-btn" // Reusing style for a small action button
          onClick={() => setIsAdding(true)}
        >
          + Add
        </button>
      </div>

      <div style={{padding: '16px'}}>
        {txs && txs.length > 0 ? (
          <GlobalTransactionList items={txs} onEdit={setEditingTx} onDelete={handleDelete} />
        ) : (
          <div style={{textAlign: 'center', color: '#666', marginTop: '32px'}}>
            No transactions this month.
          </div>
        )}
      </div>

      {dialogOpen && (
        <TransactionDialog
          accounts={summary.accounts}
          groups={summary.groups}
          existing={editingTx ?? undefined}
          onClose={() => {
            setIsAdding(false)
            setEditingTx(null)
          }}
        />
      )}
    </div>
  )
}
