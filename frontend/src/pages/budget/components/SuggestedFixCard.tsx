import {useState} from 'react'
import {useAppSelector} from '../../../store'
import {
  useSetAssignedAmountMutation,
  type MonthlySummaryDto,
  type EnvelopeDto,
} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

/**
 * Renders ONLY when `summary.readyToAssign < 0` (over-assigned).
 * Finds the envelope with the most spare cash and offers a one-tap
 * "Pull ฿X back" action — sets the candidate's assigned amount to
 * `(assigned − overage)` via the existing setAssigned mutation. No
 * new backend.
 *
 * If no envelope has spare cash (all available ≤ 0), renders a quiet
 * hint instead of a clickable card so the user knows nothing can be
 * pulled automatically.
 */
export function SuggestedFixCard({summary}: {summary: MonthlySummaryDto}) {
  const {year, month} = useAppSelector(s => s.budget)
  const [setAssigned, {isLoading}] = useSetAssignedAmountMutation()
  const [err, setErr] = useState<string | null>(null)

  if (summary.readyToAssign >= 0) return null

  const overage = Math.abs(summary.readyToAssign)
  const candidate: EnvelopeDto | undefined = summary.groups
    .flatMap(g => g.categories)
    .filter(c => !c.isHidden && c.available > 0)
    .sort((a, b) => b.available - a.available)[0]

  if (!candidate) {
    return (
      <div className="bdg-fix-card bdg-fix-card--empty" data-testid="bdg-fix-card">
        <div className="bdg-fix-card-label">Suggested fix</div>
        <div className="bdg-fix-card-empty">
          No envelope has spare cash to pull from. Edit an envelope's Assigned amount manually to rebalance.
        </div>
      </div>
    )
  }

  // Only pull what the candidate actually has spare; user may need to
  // tap a second time if a single envelope can't cover the overage.
  const pullAmount = Math.min(overage, candidate.available)
  const newAssigned = candidate.assigned - pullAmount

  const onApply = async () => {
    setErr(null)
    try {
      await setAssigned({
        categoryId: candidate.categoryId,
        year, month,
        amount: newAssigned,
      }).unwrap()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  }

  return (
    <div className="bdg-fix-card" data-testid="bdg-fix-card">
      <div className="bdg-fix-card-label">Suggested fix</div>
      <div className="bdg-fix-card-row">
        <div className="bdg-fix-card-target">
          <span className="bdg-fix-card-emoji">{candidate.emoji ?? '•'}</span>
          <span className="bdg-fix-card-name">{candidate.name}</span>
          <span className="bdg-fix-card-spare">{formatTHB(candidate.available)} spare</span>
        </div>
        <button
          type="button"
          className="bdg-fix-card-btn"
          onClick={onApply}
          disabled={isLoading}
          data-testid="bdg-fix-card-apply"
        >
          {isLoading ? '…' : `Pull ${formatTHB(pullAmount)} back`}
        </button>
      </div>
      {err && <p className="bdg-fix-card-error">{err}</p>}
    </div>
  )
}
