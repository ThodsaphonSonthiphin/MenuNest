import {Fragment, useState} from 'react'
import type {MonthlySummaryDto, EnvelopeDto} from '../../../shared/api/api'
import {useAppSelector} from '../../../store'
import {EnvelopeRow} from './EnvelopeRow'
import {MoveMoneyDialog} from './MoveMoneyDialog'
import {CoverOverspendingDialog} from './CoverOverspendingDialog'
import {formatTHB} from '../BudgetPage.hooks'

/**
 * Envelope table — the YNAB "zero-based budget" grid. Groups render as
 * sticky header rows with per-group totals, and each category is an
 * inline-editable `EnvelopeRow`. The currently selected filter chip in
 * the Redux `budget` slice narrows which categories are shown.
 *
 * We use explicit `<Fragment key>` rather than shorthand `<>` so React's
 * list-reconciliation has a stable key for each group's 3-row block.
 */
export function EnvelopeTable({summary}: {summary: MonthlySummaryDto}) {
  const filter = useAppSelector(s => s.budget.filter)
  const [moveFrom, setMoveFrom] = useState<EnvelopeDto | null>(null)
  const [coverFor, setCoverFor] = useState<EnvelopeDto | null>(null)

  const groups = summary.groups
    .map(g => {
      const cats = g.categories.filter(c => {
        switch (filter) {
          case 'overspent':   return c.available < 0
          case 'underfunded': return c.targetType !== 'None' && (c.targetProgressFraction ?? 0) < 1
          case 'overfunded':  return c.available > (c.targetAmount ?? 0)
          case 'available':   return c.available > 0
          case 'snoozed':     return c.isHidden
          default:            return !c.isHidden
        }
      })
      return {...g, categories: cats}
    })
    .filter(g => g.categories.length > 0)

  return (
    <div className="budget-envelopes">
      <table>
        <thead>
          <tr>
            <th style={{width: '48%'}}>Category</th>
            <th className="right" style={{width: '16%'}}>Assigned</th>
            <th className="right" style={{width: '16%'}}>Activity</th>
            <th className="right" style={{width: '20%'}}>Available</th>
          </tr>
        </thead>
        <tbody>
          {groups.map(g => (
            <Fragment key={g.groupId}>
              <tr className="budget-group-row">
                <td colSpan={4}>▾ {g.name}</td>
              </tr>
              {g.categories.map(c => (
                <EnvelopeRow key={c.categoryId} cat={c} onMove={setMoveFrom} onCover={setCoverFor} />
              ))}
              <tr>
                <td style={{padding: '6px 16px', fontSize: 11, color: 'var(--text-muted)', textTransform: 'uppercase'}}>
                  {g.name} total
                </td>
                <td className="right" style={{fontSize: 12, color: 'var(--text-dim)'}}>
                  {formatTHB(g.totalAssigned)}
                </td>
                <td className="right" style={{fontSize: 12, color: g.totalActivity < 0 ? 'var(--red)' : 'var(--text-dim)'}}>
                  {formatTHB(g.totalActivity)}
                </td>
                <td className="right" style={{fontSize: 12, color: 'var(--text-dim)'}}>
                  {formatTHB(g.totalAvailable)}
                </td>
              </tr>
            </Fragment>
          ))}
        </tbody>
      </table>

      {moveFrom && (
        <MoveMoneyDialog from={moveFrom} groups={summary.groups} onClose={() => setMoveFrom(null)} />
      )}
      {coverFor && (
        <CoverOverspendingDialog overspent={coverFor} groups={summary.groups} onClose={() => setCoverFor(null)} />
      )}
    </div>
  )
}
