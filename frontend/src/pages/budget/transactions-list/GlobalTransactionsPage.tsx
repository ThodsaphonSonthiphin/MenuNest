import {Link} from 'react-router-dom'
import {GlobalTransactionList} from './GlobalTransactionList'
import {TransactionDialog} from '../components/TransactionDialog'
import {PaymentDialog} from '../components/PaymentDialog'
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
    editingPayment,
    setEditingPayment,
    handleEditPayment,
    handleDeletePayment,
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
          <GlobalTransactionList
            items={txs}
            accounts={summary.accounts}
            onEdit={setEditingTx}
            onDelete={handleDelete}
            onEditPayment={handleEditPayment}
            onDeletePayment={handleDeletePayment}
          />
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

      {editingPayment && (() => {
        const toAccount = summary.accounts.find(a => a.id === editingPayment.toAccountId)
        if (!toAccount) return null
        return (
          <PaymentDialog
            toAccount={toAccount}
            accounts={summary.accounts}
            groups={summary.groups}
            existing={editingPayment}
            onClose={() => setEditingPayment(null)}
          />
        )
      })()}
    </div>
  )
}
