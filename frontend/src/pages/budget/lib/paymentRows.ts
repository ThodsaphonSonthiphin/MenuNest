import type {BudgetAccountType, BudgetTransactionDto} from '../../../shared/api/api'
import {payActionWord} from './paymentLabel'

export interface PlainTxRow {
  kind: 'transaction'
  key: string
  tx: BudgetTransactionDto
}

export interface PaymentTxRow {
  kind: 'payment'
  key: string
  paymentId: string
  /** In the order they arrived — one leg, or both. */
  legs: BudgetTransactionDto[]
  /** The NEGATIVE leg: the account the money came from. */
  fromLeg: BudgetTransactionDto | null
  /** The POSITIVE leg: the debt account being paid. */
  toLeg: BudgetTransactionDto | null
  /** The payment's magnitude — always positive. */
  amount: number
  date: string
  notes: string | null
  /** Both legs visible. Only then can the payment be edited from this feed. */
  complete: boolean
}

export type TxRow = PlainTxRow | PaymentTxRow

/**
 * Correction #3 / menunest-209: a payment is ONE row to the user. Its two
 * `BudgetTransaction`s share a non-null `paymentId`, and the backend refuses
 * `PUT`/`DELETE` on either leg on its own — so a feed that renders them as two
 * ordinary rows offers two Edit buttons and two Delete buttons, all four of
 * which fail. Grouping is what makes the pair reachable through
 * `PUT`/`DELETE /api/budget/payments/{paymentId}`.
 *
 * The grouped row takes the position of its FIRST leg, so the feed's existing
 * ordering is untouched.
 *
 * An account-detail feed is filtered to one account and therefore only ever
 * sees one leg. That still becomes a payment row — marked `complete: false`,
 * because an edit needs both sides — never an ordinary row.
 */
export function groupPaymentLegs(items: BudgetTransactionDto[]): TxRow[] {
  const rows: TxRow[] = []
  const byPaymentId = new Map<string, PaymentTxRow>()

  for (const tx of items) {
    if (tx.paymentId === null || tx.paymentId === undefined) {
      rows.push({kind: 'transaction', key: tx.id, tx})
      continue
    }

    const existing = byPaymentId.get(tx.paymentId)
    if (existing) {
      existing.legs.push(tx)
      absorb(existing, tx)
      continue
    }

    const row: PaymentTxRow = {
      kind: 'payment',
      key: tx.paymentId,
      paymentId: tx.paymentId,
      legs: [tx],
      fromLeg: null,
      toLeg: null,
      amount: Math.abs(tx.amount),
      date: tx.date,
      notes: tx.notes,
      complete: false,
    }
    absorb(row, tx)
    byPaymentId.set(tx.paymentId, row)
    rows.push(row)
  }

  return rows
}

function absorb(row: PaymentTxRow, leg: BudgetTransactionDto): void {
  if (leg.amount < 0) row.fromLeg = leg
  else row.toLeg = leg
  // The positive leg carries the canonical magnitude; either leg's absolute
  // value is the same number, so a lone leg is just as good.
  row.amount = Math.abs(leg.amount)
  row.notes = row.notes ?? leg.notes
  row.complete = row.fromLeg !== null && row.toLeg !== null
}

/**
 * The two lines a payment row shows. The action word follows the ACCOUNT being
 * paid (menunest-212) — pass its type from the summary; pass `null` when the
 * feed cannot see that account (a lone outflow leg), and no word is guessed.
 *
 * The subtitle is what tells the user why a lone leg cannot be edited here.
 */
export function paymentRowLabel(
  row: PaymentTxRow,
  toAccountType: BudgetAccountType | null,
): {title: string; subtitle: string} {
  const paidName = row.toLeg?.accountName ?? null
  const action = toAccountType === null
    ? 'ชำระหนี้'
    : paidName === null
      ? payActionWord(toAccountType)
      : `${payActionWord(toAccountType)} ${paidName}`

  const subtitle = row.complete && row.fromLeg && row.toLeg
    ? `Payment · ${row.fromLeg.accountName} → ${row.toLeg.accountName}`
    : row.toLeg !== null
      ? 'Payment · other half is on the paying account'
      : 'Payment · other half is on the account being paid'

  return {title: row.notes ?? action, subtitle}
}
