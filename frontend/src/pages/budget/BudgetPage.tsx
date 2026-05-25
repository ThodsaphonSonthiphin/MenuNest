import {useState} from 'react'
import {useAppDispatch, useAppSelector} from '../../store'
import {MonthStrip} from './components/MonthStrip'
import {RtaHero} from './components/RtaHero'
import {AccountsStrip} from './components/AccountsStrip'
import {EnvelopeList} from './components/EnvelopeList'
import {SetIncomeDialog} from './components/SetIncomeDialog'
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
  const [incomeOpen, setIncomeOpen] = useState(false)
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
      <RtaHero summary={summary} onClick={() => setIncomeOpen(true)} />
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
      {incomeOpen && (
        <SetIncomeDialog
          currentAmount={summary.income}
          onClose={() => setIncomeOpen(false)}
        />
      )}
    </div>
  )
}
