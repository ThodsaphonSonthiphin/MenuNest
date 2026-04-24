import {useAppDispatch, useAppSelector} from '../../store'
import {setAccountsOpen, setSummaryOpen} from './budgetSlice'
import {AccountsSidebar} from './components/AccountsSidebar'
import {EnvelopeTable} from './components/EnvelopeTable'
import {MonthlySummaryPanel} from './components/MonthlySummaryPanel'
import {useBudgetData, useBudgetLayout, formatTHB} from './BudgetPage.hooks'
import {goPrevMonth, goNextMonth, setFilter} from './budgetSlice'
import type {BudgetFilter} from './budgetSlice'
import './BudgetPage.css'

const MONTHS = ['January','February','March','April','May','June','July','August','September','October','November','December']

export function BudgetPage() {
  const layout = useBudgetLayout()
  const dispatch = useAppDispatch()
  const {year, month, summary, isLoading} = useBudgetData()
  const {filter, accountsOpen, summaryOpen} = useAppSelector(s => s.budget)
  const overspentCount = summary?.groups.flatMap(g => g.categories).filter(c => c.available < 0).length ?? 0

  if (isLoading || !summary) return <div style={{padding: 40, color: '#888'}}>Loading budget…</div>

  return (
    <div className="budget-page">
      <AccountsSidebar accounts={summary.accounts} />

      <div className="budget-main">
        {layout !== 'desktop' && (
          <div className="budget-mobile-bar">
            <button onClick={() => dispatch(setAccountsOpen(true))} aria-label="Accounts">🏦</button>
            <div style={{flex: 1}} />
            <button onClick={() => dispatch(setSummaryOpen(true))} aria-label="Summary">📊</button>
          </div>
        )}

        <div className="budget-month-strip">
          <button onClick={() => dispatch(goPrevMonth())}>‹</button>
          <span className="label">{MONTHS[month - 1]} {year}</span>
          <button onClick={() => dispatch(goNextMonth())}>›</button>
          <div className="budget-rta">
            <div>
              <div className={`budget-rta-amount ${summary.readyToAssign < 0 ? 'negative' : ''}`}>
                {formatTHB(summary.readyToAssign)}
              </div>
              <div className="budget-rta-label">
                {summary.readyToAssign === 0 ? 'All Money Assigned'
                 : summary.readyToAssign > 0 ? 'Ready to Assign' : 'Over-Assigned'}
              </div>
            </div>
          </div>
        </div>

        <div className="budget-filters">
          {([
            ['all', 'All', false],
            ['overspent', `⚠ ${overspentCount} Overspent`, true],
            ['underfunded', 'Underfunded', false],
            ['overfunded', 'Overfunded', false],
            ['available', 'Money Available', false],
            ['snoozed', 'Snoozed', false],
          ] as [BudgetFilter, string, boolean][]).map(([k, label, danger]) => (
            <div
              key={k}
              className={`budget-chip ${filter === k ? 'active' : ''} ${danger && overspentCount > 0 ? 'danger' : ''}`}
              onClick={() => dispatch(setFilter(k))}
            >{label}</div>
          ))}
        </div>

        <EnvelopeTable summary={summary} />
      </div>

      <MonthlySummaryPanel summary={summary} />

      {layout !== 'desktop' && accountsOpen && (
        <>
          <div className="budget-drawer-backdrop" onClick={() => dispatch(setAccountsOpen(false))} />
          <aside className="budget-drawer budget-drawer--left">
            <AccountsSidebar accounts={summary.accounts} inDrawer />
          </aside>
        </>
      )}
      {layout !== 'desktop' && summaryOpen && (
        <>
          <div className="budget-drawer-backdrop" onClick={() => dispatch(setSummaryOpen(false))} />
          <aside className="budget-drawer budget-drawer--right">
            <MonthlySummaryPanel summary={summary} inDrawer />
          </aside>
        </>
      )}
    </div>
  )
}
