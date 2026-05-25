import {formatTHB} from '../BudgetPage.hooks'
import type {MonthlySummaryDto} from '../../../shared/api/api'

/**
 * Ready-to-Assign hero. Renders three visually distinct states so a
 * zero-based-budgeting user can see at a glance whether they're done
 * for the month:
 *   - has-money (orange, ⚡): readyToAssign > 0 — still assigning
 *   - zero      (green,  ✓): readyToAssign === 0 — goal reached
 *   - over      (red,   ⚠): readyToAssign < 0 — pull money back
 * Tapping the hero invokes onClick (used to open SetIncomeDialog).
 */
export function RtaHero({
  summary,
  onClick,
}: {
  summary: MonthlySummaryDto
  onClick?: () => void
}) {
  const rta = summary.readyToAssign
  const state: 'has-money' | 'zero' | 'over' =
    rta > 0 ? 'has-money' : rta === 0 ? 'zero' : 'over'

  const stateIcon = state === 'zero' ? '✓' : state === 'over' ? '⚠' : '⚡'
  const stateLabel =
    state === 'zero' ? 'Every baht has a job' :
    state === 'over' ? 'Too much assigned' :
    'Assign every baht'

  const ctaText =
    state === 'zero' ? 'Goal reached' :
    state === 'over' ? `Pull ${formatTHB(Math.abs(rta))} back` :
    '↑ Still to assign'

  // Progress = how much of income is assigned. Special-cased: 0% when
  // there's no income yet, 100% when over-assigned (bar visually full).
  const pct =
    summary.income <= 0 ? 0 :
    state === 'over' ? 100 :
    Math.min(100, Math.round((summary.totalAssigned / summary.income) * 100))

  return (
    <button
      type="button"
      className={`bdg-rta-hero is-${state}`}
      data-testid="bdg-rta-hero"
      onClick={onClick}
    >
      <span className="bdg-rta-edit-icon" aria-hidden>✎</span>
      <span className="bdg-rta-state-icon" aria-hidden>{stateIcon}</span>
      <div className="bdg-rta-label">{stateLabel}</div>
      <div className="bdg-rta-amount" data-testid="bdg-rta-amount">
        {formatTHB(rta)}
      </div>
      <div className="bdg-rta-cta">{ctaText}</div>

      <div className="bdg-rta-progress-wrap" data-testid="bdg-rta-progress">
        <div className="bdg-rta-progress-labels">
          <span>{formatTHB(summary.totalAssigned)} assigned</span>
          <span>{pct}% of {formatTHB(summary.income)}</span>
        </div>
        <div className="bdg-rta-progress-bar">
          <div className="bdg-rta-progress-fill" style={{width: `${pct}%`}} />
        </div>
      </div>
    </button>
  )
}
