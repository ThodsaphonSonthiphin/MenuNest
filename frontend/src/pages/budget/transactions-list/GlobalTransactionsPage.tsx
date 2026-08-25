import {useState} from 'react'
import {Link} from 'react-router-dom'
import {useAppSelector} from '../../../store'
import {useListBudgetTransactionsQuery, useDeleteBudgetTransactionMutation} from '../../../shared/api/api'
import type {BudgetTransactionDto} from '../../../shared/api/api'
import {useBudgetData} from '../BudgetPage.hooks'
import {GlobalTransactionList} from './GlobalTransactionList'
import {TransactionDialog} from '../components/TransactionDialog'
import '../BudgetPage.css' // to ensure .bdg-page and standard styles are available

export function GlobalTransactionsPage() {
  const {year, month} = useAppSelector(s => s.budget)
  
  // Need accounts and groups for the TransactionDialog
  const {summary, isLoading: isSummaryLoading} = useBudgetData()
  
  const {data: txs, isLoading: isTxLoading} = useListBudgetTransactionsQuery({year, month})
  const [deleteTx] = useDeleteBudgetTransactionMutation()

  const [editingTx, setEditingTx] = useState<BudgetTransactionDto | null>(null)
  const [isAdding, setIsAdding] = useState(false)

  const isLoading = isSummaryLoading || isTxLoading

  if (isLoading || !summary) {
    return <div className="bdg-page bdg-loading">Loading transactions…</div>
  }

  const handleDelete = async (tx: BudgetTransactionDto) => {
    if (window.confirm('Delete this transaction?')) {
      try {
        await deleteTx({id: tx.id, year, month}).unwrap()
      } catch {
        window.alert('Failed to delete transaction.')
      }
    }
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
