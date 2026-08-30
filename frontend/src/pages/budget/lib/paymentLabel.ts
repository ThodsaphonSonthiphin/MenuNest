import type {BudgetAccountType} from '../../../shared/api/api'
import {formatTHB} from './formatTHB'

/**
 * menunest-212 — one action; only the word changes with the account type.
 * Resolved from the ACCOUNT, at render time, so the envelope card, the
 * transaction row and the dialog title can never disagree about it.
 */
export function payActionWord(type: BudgetAccountType): string {
  return type === 'Loan' ? 'จ่ายค่างวด' : 'จ่ายบัตร'
}

/** The primary button on a Payment envelope's expanded card (see the mock). */
export function payButtonLabel(type: BudgetAccountType): string {
  return `฿ ${payActionWord(type)}`
}

/**
 * Spec §4.3 — the one number issue #112 asks for: "can I pay this bill?"
 *
 * `null`/`undefined` is an ordinary envelope (the backend sends the field only
 * on a Payment envelope), and renders nothing at all rather than a zero.
 */
export function shortfallLine(
  shortfall: number | null | undefined,
): {text: string; tone: 'ok' | 'short'} | null {
  if (shortfall === null || shortfall === undefined) return null
  return shortfall === 0
    ? {text: 'จ่ายเต็มได้', tone: 'ok'}
    : {text: `ขาดอีก ${formatTHB(shortfall)}`, tone: 'short'}
}

/**
 * The progress bar under a Payment envelope's rows: money set aside against
 * money owed.
 *
 * A Payment envelope has no target (`targetType` is `'None'`), so the shared
 * `targetProgressFraction` is null and the bar the other cards use would sit
 * flat at 0% next to a card reading **จ่ายเต็มได้** — the mock shows it full.
 * This is the payment-envelope substitute: funded ÷ owed, floored and capped.
 */
export function paymentProgress(
  accountBalance: number,
  available: number,
): {pct: number; tone: 'ok' | 'short'} {
  const owed = -accountBalance
  if (owed <= 0) return {pct: 100, tone: 'ok'}
  const pct = Math.min(100, Math.max(0, (available / owed) * 100))
  return {pct, tone: pct >= 100 ? 'ok' : 'short'}
}

/**
 * The money pill's colour on a Payment envelope.
 *
 * The mock draws it green on the funded card and orange on the same card
 * carrying old debt — both hold a positive ฿500, so the pill follows the
 * SHORTFALL, not the amount. The shared rule cannot say that: it keys off
 * `targetProgressFraction`, and a Payment envelope has no target.
 */
export function paymentPillTone(
  available: number,
  shortfall: number | null | undefined,
): 'is-red' | 'is-orange' | 'is-green' | 'is-zero' {
  if (available < 0) return 'is-red'
  if (shortfall !== null && shortfall !== undefined && shortfall > 0) return 'is-orange'
  return available === 0 ? 'is-zero' : 'is-green'
}
