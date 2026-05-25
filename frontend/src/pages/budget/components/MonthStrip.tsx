import {useAppDispatch, useAppSelector} from '../../../store'
import {goPrevMonth, goNextMonth} from '../budgetSlice'

const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

/**
 * Month navigator at the top of /budget. Previous / next chevrons
 * read year+month from budgetSlice and dispatch the existing month
 * actions. Stays narrow on mobile — flex-row, centered.
 */
export function MonthStrip() {
  const dispatch = useAppDispatch()
  const {year, month} = useAppSelector(s => s.budget)
  return (
    <div className="bdg-month-strip" data-testid="bdg-month-strip">
      <button
        type="button"
        className="bdg-month-arrow"
        onClick={() => dispatch(goPrevMonth())}
        aria-label="Previous month"
      >‹</button>
      <span className="bdg-month-label">{MONTHS[month - 1]} {year}</span>
      <button
        type="button"
        className="bdg-month-arrow"
        onClick={() => dispatch(goNextMonth())}
        aria-label="Next month"
      >›</button>
    </div>
  )
}
