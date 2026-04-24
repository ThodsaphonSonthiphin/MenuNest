import type {MonthlySummaryDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'

export function MonthlySummaryPanel({summary, inDrawer = false}: {summary: MonthlySummaryDto; inDrawer?: boolean}) {
  return (
    <aside className={inDrawer ? '' : 'budget-summary-panel'}>
      <div className="budget-summary-section">
        <div className="budget-summary-title">{summary.year}-{String(summary.month).padStart(2, '0')} Summary</div>
        <div className="budget-summary-row"><span className="label">Income</span><span className="val">{formatTHB(summary.income)}</span></div>
        <div className="budget-summary-row"><span className="label">Left Over from Last Month</span><span className="val blue">{formatTHB(summary.leftOverFromLastMonth)}</span></div>
        <div className="budget-summary-row"><span className="label">Assigned</span><span className="val">{formatTHB(summary.totalAssigned)}</span></div>
        <div className="budget-summary-row"><span className="label">Activity</span><span className="val red">{formatTHB(summary.totalActivity)}</span></div>
        <div style={{borderTop: '1px solid var(--border)', margin: '10px 0'}} />
        <div className="budget-summary-row"><span className="label" style={{color: '#ccc', fontWeight: 700}}>Available</span><span className="val green" style={{fontSize: 15}}>{formatTHB(summary.available)}</span></div>
      </div>

      <div className="budget-summary-section">
        <div className="budget-summary-title">Ready to Assign</div>
        <div className="budget-summary-row">
          <span className="label">This month</span>
          <span className={`val ${summary.readyToAssign < 0 ? 'red' : 'green'}`} style={{fontSize: 16}}>
            {formatTHB(summary.readyToAssign)}
          </span>
        </div>
      </div>
    </aside>
  )
}
