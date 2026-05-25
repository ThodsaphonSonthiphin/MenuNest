import {formatTHB} from '../BudgetPage.hooks'
import type {MonthlySummaryDto} from '../../../shared/api/api'

/**
 * Hero card showing Ready-to-Assign at the top of /budget. The colour
 * shifts to a red gradient when readyToAssign < 0 (over-assigned).
 * Tapping the hero invokes onClick (used to open SetIncomeDialog).
 */
export function RtaHero({
  summary,
  onClick,
}: {
  summary: MonthlySummaryDto
  onClick?: () => void
}) {
  const negative = summary.readyToAssign < 0
  const zero = summary.readyToAssign === 0
  return (
    <button
      type="button"
      className={`bdg-rta-hero ${negative ? 'is-negative' : ''}`}
      data-testid="bdg-rta-hero"
      onClick={onClick}
    >
      <span className="bdg-rta-edit-icon" aria-hidden>✎</span>
      <div className="bdg-rta-label">
        {zero ? 'All Money Assigned' : negative ? 'Over-Assigned' : 'Ready to Assign'}
      </div>
      <div className="bdg-rta-amount" data-testid="bdg-rta-amount">
        {formatTHB(summary.readyToAssign)}
      </div>
      <div className="bdg-rta-sub">
        {formatTHB(summary.income)} income · {formatTHB(summary.totalAssigned)} assigned
      </div>
    </button>
  )
}
