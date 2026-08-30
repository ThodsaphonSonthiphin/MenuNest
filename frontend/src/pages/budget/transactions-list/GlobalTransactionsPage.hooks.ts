import {useState} from 'react'
import {useAppSelector} from '../../../store'
import {
  useDeletePaymentMutation,
  useListBudgetTransactionsQuery,
  useDeleteBudgetTransactionMutation,
} from '../../../shared/api/api'
import type {BudgetTransactionDto} from '../../../shared/api/api'
import {useBudgetData} from '../BudgetPage.hooks'
import {paymentDraftFromRow, type PaymentDraft, type PaymentTxRow} from '../lib/paymentRows'

export function useGlobalTransactionsPage() {
  const {year, month} = useAppSelector(s => s.budget)

  const {summary, isLoading: isSummaryLoading} = useBudgetData()

  const {data: txs, isLoading: isTxLoading} = useListBudgetTransactionsQuery({year, month})
  const [deleteTx] = useDeleteBudgetTransactionMutation()
  const [deletePayment] = useDeletePaymentMutation()

  const [editingTx, setEditingTx] = useState<BudgetTransactionDto | null>(null)
  const [isAdding, setIsAdding] = useState(false)
  // menunest-209: a payment is edited as a PAIR, through its own route.
  const [editingPayment, setEditingPayment] = useState<PaymentDraft | null>(null)

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

  // Returns null unless BOTH legs are present, and reads the funding Envelope
  // off the outflow leg (menunest-214) — see lib/paymentRows.ts, where that
  // rule is pinned by test.
  const handleEditPayment = (row: PaymentTxRow) => {
    setEditingPayment(paymentDraftFromRow(row))
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
