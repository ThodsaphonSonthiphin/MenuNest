import {formatTHB} from '../BudgetPage.hooks'
import {formatPaceLine} from '../lib/paceLine'
import {formatFreezeLine} from '../lib/freezeLine'
import type {DailyAllowanceDto} from '../../../shared/api/api'

// Viewer-local 'YYYY-MM-DD', built from Date's local getters (never
// `toISOString`/UTC) — same pattern as AccountTransactionList.tsx's
// `todayIso()`, kept local to each file rather than shared since it's a
// three-line glue call, not decision logic (the actual logic that needed a
// lib/ module + tests is formatFreezeLine itself).
function todayIso(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * The frozen "you can spend this much today" figure (menunest-181).
 *
 * Three states, all in one component:
 *   1. `dailyAllowance === null` — the requested month isn't the real
 *      current month (menunest-185). Render nothing at all, not a
 *      placeholder.
 *   2. `hasMarks === false` — no everyday envelope has been marked yet.
 *      An invitation to pick some, never a number (menunest-181).
 *   3. otherwise — the frozen amount, the freeze line (when it froze +
 *      "won't change if you spend more today", via `formatFreezeLine`),
 *      and the pace line (menunest-186) when it has something to say. The
 *      pace line is the ONLY part of this card that reacts to spending;
 *      it counts completed days only, so it renders nothing on the
 *      freeze day itself. `frozenOn` is NOT always today — it only moves
 *      on a Budgeting event or a month rollover, so it can lag behind by
 *      days within the same month.
 *
 * The whole card is tappable in every rendered state and opens the
 * everyday-marks picker (Task 7) via `onOpenMarks` — this component only
 * wires the callback, it does not own or render that sheet.
 */
export function DailyAllowanceCard({
  dailyAllowance,
  onOpenMarks,
}: {
  dailyAllowance: DailyAllowanceDto | null
  onOpenMarks: () => void
}) {
  if (dailyAllowance === null) return null

  if (!dailyAllowance.hasMarks) {
    return (
      <button
        type="button"
        className="bdg-allowance-hero"
        data-testid="bdg-daily-allowance-empty"
        onClick={onOpenMarks}
      >
        <div className="bdg-allowance-topline">
          <span className="bdg-allowance-eyebrow">Today's allowance</span>
        </div>
        <div className="bdg-allowance-empty-title">No everyday envelopes marked yet</div>
        <div className="bdg-allowance-empty-hint">
          Pick which envelopes are your everyday spending to see what's left today.
        </div>
      </button>
    )
  }

  const paceLine = formatPaceLine(dailyAllowance.paceDelta)
  const paceState = dailyAllowance.paceDelta > 0 ? 'over' : 'under'

  return (
    <button
      type="button"
      className="bdg-allowance-hero"
      data-testid="bdg-daily-allowance"
      onClick={onOpenMarks}
    >
      <div className="bdg-allowance-topline">
        <span className="bdg-allowance-eyebrow">Today's allowance</span>
      </div>

      <div className="bdg-allowance-amount">{formatTHB(dailyAllowance.amount)}</div>

      {paceLine && (
        <div className={`bdg-pace-line ${paceState}`}>
          <span aria-hidden="true">{paceState === 'over' ? '▼' : '▲'}</span>
          {paceLine}
        </div>
      )}

      <div className="bdg-allowance-freeze">{formatFreezeLine(dailyAllowance.frozenOn, todayIso())}</div>
    </button>
  )
}
