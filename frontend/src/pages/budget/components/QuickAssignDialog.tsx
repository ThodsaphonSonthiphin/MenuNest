import {useState} from 'react'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {useAppSelector} from '../../../store'
import {
  useSetAssignedAmountMutation,
  type EnvelopeDto,
  type MonthlySummaryDto,
} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

export type QuickAssignMode = 'targets' | 'equally'

interface Allocation {
  cat: EnvelopeDto
  add: number
  newAssigned: number
}

/**
 * Build the allocation plan for the chosen quick-assign mode.
 * - "targets": fill every category that has a target, ByDate first
 *   (sorted by due date), then Monthly. Each gets min(need, remaining).
 * - "equally": split the available RTA evenly across all visible
 *   non-hidden envelopes; the last envelope takes the rounding remainder.
 */
function planAllocations(summary: MonthlySummaryDto, mode: QuickAssignMode): Allocation[] {
  const rta = summary.readyToAssign
  if (rta <= 0) return []

  const allCats = summary.groups
    .flatMap(g => g.categories)
    .filter(c => !c.isHidden)

  if (mode === 'targets') {
    type Need = {cat: EnvelopeDto; need: number; priority: number; tiebreak: number}
    const needs: Need[] = []
    for (const c of allCats) {
      if (c.targetType === 'None' || c.targetAmount == null) continue
      let need = 0
      let priority = 99
      let tiebreak = c.sortOrder
      if (c.targetType === 'ByDate') {
        need = Math.max(0, c.targetAmount - c.available)
        priority = 0
        if (c.targetDueDate) tiebreak = new Date(c.targetDueDate).getTime()
      } else if (c.targetType === 'MonthlyAmount' || c.targetType === 'MonthlySavingsBuilder') {
        need = Math.max(0, c.targetAmount - c.assigned)
        priority = 1
      }
      if (need > 0) needs.push({cat: c, need, priority, tiebreak})
    }
    needs.sort((a, b) => a.priority - b.priority || a.tiebreak - b.tiebreak)

    const out: Allocation[] = []
    let remaining = rta
    for (const n of needs) {
      if (remaining <= 0) break
      const add = Math.min(n.need, remaining)
      // Round to 2 decimals to avoid float drift in display + payload.
      const addRounded = Math.round(add * 100) / 100
      if (addRounded <= 0) continue
      out.push({
        cat: n.cat,
        add: addRounded,
        newAssigned: Math.round((n.cat.assigned + addRounded) * 100) / 100,
      })
      remaining = Math.round((remaining - addRounded) * 100) / 100
    }
    return out
  }

  // mode === 'equally'
  if (allCats.length === 0) return []
  const per = Math.floor((rta / allCats.length) * 100) / 100
  let remainder = Math.round((rta - per * allCats.length) * 100) / 100
  const out: Allocation[] = []
  for (let i = 0; i < allCats.length; i++) {
    const c = allCats[i]
    let add = per
    // Last envelope absorbs rounding remainder so the sum lands on rta exactly.
    if (i === allCats.length - 1) add = Math.round((add + remainder) * 100) / 100
    if (add <= 0) continue
    out.push({cat: c, add, newAssigned: Math.round((c.assigned + add) * 100) / 100})
  }
  return out
}

/**
 * Preview + apply dialog for the two quick-assign chips. Shows every
 * affected envelope with its delta, then fires setAssigned in sequence
 * on Apply. If one mutation fails, stops and surfaces the error — the
 * user can re-open the dialog to retry from the new RTA state.
 */
export function QuickAssignDialog({
  summary,
  mode,
  onClose,
}: {
  summary: MonthlySummaryDto
  mode: QuickAssignMode
  onClose: () => void
}) {
  const {year, month} = useAppSelector(s => s.budget)
  const [setAssigned] = useSetAssignedAmountMutation()
  const [applying, setApplying] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  const plan = planAllocations(summary, mode)
  const totalAdd = plan.reduce((s, a) => s + a.add, 0)

  const title = mode === 'targets' ? 'Fill targets first' : 'Equally to envelopes'
  const subtitle = mode === 'targets'
    ? 'Distributes Ready-to-Assign into envelopes with targets — by-date deadlines first.'
    : 'Splits Ready-to-Assign evenly across every visible envelope.'

  const onApply = async () => {
    setErr(null)
    setApplying(true)
    try {
      for (const a of plan) {
        await setAssigned({
          categoryId: a.cat.categoryId,
          year, month,
          amount: a.newAssigned,
        }).unwrap()
      }
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    } finally {
      setApplying(false)
    }
  }

  return (
    <div
      className="budget-modal-overlay"
      onClick={(e) => { if (e.target === e.currentTarget && !applying) onClose() }}
      data-testid="bdg-quick-assign-dialog"
    >
      <div className="budget-modal">
        <h3>{title}</h3>
        <div className="subtitle">{subtitle}</div>

        {plan.length === 0 ? (
          <div className="bdg-qa-empty">
            {mode === 'targets'
              ? 'No envelopes have an unfilled target. Set a monthly or by-date target on an envelope to use this action.'
              : 'No envelopes available to split into.'}
          </div>
        ) : (
          <>
            <div className="bdg-qa-list">
              {plan.map(a => (
                <div key={a.cat.categoryId} className="bdg-qa-row">
                  <span className="bdg-qa-emoji">{a.cat.emoji ?? '•'}</span>
                  <span className="bdg-qa-name">{a.cat.name}</span>
                  <span className="bdg-qa-add">+{formatTHB(a.add)}</span>
                </div>
              ))}
            </div>
            <div className="bdg-qa-total">
              <span>Total distributed</span>
              <strong>{formatTHB(totalAdd)}</strong>
            </div>
          </>
        )}

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button
            type="button"
            variant={Variant.Outlined}
            color={Color.Secondary}
            onClick={onClose}
            disabled={applying}
          >Cancel</Button>
          <Button
            type="button"
            variant={Variant.Filled}
            color={Color.Primary}
            onClick={onApply}
            disabled={applying || plan.length === 0}
          >
            {applying ? '…' : 'Apply'}
          </Button>
        </div>
      </div>
    </div>
  )
}
