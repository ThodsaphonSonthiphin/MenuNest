import {Fragment, useEffect, useRef, useState} from 'react'
import type {BudgetAccountDto, BudgetTransactionDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {groupPaymentLegs, type PaymentTxRow, type TxRow} from '../lib/paymentRows'
import {PaymentTransactionRow} from '../components/PaymentTransactionRow'

export interface GlobalTransactionListProps {
  items: BudgetTransactionDto[]
  /** Resolves the paid account's TYPE, which picks menunest-212's action word. */
  accounts: BudgetAccountDto[]
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
  const d = new Date()
  const yest = new Date(d.getFullYear(), d.getMonth(), d.getDate() - 1)
  const yestIso = `${yest.getFullYear()}-${String(yest.getMonth() + 1).padStart(2, '0')}-${String(yest.getDate()).padStart(2, '0')}`
  if (iso === yestIso) return `Yesterday · ${formatDateShort(iso)}`
  return formatDateShort(iso)
}

function formatDateShort(iso: string): string {
  const [, m, d] = iso.split('-').map(Number)
  const months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec']
  return `${months[m - 1]} ${d}`
}

export function GlobalTransactionList({
  items, accounts, onEdit, onDelete, onEditPayment, onDeletePayment,
}: GlobalTransactionListProps) {
  // menunest-209: collapse the two legs of a payment into ONE row BEFORE
  // bucketing — rendered as two rows they offer two Edit and two Delete
  // buttons, every one of which the backend refuses.
  const sortedItems = [...items].sort((a, b) => b.date.localeCompare(a.date))
  const rows = groupPaymentLegs(sortedItems)

  const typeById = new Map(accounts.map(a => [a.id, a.type]))

  // Bucket by Date
  const buckets: {date: string; rows: TxRow[]}[] = []
  for (const row of rows) {
    const date = row.kind === 'payment' ? row.date : row.tx.date
    const last = buckets[buckets.length - 1]
    if (last && last.date === date) last.rows.push(row)
    else buckets.push({date, rows: [row]})
  }

  const [openMenuId, setOpenMenuId] = useState<string | null>(null)
  const containerRef = useRef<HTMLDivElement | null>(null)

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
      const anchor = (target as HTMLElement).closest('.bdg-tx-menu-anchor')
      if (!anchor) setOpenMenuId(null)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [openMenuId])

  return (
    <div ref={containerRef} className="bdg-tx-feed" data-testid="global-tx-feed">
      {buckets.map((b) => (
        <Fragment key={b.date}>
          <div className="bdg-tx-date-header">{dateHeaderFor(b.date)}</div>
          {b.rows.map(row => {
            if (row.kind === 'payment') {
              return (
                <PaymentTransactionRow
                  key={row.key}
                  row={row}
                  toAccountType={row.toLeg ? typeById.get(row.toLeg.accountId) ?? null : null}
                  isOpen={openMenuId === row.key}
                  onToggleMenu={setOpenMenuId}
                  onEditPayment={onEditPayment}
                  onDeletePayment={onDeletePayment}
                  testId="global-tx-row"
                />
              )
            }
            const tx = row.tx
            const isOpen = openMenuId === tx.id
            const subtitle = [tx.categoryName ?? 'Uncategorized', tx.accountName].filter(Boolean).join(' • ')

            return (
              <div key={tx.id} className="bdg-tx-row" data-testid="global-tx-row" data-tx-id={tx.id}>
                <div className="bdg-tx-icon">{tx.categoryEmoji ?? '•'}</div>
                <div className="bdg-tx-body">
                  <div className="bdg-tx-title">{tx.notes ?? tx.categoryName ?? 'Transaction'}</div>
                  <div className="bdg-tx-sub">{subtitle}</div>
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
    </div>
  )
}
