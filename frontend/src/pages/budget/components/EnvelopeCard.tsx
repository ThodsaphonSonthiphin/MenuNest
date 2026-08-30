import {formatTHB} from '../BudgetPage.hooks'
import {payButtonLabel, paymentPillTone, paymentProgress, shortfallLine} from '../lib/paymentLabel'
import {useEnvelopeCard, type UseEnvelopeCardArgs} from './EnvelopeCard.hooks'

export function EnvelopeCard(props: UseEnvelopeCardArgs) {
  const {cat} = props
  const h = useEnvelopeCard(props)

  const overspent = cat.available < 0
  const zero = cat.available === 0
  // menunest-202: on a Payment envelope the pill follows the SHORTFALL — the
  // mock draws the same positive ฿500 green when the card is covered and
  // orange when ฿20,000 is still owed. The shared rule below keys off a
  // target, and a Payment envelope has none.
  const pillClass = h.isPayment
    ? paymentPillTone(cat.available, cat.shortfall)
    : overspent ? 'is-red' :
      zero ? 'is-zero' :
      cat.targetType !== 'None' && cat.targetProgressFraction !== null && cat.targetProgressFraction < 1 ? 'is-orange' :
      'is-green'

  // menunest-202: a Payment envelope has no target, so the shared
  // target-progress bar would sit flat at 0% beside a card reading จ่ายเต็มได้.
  // It tracks funded-against-owed instead (lib/paymentLabel `paymentProgress`).
  const payProgress = h.isPayment && h.payAccount
    ? paymentProgress(h.payAccount.balance, cat.available)
    : null
  const pct = payProgress ? payProgress.pct : Math.round((cat.targetProgressFraction ?? 0) * 100)
  const progressClass = overspent
    ? 'is-red'
    : payProgress
      ? (payProgress.tone === 'short' ? 'is-orange' : 'is-green')
      : 'is-green'

  const shortfall = shortfallLine(cat.shortfall)

  return (
    <div
      className={`bdg-env-card ${overspent ? 'is-overspent' : ''} ${h.expanded ? 'is-expanded' : ''} ${h.isPayment ? 'is-payment' : ''}`}
      data-testid="bdg-envelope-card"
      data-category-id={cat.categoryId}
      data-payment-for={cat.paymentForAccountId ?? undefined}
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
          {/* menunest-205: a Payment envelope can never be an Everyday
              envelope, so the dot is not merely absent — it is impossible. */}
          {cat.isEveryday && !h.isPayment && (
            <span className="bdg-env-everyday-dot" data-testid="bdg-env-everyday-dot" aria-label="Everyday envelope" />
          )}
          <span className="bdg-env-name-text">{cat.name}</span>
        </div>
        <div className="bdg-env-row1-right">
          {/* menunest-204: no ＋ on a Payment envelope — a plain transaction is
              exactly the write it forbids. The payment action lives below. */}
          {!h.expanded && !h.isPayment && (
            <button
              type="button"
              className="bdg-env-icon-btn"
              onClick={(e) => { e.stopPropagation(); h.onAddTransaction() }}
              aria-label="Add transaction"
              data-testid="bdg-env-add-icon"
            >＋</button>
          )}
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
      {/* Spec §4.3 — on a Payment envelope row 2 IS the shortfall line: the
          card's balance against the money set aside for it. */}
      {h.isPayment && shortfall ? (
        <div className="bdg-env-row2" data-testid="bdg-env-shortfall">
          <span>{h.payAccount ? `ยอดบัตร ${formatTHB(h.payAccount.balance)}` : 'ยอดบัตร —'}</span>
          <span><b className={shortfall.tone === 'short' ? 'short' : undefined}>{shortfall.text}</b></span>
        </div>
      ) : (
        <div className="bdg-env-row2">
          <span>{cat.targetHint ?? `Activity ${formatTHB(cat.activity)}`}</span>
          <span>{cat.assigned > 0 ? `Assigned ${formatTHB(cat.assigned)}` : 'Unassigned'}</span>
        </div>
      )}
      <div className="bdg-env-progress">
        <div className={`bdg-env-progress-fill ${progressClass}`} style={{width: `${pct}%`}} />
      </div>

      {h.expanded && (
        <div className="bdg-env-expanded" onClick={(e) => e.stopPropagation()}>
          {/* menunest-205: the assigned input STAYS on a Payment envelope —
              funding it by hand is the only way to pay down debt that predates
              the budget. */}
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
          {h.isPayment ? (
            // R-1: on a Payment envelope `assigned + activity` does not explain
            // the change in `available` — a categorised card purchase moves
            // `available` while both stay 0. `cardSpending` (รูดบัตร) is the
            // display term that does.
            <div className="bdg-env-meta">
              <span>รูดบัตร <span className="val">{formatTHB(cat.cardSpending ?? 0)}</span></span>
              <span>คงเหลือ <span className="val">{formatTHB(cat.available)}</span></span>
            </div>
          ) : (
            <div className="bdg-env-meta">
              <span>Activity: <span className="val">{formatTHB(cat.activity)}</span></span>
              <span>Available: <span className="val">{formatTHB(cat.available)}</span></span>
            </div>
          )}
          <div className="bdg-env-actions">
            {h.isPayment ? (
              <button
                type="button"
                className="bdg-env-action is-primary"
                onClick={h.onPay}
                disabled={!h.payAccount}
                data-testid="bdg-env-pay"
              >{payButtonLabel(h.payAccount?.type ?? 'Credit')}</button>
            ) : (
              <button
                type="button"
                className="bdg-env-action is-primary"
                onClick={h.onAddTransaction}
                data-testid="bdg-env-add-tx"
              >+ Transaction</button>
            )}
            <button
              type="button"
              className="bdg-env-action"
              onClick={h.onMoveMoney}
            >⇄ Move</button>
            {/* menunest-205: rename, move group, delete and hide are all
                refused on a Payment envelope — its name follows the Account. */}
            <button
              type="button"
              className={`bdg-env-action ${h.isPayment ? 'is-off' : ''}`}
              disabled
              title={h.isPayment
                ? 'ชื่อซองตามชื่อบัญชี — แก้ไม่ได้'
                : 'Editing categories is a Phase-2 feature'}
            >{h.isPayment ? '✎ Edit' : '✎ Edit (soon)'}</button>
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
