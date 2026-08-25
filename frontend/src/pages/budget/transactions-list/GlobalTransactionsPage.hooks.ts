import {useState} from 'react'
import {useAppSelector} from '../../../store'
import {useListBudgetTransactionsQuery, useDeleteBudgetTransactionMutation} from '../../../shared/api/api'
import type {BudgetTransactionDto} from '../../../shared/api/api'
import {useBudgetData} from '../BudgetPage.hooks'

export function useGlobalTransactionsPage() {
  const {year, month} = useAppSelector(s => s.budget)
  
  const {summary, isLoading: isSummaryLoading} = useBudgetData()
  
  const {data: txs, isLoading: isTxLoading} = useListBudgetTransactionsQuery({year, month})
  const [deleteTx] = useDeleteBudgetTransactionMutation()

  const [editingTx, setEditingTx] = useState<BudgetTransactionDto | null>(null)
  const [isAdding, setIsAdding] = useState(false)

  const isLoading = isSummaryLoading || isTxLoading

  const handleDelete = async (tx: BudgetTransactionDto) => {
    if (window.confirm('Delete this transaction?')) {
      try {
        await deleteTx({id: tx.id, year, month}).unwrap()
      } catch {
        window.alert('Failed to delete transaction.')
      }
    }
  }

  return {
    txs,
    summary,
    isLoading,
    editingTx,
    setEditingTx,
    isAdding,
    setIsAdding,
    handleDelete,
  }
}
