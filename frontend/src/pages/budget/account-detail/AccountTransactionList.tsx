import {Fragment} from 'react'
import type {BudgetTransactionDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'

interface Props {
  items: BudgetTransactionDto[]
  /** Sentinel for IntersectionObserver — page-end ref. Caller wires it. */
  endSentinelRef: React.RefObject<HTMLDivElement | null>
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

export function AccountTransactionList({items, endSentinelRef}: Props) {
  // Bucket by Date — preserves CreatedAt DESC order within each bucket.
  const buckets: {date: string; rows: BudgetTransactionDto[]}[] = []
  for (const tx of items) {
    const last = buckets[buckets.length - 1]
    if (last && last.date === tx.date) last.rows.push(tx)
    else buckets.push({date: tx.date, rows: [tx]})
  }

  return (
    <div className="bdg-tx-feed" data-testid="bdg-tx-feed">
      {buckets.map((b) => (
        <Fragment key={b.date}>
          <div className="bdg-tx-date-header">{dateHeaderFor(b.date)}</div>
          {b.rows.map(tx => (
            <div key={tx.id} className="bdg-tx-row" data-testid="bdg-tx-row">
              <div className="bdg-tx-icon">{tx.categoryEmoji ?? '•'}</div>
              <div className="bdg-tx-body">
                <div className="bdg-tx-title">{tx.notes ?? tx.categoryName ?? 'Transaction'}</div>
                <div className="bdg-tx-sub">{tx.categoryName ?? 'Uncategorized'}</div>
              </div>
              <div className={`bdg-tx-amount ${tx.amount >= 0 ? 'is-income' : ''}`}>
                {tx.amount >= 0 ? '+' : ''}{formatTHB(tx.amount)}
              </div>
            </div>
          ))}
        </Fragment>
      ))}
      <div ref={endSentinelRef} className="bdg-tx-sentinel" />
    </div>
  )
}
