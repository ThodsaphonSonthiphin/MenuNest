import {useState} from 'react'
import {useAppDispatch, useAppSelector} from '../../store'
import {MonthStrip} from './components/MonthStrip'
import {DailyAllowanceCard} from './components/DailyAllowanceCard'
import {RtaHero} from './components/RtaHero'
import {AccountsStrip} from './components/AccountsStrip'
import {EnvelopeList} from './components/EnvelopeList'
import {SuggestedFixCard} from './components/SuggestedFixCard'
import {QuickAssignChips} from './components/QuickAssignChips'
import {useBudgetData} from './BudgetPage.hooks'
import {setFilter} from './budgetSlice'
import type {BudgetFilter} from './budgetSlice'
import './BudgetPage.css'

export function BudgetPage() {
  const dispatch = useAppDispatch()
  const {summary, isLoading} = useBudgetData()
  const filter = useAppSelector(s => s.budget.filter)
  // The everyday-marks picker sheet is Task 7's job — this only owns the
  // trigger, matching the local-useState pattern already used for every
  // other budget dialog (QuickAssignChips, AccountsStrip, TransactionDialog).
  const [, setMarksSheetOpen] = useState(false)
  const overspentCount = summary?.groups.flatMap(g => g.categories).filter(c => c.available < 0).length ?? 0

  if (isLoading || !summary) {
    return <div className="bdg-loading">Loading budget…</div>
  }

  const chips: [BudgetFilter, string, boolean][] = [
    ['all',         'All',                              false],
    ['overspent',   `⚠ ${overspentCount} Overspent`,    true],
    ['underfunded', 'Underfunded',                      false],
    ['overfunded',  'Overfunded',                       false],
    ['available',   'Money Available',                  false],
    ['snoozed',     'Snoozed',                          false],
  ]

  return (
    <div className="bdg-page" data-testid="bdg-page">
      <MonthStrip />
      <DailyAllowanceCard
        dailyAllowance={summary.dailyAllowance}
        onOpenMarks={() => setMarksSheetOpen(true)}
      />
      <RtaHero summary={summary} />
      <SuggestedFixCard summary={summary} />
      <QuickAssignChips summary={summary} />
      <AccountsStrip accounts={summary.accounts} />

      <div className="bdg-filters">
        {chips.map(([k, label, danger]) => (
          <button
            key={k}
            type="button"
            className={`bdg-chip ${filter === k ? 'is-active' : ''} ${danger && overspentCount > 0 ? 'is-danger' : ''}`}
            onClick={() => dispatch(setFilter(k))}
          >{label}</button>
        ))}
      </div>

      <EnvelopeList summary={summary} />
    </div>
  )
}
