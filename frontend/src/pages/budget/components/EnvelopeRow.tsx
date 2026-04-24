import {useEffect, useState} from 'react'
import {useSetAssignedAmountMutation, type EnvelopeDto} from '../../../shared/api/api'
import {formatTHB} from '../BudgetPage.hooks'
import {useAppSelector} from '../../../store'

/**
 * Single envelope row — renders category name, inline-editable assigned input,
 * monthly activity, and an availability pill. Overspent rows surface a red
 * "Cover overspending" action; healthy rows offer "Move money" instead.
 *
 * Inline edit flow: local `value` state mirrors the prop; on blur we fire
 * `setAssigned` only if the number changed. The `useEffect` resync keeps the
 * input consistent after the mutation resolves and the parent re-renders with
 * a fresh `cat.assigned`.
 */
export function EnvelopeRow({cat, onMove, onCover}: {
  cat: EnvelopeDto
  onMove: (c: EnvelopeDto) => void
  onCover: (c: EnvelopeDto) => void
}) {
  const {year, month} = useAppSelector(s => s.budget)
  const [setAssigned] = useSetAssignedAmountMutation()
  const [value, setValue] = useState<number>(cat.assigned)
  useEffect(() => {
    setValue(cat.assigned)
  }, [cat.assigned])

  const commit = () => {
    if (value !== cat.assigned) {
      setAssigned({categoryId: cat.categoryId, year, month, amount: value})
    }
  }

  const overspent = cat.available < 0
  const pillClass =
    cat.available < 0 ? 'red' :
    cat.available === 0 ? 'zero' :
    cat.targetType !== 'None' && cat.targetProgressFraction !== null && cat.targetProgressFraction < 1 ? 'orange' :
    'green'

  const progressColor =
    pillClass === 'red' ? 'var(--red)' :
    pillClass === 'orange' ? 'var(--orange)' :
    pillClass === 'green' ? 'var(--green)' :
    'var(--border)'
  const pct = Math.round((cat.targetProgressFraction ?? 0) * 100)

  return (
    <tr className={`budget-cat-row ${overspent ? 'overspent' : ''}`}>
      <td data-label="Category">
        <div className="budget-cat-name">
          <span className="budget-cat-emoji">{cat.emoji ?? '•'}</span>
          <div style={{minWidth: 0}}>
            <div>{cat.name}</div>
            {cat.targetHint && (
              <div className={`budget-cat-target ${overspent ? 'overspent' : 'urgent'}`}>
                {cat.targetHint}
              </div>
            )}
            <div className="budget-cat-progress">
              <div
                className="budget-cat-progress-fill"
                style={{width: `${pct}%`, background: progressColor}}
              />
            </div>
          </div>
        </div>
      </td>
      <td data-label="Assigned" className="right">
        <input
          className="budget-assigned-input"
          type="number"
          step="0.01"
          value={value}
          onChange={e => setValue(Number(e.target.value))}
          onBlur={commit}
          onKeyDown={e => {
            if (e.key === 'Enter') (e.target as HTMLInputElement).blur()
          }}
        />
      </td>
      <td data-label="Activity" className="right" style={{color: cat.activity < 0 ? 'var(--red)' : undefined}}>
        {formatTHB(cat.activity)}
      </td>
      <td data-label="Available" className="right">
        <span className={`budget-avail-pill ${pillClass}`}>{formatTHB(cat.available)}</span>
        <div className="budget-row-actions">
          {overspent
            ? <button type="button" className="budget-row-btn danger" onClick={() => onCover(cat)}>Cover overspending</button>
            : <button type="button" className="budget-row-btn" onClick={() => onMove(cat)}>Move money</button>}
        </div>
      </td>
    </tr>
  )
}
