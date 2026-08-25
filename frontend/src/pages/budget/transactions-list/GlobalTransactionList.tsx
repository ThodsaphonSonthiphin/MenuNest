import {Fragment, useEffect, useRef, useState} from 'react'
import type {BudgetTransactionDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'

export interface GlobalTransactionListProps {
  items: BudgetTransactionDto[]
  onEdit: (tx: BudgetTransactionDto) => void
  onDelete: (tx: BudgetTransactionDto) => void
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

export function GlobalTransactionList({items, onEdit, onDelete}: GlobalTransactionListProps) {
  // Bucket by Date
  const buckets: {date: string; rows: BudgetTransactionDto[]}[] = []
  for (const tx of items) {
    const last = buckets[buckets.length - 1]
    if (last && last.date === tx.date) last.rows.push(tx)
    else buckets.push({date: tx.date, rows: [tx]})
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
          {b.rows.map(tx => {
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
