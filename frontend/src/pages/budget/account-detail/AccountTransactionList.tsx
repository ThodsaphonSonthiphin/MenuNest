import {Fragment, useEffect, useRef, useState} from 'react'
import type {BudgetAccountType, BudgetTransactionDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {groupPaymentLegs, type PaymentTxRow, type TxRow} from '../lib/paymentRows'
import {PaymentTransactionRow} from '../components/PaymentTransactionRow'

interface Props {
  items: BudgetTransactionDto[]
  /**
   * The type of the account this feed belongs to — menunest-212's action word,
   * for a payment row whose INFLOW leg is on this very account. When only the
   * outflow leg is here the account being paid is off-feed, and no word is
   * guessed (see `paymentRowLabel`).
   */
  accountType: BudgetAccountType
  /** Sentinel for IntersectionObserver — page-end ref. Caller wires it. */
  endSentinelRef: React.RefObject<HTMLDivElement | null>
  onEdit: (tx: BudgetTransactionDto) => void
  onDelete: (tx: BudgetTransactionDto) => void
  onEditPayment: (row: PaymentTxRow) => void
  onDeletePayment: (row: PaymentTxRow) => void
}

function todayIso(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function dateHeaderFor(iso: string): string {
  const today = todayIso()
  if (iso === today) return `Today · ${formatDateShort(iso)}`
  const yest = new Date(Date.now() - 86400_000)
  const yestIso = `${yest.getFullYear()}-${String(yest.getMonth() + 1).padStart(2, '0')}-${String(yest.getDate()).padStart(2, '0')}`
  if (iso === yestIso) return `Yesterday · ${formatDateShort(iso)}`
  return formatDateShort(iso)
}

function formatDateShort(iso: string): string {
  const [, m, d] = iso.split('-').map(Number)
  const months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec']
  return `${months[m - 1]} ${d}`
}

export function AccountTransactionList({
  items, accountType, endSentinelRef, onEdit, onDelete, onEditPayment, onDeletePayment,
}: Props) {
  // menunest-209: a payment is ONE row. This feed is filtered to a single
  // account, so it only ever holds one leg of any payment — grouped anyway, so
  // the row offers the payment's own controls instead of a transaction's,
  // which the backend refuses on a leg.
  const rows = groupPaymentLegs(items)

  // Bucket by Date — preserves CreatedAt DESC order within each bucket.
  const buckets: {date: string; rows: TxRow[]}[] = []
  for (const row of rows) {
    const date = row.kind === 'payment' ? row.date : row.tx.date
    const last = buckets[buckets.length - 1]
    if (last && last.date === date) last.rows.push(row)
    else buckets.push({date, rows: [row]})
  }

  const [openMenuId, setOpenMenuId] = useState<string | null>(null)
  const containerRef = useRef<HTMLDivElement | null>(null)

  // Close the open menu when the user clicks anywhere outside any menu.
  useEffect(() => {
    if (!openMenuId) return
    function onDoc(e: MouseEvent) {
      const root = containerRef.current
      if (!root) return
      const target = e.target as Node
      if (!root.contains(target)) {
        setOpenMenuId(null)
        return
      }
      // Click was inside the feed but not on a menu anchor — close.
      const anchor = (target as HTMLElement).closest('.bdg-tx-menu-anchor')
      if (!anchor) setOpenMenuId(null)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [openMenuId])

  return (
    <div ref={containerRef} className="bdg-tx-feed" data-testid="bdg-tx-feed">
      {buckets.map((b) => (
        <Fragment key={b.date}>
          <div className="bdg-tx-date-header">{dateHeaderFor(b.date)}</div>
          {b.rows.map(row => {
            if (row.kind === 'payment') {
              return (
                <PaymentTransactionRow
                  key={row.key}
                  row={row}
                  toAccountType={row.toLeg ? accountType : null}
                  isOpen={openMenuId === row.key}
                  onToggleMenu={setOpenMenuId}
                  onEditPayment={onEditPayment}
                  onDeletePayment={onDeletePayment}
                  testId="bdg-tx-row"
                />
              )
            }
            const tx = row.tx
            const isOpen = openMenuId === tx.id
            return (
              <div key={tx.id} className="bdg-tx-row" data-testid="bdg-tx-row" data-tx-id={tx.id}>
                <div className="bdg-tx-icon">{tx.categoryEmoji ?? '•'}</div>
                <div className="bdg-tx-body">
                  <div className="bdg-tx-title">{tx.notes ?? tx.categoryName ?? 'Transaction'}</div>
                  <div className="bdg-tx-sub">{tx.categoryName ?? 'Uncategorized'}</div>
                </div>
                <div className={`bdg-tx-amount ${tx.amount >= 0 ? 'is-income' : ''}`}>
                  {tx.amount >= 0 ? '+' : ''}{formatTHB(tx.amount)}
                </div>
                <div className="bdg-tx-menu-anchor">
                  <button
                    type="button"
                    className="bdg-tx-menu-btn"
                    aria-label={`Menu for ${tx.notes ?? tx.categoryName ?? 'transaction'}`}
                    aria-haspopup="menu"
                    aria-expanded={isOpen}
                    data-testid="bdg-tx-menu-btn"
                    onClick={() => setOpenMenuId(isOpen ? null : tx.id)}
                  >
                    ⋯
                  </button>
                  {isOpen && (
                    <div className="bdg-tx-menu-pop" role="menu">
                      <button
                        type="button"
                        className="bdg-tx-menu-item"
                        data-testid="bdg-tx-menu-edit"
                        role="menuitem"
                        onClick={() => { setOpenMenuId(null); onEdit(tx) }}
                      >
                        <span className="icon">✎</span>
                        <span>Edit</span>
                      </button>
                      <button
                        type="button"
                        className="bdg-tx-menu-item is-destructive"
                        data-testid="bdg-tx-menu-delete"
                        role="menuitem"
                        onClick={() => { setOpenMenuId(null); onDelete(tx) }}
                      >
                        <span className="icon">🗑</span>
                        <span>Delete</span>
                      </button>
                    </div>
                  )}
                </div>
              </div>
            )
          })}
        </Fragment>
      ))}
      <div ref={endSentinelRef} className="bdg-tx-sentinel" />
    </div>
  )
}
