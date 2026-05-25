import {formatTHB} from '../BudgetPage.hooks'
import {useEnvelopeCard, type UseEnvelopeCardArgs} from './EnvelopeCard.hooks'

export function EnvelopeCard(props: UseEnvelopeCardArgs) {
  const {cat} = props
  const h = useEnvelopeCard(props)

  const overspent = cat.available < 0
  const zero = cat.available === 0
  const pillClass =
    overspent ? 'is-red' :
    zero ? 'is-zero' :
    cat.targetType !== 'None' && cat.targetProgressFraction !== null && cat.targetProgressFraction < 1 ? 'is-orange' :
    'is-green'

  const pct = Math.round((cat.targetProgressFraction ?? 0) * 100)
  const progressClass = overspent ? 'is-red' : 'is-green'

  return (
    <div
      className={`bdg-env-card ${overspent ? 'is-overspent' : ''} ${h.expanded ? 'is-expanded' : ''}`}
      data-testid="bdg-envelope-card"
      data-category-id={cat.categoryId}
      onClick={h.onTap}
      onPointerDown={h.onPointerDown}
      onPointerMove={h.onPointerMove}
      onPointerUp={h.onPointerUp}
      onPointerCancel={h.onPointerCancel}
      role="button"
      tabIndex={0}
    >
      <div className="bdg-env-row1">
        <div className="bdg-env-name">
          <span className="bdg-env-emoji">{cat.emoji ?? '•'}</span>
          {cat.name}
        </div>
        <div className="bdg-env-row1-right">
          {!h.expanded && overspent && (
            <button
              type="button"
              className="bdg-env-icon-btn is-danger"
              onClick={(e) => { e.stopPropagation(); h.onCoverOverspending() }}
              aria-label="Cover overspending"
              data-testid="bdg-env-cover-icon"
            >⚠</button>
          )}
          {!h.expanded && !overspent && (
            <button
              type="button"
              className="bdg-env-icon-btn"
              onClick={(e) => { e.stopPropagation(); h.onMoveMoney() }}
              aria-label="Move money"
              data-testid="bdg-env-move-icon"
            >⇄</button>
          )}
          <span className={`bdg-env-pill ${pillClass}`}>{formatTHB(cat.available)}</span>
        </div>
      </div>
      <div className="bdg-env-row2">
        <span>{cat.targetHint ?? `Activity ${formatTHB(cat.activity)}`}</span>
        <span>{cat.assigned > 0 ? `Assigned ${formatTHB(cat.assigned)}` : 'Unassigned'}</span>
      </div>
      <div className="bdg-env-progress">
        <div className={`bdg-env-progress-fill ${progressClass}`} style={{width: `${pct}%`}} />
      </div>

      {h.expanded && (
        <div className="bdg-env-expanded" onClick={(e) => e.stopPropagation()}>
          <div className="bdg-env-assigned-row">
            <span className="bdg-env-assigned-label">Assigned this month</span>
            <input
              className="bdg-env-assigned-input"
              type="number"
              step="0.01"
              value={h.assignedDraft}
              onChange={(e) => h.setAssignedDraft(Number(e.target.value))}
              onBlur={h.commitAssigned}
              onKeyDown={(e) => {
                if (e.key === 'Enter') (e.target as HTMLInputElement).blur()
                if (e.key === 'Escape') h.revertAssigned()
              }}
              data-testid="bdg-env-assigned-input"
            />
          </div>
          <div className="bdg-env-meta">
            <span>Activity: <span className="val">{formatTHB(cat.activity)}</span></span>
            <span>Available: <span className="val">{formatTHB(cat.available)}</span></span>
          </div>
          <div className="bdg-env-actions">
            <button
              type="button"
              className="bdg-env-action is-primary"
              onClick={h.onAddTransaction}
              data-testid="bdg-env-add-tx"
            >+ Transaction</button>
            <button
              type="button"
              className="bdg-env-action"
              onClick={h.onMoveMoney}
            >⇄ Move</button>
            <button
              type="button"
              className="bdg-env-action"
              disabled
              title="Editing categories is a Phase-2 feature"
              style={{opacity: 0.5, cursor: 'not-allowed'}}
            >✎ Edit (soon)</button>
            {overspent && (
              <button
                type="button"
                className="bdg-env-action is-danger"
                onClick={h.onCoverOverspending}
              >⚠ Cover</button>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
