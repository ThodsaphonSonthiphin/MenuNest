import {useState} from 'react'
import {useAppSelector} from '../../../store'
import {
  useDeletePaymentMutation,
  useListBudgetTransactionsQuery,
  useDeleteBudgetTransactionMutation,
} from '../../../shared/api/api'
import type {BudgetTransactionDto} from '../../../shared/api/api'
import {useBudgetData} from '../BudgetPage.hooks'
import type {PaymentTxRow} from '../lib/paymentRows'
import type {PaymentDraft} from '../components/PaymentDialog'

export function useGlobalTransactionsPage() {
  const {year, month} = useAppSelector(s => s.budget)

  const {summary, isLoading: isSummaryLoading} = useBudgetData()

  const {data: txs, isLoading: isTxLoading} = useListBudgetTransactionsQuery({year, month})
  const [deleteTx] = useDeleteBudgetTransactionMutation()
  const [deletePayment] = useDeletePaymentMutation()

  const [editingTx, setEditingTx] = useState<BudgetTransactionDto | null>(null)
  const [isAdding, setIsAdding] = useState(false)
  // menunest-209: a payment is edited as a PAIR, through its own route. The
  // draft carries the paying account and the funding Envelope off the OUTFLOW
  // leg — the only leg that ever holds a category (menunest-214).
  const [editingPayment, setEditingPayment] = useState<PaymentDraft & {toAccountId: string} | null>(null)

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

  const handleEditPayment = (row: PaymentTxRow) => {
    if (!row.fromLeg || !row.toLeg) return   // an edit needs both halves
    setEditingPayment({
      paymentId: row.paymentId,
      fromAccountId: row.fromLeg.accountId,
      toAccountId: row.toLeg.accountId,
      amount: row.amount,
      date: row.date,
      notes: row.notes,
      categoryId: row.fromLeg.categoryId,
    })
  }

  const handleDeletePayment = async (row: PaymentTxRow) => {
    if (!window.confirm('Delete this payment? Both halves are removed.')) return
    try {
      await deletePayment(row.paymentId).unwrap()
    } catch {
      window.alert('Failed to delete payment.')
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
    editingPayment,
    setEditingPayment,
    handleEditPayment,
    handleDeletePayment,
  }
}
