import {useState, Fragment} from 'react'
import type {MonthlySummaryDto, EnvelopeDto} from '../../../shared/api/api'
import {useAppSelector} from '../../../store'
import {EnvelopeCard} from './EnvelopeCard'
import {TransactionDialog} from './TransactionDialog'
import {MoveMoneyDialog} from './MoveMoneyDialog'
import {CoverOverspendingDialog} from './CoverOverspendingDialog'
import {AddCategoryDialog} from './AddCategoryDialog'
import {AddGroupDialog} from './AddGroupDialog'
import {formatTHB} from '../BudgetPage.hooks'

/**
 * Stacked groups of envelope cards. Group headers render the totals
 * (assigned + available) on the right. Owns the four dialog state
 * machines spawned by per-card actions.
 */
export function EnvelopeList({summary}: {summary: MonthlySummaryDto}) {
  const filter = useAppSelector(s => s.budget.filter)
  const [txPreset, setTxPreset] = useState<{categoryId: string} | null>(null)
  const [moveFrom, setMoveFrom] = useState<EnvelopeDto | null>(null)
  const [coverFor, setCoverFor] = useState<EnvelopeDto | null>(null)
  const [addCatGroupId, setAddCatGroupId] = useState<string | null>(null)
  const [addGroupOpen, setAddGroupOpen] = useState(false)

  const groups = summary.groups
    .map(g => ({
      ...g,
      categories: g.categories.filter(c => {
        switch (filter) {
          case 'overspent':   return c.available < 0
          case 'underfunded': return c.targetType !== 'None' && (c.targetProgressFraction ?? 0) < 1
          case 'overfunded':  return c.available > (c.targetAmount ?? 0)
          case 'available':   return c.available > 0
          case 'snoozed':     return c.isHidden
          default:            return !c.isHidden
        }
      }),
    }))
    .filter(g => g.categories.length > 0)

  return (
    <div className="bdg-envelopes" data-testid="bdg-envelopes">
      {groups.map(g => (
        <Fragment key={g.groupId}>
          <div className="bdg-env-group-header">
            <span>{g.name}</span>
            <span className="bdg-env-group-actions">
              <span>{formatTHB(g.totalAssigned)} / {formatTHB(g.totalAvailable)}</span>
              <button
                type="button"
                className="bdg-add-cat-btn"
                data-testid="bdg-add-cat-btn"
                onClick={() => setAddCatGroupId(g.groupId)}
              >+ Cat</button>
            </span>
          </div>
          {g.categories.map(c => (
            <EnvelopeCard
              key={c.categoryId}
              cat={c}
              onAddTransaction={(categoryId) => setTxPreset({categoryId})}
              onMoveMoney={setMoveFrom}
              onCoverOverspending={setCoverFor}
            />
          ))}
        </Fragment>
      ))}

      <button
        type="button"
        className="bdg-add-group-btn"
        data-testid="bdg-add-group-btn"
        onClick={() => setAddGroupOpen(true)}
      >＋ Add Group</button>

      {txPreset && (
        <TransactionDialog
          accounts={summary.accounts}
          groups={summary.groups}
          preset={txPreset}
          onClose={() => setTxPreset(null)}
        />
      )}
      {moveFrom && (
        <MoveMoneyDialog from={moveFrom} groups={summary.groups} onClose={() => setMoveFrom(null)} />
      )}
      {coverFor && (
        <CoverOverspendingDialog overspent={coverFor} groups={summary.groups} onClose={() => setCoverFor(null)} />
      )}
      {addCatGroupId && (
        <AddCategoryDialog
          presetGroupId={addCatGroupId}
          onClose={() => setAddCatGroupId(null)}
        />
      )}
      {addGroupOpen && <AddGroupDialog onClose={() => setAddGroupOpen(false)} />}
    </div>
  )
}
