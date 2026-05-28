import {useCallback, useEffect, useRef, useState} from 'react'
import {Link, useNavigate, useParams} from 'react-router-dom'
import '../BudgetPage.css'
import {AccountHero} from './AccountHero'
import {AccountTransactionList} from './AccountTransactionList'
import {TransactionDialog} from '../components/TransactionDialog'
import {ReconcileBalanceDialog} from '../components/ReconcileBalanceDialog'
import {TransactionUndoToast} from '../components/TransactionUndoToast'
import {useAccountDetail} from './AccountDetailPage.hooks'
import {useAppDispatch, useAppSelector} from '../../../store'
import {
  api,
  useDeleteBudgetTransactionMutation,
  useGetBudgetSummaryQuery,
  type BudgetTransactionDto,
} from '../../../shared/api/api'

interface PendingDelete {
  tx: BudgetTransactionDto
  timerId: number
}

const UNDO_MS = 5000

export function AccountDetailPage() {
  const {accountId = ''} = useParams<{accountId: string}>()
  const navigate = useNavigate()
  const dispatch = useAppDispatch()
  const {account, items, isLoading, error, endSentinelRef, hasMore, applyEdit, applyDelete, applyRestore} =
    useAccountDetail(accountId)
  const [txOpen, setTxOpen] = useState(false)
  const [editing, setEditing] = useState<BudgetTransactionDto | null>(null)
  const [menuOpen, setMenuOpen] = useState(false)
  const [reconcileOpen, setReconcileOpen] = useState(false)
  const [pending, setPending] = useState<PendingDelete | null>(null)
  const [errorToast, setErrorToast] = useState<string | null>(null)
  const menuRef = useRef<HTMLDivElement | null>(null)

  const [deleteTx] = useDeleteBudgetTransactionMutation()
  const {year, month} = useAppSelector(s => s.budget)
  const {data: summary} = useGetBudgetSummaryQuery({year, month})

  // Account-level top-bar menu outside-click handler (unchanged).
  useEffect(() => {
    if (!menuOpen) return
    function onDoc(e: MouseEvent) {
      if (!menuRef.current?.contains(e.target as Node)) setMenuOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [menuOpen])

  // Mirror `pending` into a ref so the unmount cleanup (which has empty
  // deps and captures its initial closure) can see the latest value.
  const pendingRef = useRef<PendingDelete | null>(null)
  useEffect(() => { pendingRef.current = pending }, [pending])

  // Commit a pending delete (fire-and-forget). Used by the row timer, by
  // single-pending replacement (a second delete while one is pending),
  // and by the unmount cleanup. On API failure we restore the row and
  // surface an error toast.
  const commitPending = useCallback((p: PendingDelete) => {
    window.clearTimeout(p.timerId)
    void deleteTx({id: p.tx.id, year, month}).unwrap().catch(() => {
      // TODO: 404 means the row was already deleted elsewhere — skip restore in that case.
      applyRestore(p.tx)
      setErrorToast('Could not delete. Restored.')
    })
  }, [deleteTx, year, month, applyRestore])

  // Auto-dismiss the small error toast.
  useEffect(() => {
    if (!errorToast) return
    const id = window.setTimeout(() => setErrorToast(null), 3000)
    return () => window.clearTimeout(id)
  }, [errorToast])

  // Commit any still-pending delete when the page unmounts (e.g. user
  // navigated Back before the 5-second timer ran out). Without this the
  // user thinks the row is gone but the server still has it. We use
  // `dispatch(...)` directly so the request goes out even after React
  // tears the component down.
  useEffect(() => {
    return () => {
      const p = pendingRef.current
      if (!p) return
      window.clearTimeout(p.timerId)
      void dispatch(api.endpoints.deleteBudgetTransaction.initiate({id: p.tx.id, year, month}))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const handleEdit = useCallback((tx: BudgetTransactionDto) => {
    setEditing(tx)
    setTxOpen(true)
  }, [])

  const handleDelete = useCallback((tx: BudgetTransactionDto) => {
    // Single-pending policy: if a delete is already pending, commit it
    // immediately so we only ever have one undoable toast on screen.
    if (pending) commitPending(pending)

    applyDelete(tx.id)
    const timerId = window.setTimeout(() => {
      commitPending({tx, timerId: 0})   // timer already fired; clearTimeout is a no-op
      setPending(null)
    }, UNDO_MS)
    setPending({tx, timerId})
  }, [pending, commitPending, applyDelete])

  const handleUndo = useCallback(() => {
    if (!pending) return
    window.clearTimeout(pending.timerId)
    applyRestore(pending.tx)
    setPending(null)
  }, [pending, applyRestore])

  // The toast's onTimeout fires from inside the component, but our
  // setTimeout above already does the commit. Provide a no-op so the
  // toast component's contract stays simple — the page is the source of
  // truth for the timer (durationMs matches).
  const handleToastTimeout = useCallback(() => {
    // No-op; commit already scheduled by handleDelete's setTimeout.
  }, [])

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

      <AccountTransactionList
        items={items}
        endSentinelRef={endSentinelRef}
        onEdit={handleEdit}
        onDelete={handleDelete}
      />

      {!hasMore && items.length === 0 && (
        <div className="bdg-tx-empty">No transactions yet.</div>
      )}

      <button
        type="button"
        className="bdg-fab"
        onClick={() => { setEditing(null); setTxOpen(true) }}
        aria-label="Add transaction"
        data-testid="bdg-fab"
      >+</button>

      {txOpen && (
        <TransactionDialog
          accounts={summary?.accounts ?? []}
          groups={summary?.groups ?? []}
          existing={editing ?? undefined}
          preset={editing ? undefined : {accountId}}
          onSaved={(updated) => applyEdit(updated)}
          onClose={() => { setTxOpen(false); setEditing(null) }}
        />
      )}

      {reconcileOpen && (
        <ReconcileBalanceDialog
          accountId={accountId}
          trackedBalance={account.balance}
          onClose={() => setReconcileOpen(false)}
        />
      )}

      {pending && (
        <TransactionUndoToast
          key={pending.tx.id}
          message={`Deleted '${pending.tx.notes ?? pending.tx.categoryName ?? 'transaction'}'`}
          onUndo={handleUndo}
          onTimeout={handleToastTimeout}
          durationMs={UNDO_MS}
        />
      )}

      {errorToast && (
        <div className="bdg-undo-toast is-error" data-testid="bdg-error-toast" role="status" aria-live="polite">
          <span className="bdg-undo-toast-msg">{errorToast}</span>
        </div>
      )}
    </div>
  )
}
