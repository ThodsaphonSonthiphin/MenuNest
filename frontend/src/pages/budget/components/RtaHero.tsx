import {formatTHB} from '../BudgetPage.hooks'
import type {MonthlySummaryDto} from '../../../shared/api/api'

const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

/**
 * Ready-to-Assign hero — calm, minimal card design.
 *
 * Single neutral slate panel across all three states. A small colored
 * pill at the top names the state; a thin colored progress bar at the
 * bottom shows assigned/income ratio. The amount stays neutral white
 * across states so the eye reads value, not chrome.
 *
 *   has-money  amber accent  — "Still to place"
 *   zero       emerald accent — "Every baht has a job"
 *   over       red accent     — "Too much assigned"
 *
 * Display-only; the assign / spend flows live elsewhere.
 */
export function RtaHero({summary}: {summary: MonthlySummaryDto}) {
  const rta = summary.readyToAssign
  const state: 'has-money' | 'zero' | 'over' =
    rta > 0 ? 'has-money' : rta === 0 ? 'zero' : 'over'

  const stateLabel =
    state === 'zero' ? 'Every baht has a job' :
    state === 'over' ? 'Too much assigned' :
    'Still to place'

  const contextLine =
    state === 'zero'
      ? `${formatTHB(summary.income)} fully placed.`
      : state === 'over'
      ? `Pull ${formatTHB(Math.abs(rta))} back to rebalance.`
      : summary.income > 0
      ? `${formatTHB(summary.totalAssigned)} of ${formatTHB(summary.income)} placed.`
      : `${formatTHB(rta)} available to assign.`

  const pctRaw = summary.income <= 0 ? 0 : (summary.totalAssigned / summary.income) * 100
  const pctClamped = state === 'over' ? 100 : Math.min(100, Math.max(0, pctRaw))
  const pctLabel = Math.round(pctClamped)

  return (
    <section className={`bdg-rta-hero is-${state}`} data-testid="bdg-rta-hero">
      <div className="bdg-rta-topline">
        <span className="bdg-rta-month">{MONTHS[summary.month - 1]} {summary.year}</span>
        <span className="bdg-rta-state-pill">{stateLabel}</span>
      </div>

      <div className="bdg-rta-amount" data-testid="bdg-rta-amount">
        {formatTHB(rta)}
      </div>

      <div className="bdg-rta-context">{contextLine}</div>

      <div
        className="bdg-rta-progress"
        data-testid="bdg-rta-progress"
        aria-label={`${pctLabel} percent of income placed`}
      >
        <div className="bdg-rta-progress-track">
          <div className="bdg-rta-progress-fill" style={{width: `${pctClamped}%`}} />
        </div>
        <span className="bdg-rta-progress-pct">{pctLabel}%</span>
      </div>
    </section>
  )
}
