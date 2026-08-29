import {useCallback, useMemo, useState} from 'react'
import {useAppDispatch, useAppSelector} from '../../store'
import {MonthStrip} from './components/MonthStrip'
import {DailyAllowanceCard} from './components/DailyAllowanceCard'
import {EverydayMarksSheet} from './components/EverydayMarksSheet'
import {RtaHero} from './components/RtaHero'
import {AccountsStrip} from './components/AccountsStrip'
import {EnvelopeList} from './components/EnvelopeList'
import {SuggestedFixCard} from './components/SuggestedFixCard'
import {QuickAssignChips} from './components/QuickAssignChips'
import {ChangeHistorySheet} from './components/ChangeHistorySheet'
import {useBudgetData} from './BudgetPage.hooks'
import {useShortcutRail} from '../../shared/hooks/useShortcutRail'
import {
  useListBudgetHistoryQuery,
  useRedoBudgetChangeMutation,
  useUndoBudgetChangeMutation,
} from '../../shared/api/api'
import {latestRedoable, latestUndoable} from './lib/latestUndoable'
import {setFilter} from './budgetSlice'
import type {BudgetFilter} from './budgetSlice'
import './BudgetPage.css'

export function BudgetPage() {
  const dispatch = useAppDispatch()
  const {summary, isLoading} = useBudgetData()
  const filter = useAppSelector(s => s.budget.filter)
  // Local-useState trigger, matching the pattern already used for every
  // other budget dialog (QuickAssignChips, AccountsStrip, TransactionDialog)
  // — no Redux precedent for dialog open/closed state in this module.
  const [marksSheetOpen, setMarksSheetOpen] = useState(false)
  const [historyOpen, setHistoryOpen] = useState(false)
  const overspentCount = summary?.groups.flatMap(g => g.categories).filter(c => c.available < 0).length ?? 0

  // ----- the shortcut rail (menunest-191/192/199) -----
  const {year, month} = useAppSelector(s => s.budget)
  const {data: history = []} = useListBudgetHistoryQuery({year, month})
  const [undoChange] = useUndoBudgetChangeMutation()
  const [redoChange] = useRedoBudgetChangeMutation()

  const undoTarget = latestUndoable(history)
  const redoTarget = latestRedoable(history)

  const onUndo = useCallback(() => {
    if (undoTarget) void undoChange({id: undoTarget.id, year, month})
  }, [undoTarget, undoChange, year, month])

  const onRedo = useCallback(() => {
    if (redoTarget) void redoChange({id: redoTarget.id, year, month})
  }, [redoTarget, redoChange, year, month])

  // menunest-200: ⌘ on macOS, Ctrl elsewhere. The label is hidden below desktop
  // widths in CSS, where there is no keyboard to hint about.
  const mod = useMemo(
    () => (typeof navigator !== 'undefined' && /Mac|iPhone|iPad/i.test(navigator.userAgent) ? '⌘' : 'Ctrl+'),
    [],
  )

  // menunest-191 fixes this order: undo nearest the thumb, then redo, then
  // change history. Do not reorder.
  const railDeclaration = useMemo(
    () => ({
      actions: [
        {key: 'undo', label: 'Undo', icon: '↶', hint: `${mod}Z`, disabled: !undoTarget, onPress: onUndo},
        {key: 'redo', label: 'Redo', icon: '↷', hint: `${mod}⇧Z`, disabled: !redoTarget, onPress: onRedo},
        {key: 'history', label: 'Change history', icon: '⌚', onPress: () => setHistoryOpen(true)},
      ],
    }),
    [mod, undoTarget, redoTarget, onUndo, onRedo],
  )

  useShortcutRail(railDeclaration)

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
      <AccountsStrip accounts={summary.accounts} readyToAssign={summary.readyToAssign} />

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

      {marksSheetOpen && (
        <EverydayMarksSheet groups={summary.groups} onClose={() => setMarksSheetOpen(false)} />
      )}

      {historyOpen && <ChangeHistorySheet onClose={() => setHistoryOpen(false)} />}
    </div>
  )
}
