import type {BudgetAccountDto, BudgetAccountType, EnvelopeGroupDto} from '../../../shared/api/api'
import {formatTHB} from './formatTHB'

export interface PickerOption {
  id: string
  label: string
}

/**
 * The accounts `PaymentDialog` may offer as the PAYER, mirroring
 * `MakePaymentHandler`'s own refusals so the user never picks something the
 * server will reject:
 *
 * - never the account being paid (paying an account from itself is refused);
 * - never a **Loan** — "A Loan account cannot be the paying account": a loan's
 *   balance is not spendable money;
 * - never a closed account.
 *
 * A **Credit** card IS offered: paying one card with another is a legal
 * balance-transfer-style move. `payingCardWarning` is what makes its
 * consequence visible.
 */
export function payingAccountOptions(
  accounts: BudgetAccountDto[],
  toAccountId: string,
): PickerOption[] {
  return accounts
    .filter(a =>
      a.id !== toAccountId &&
      !a.isClosed &&
      a.type !== 'Loan' &&
      a.type !== 'Closed')
    .map(a => ({id: a.id, label: `${a.name} (${formatTHB(a.balance)})`}))
}

/**
 * menunest-214 / `PaymentCategoryRule`: the funding Envelope is REQUIRED when
 * paying a **Loan** (a Loan has no Payment envelope, so the Envelope is the
 * only thing a loan payment ever spends) and REFUSED when paying a **Credit**
 * card (its Payment envelope already falls by derivation; categorising the
 * outflow leg too would double-spend one payment across two envelopes).
 */
export function needsFundingEnvelope(toType: BudgetAccountType): boolean {
  return toType === 'Loan'
}

/**
 * The envelopes that may fund a loan instalment. A **Payment envelope** is
 * excluded: it is derived solely from its own card's rows, so a categorised row
 * landing on it would vanish from every derivation — the exact defect
 * menunest-214 exists to prevent, one level down.
 */
export function fundingEnvelopeOptions(groups: EnvelopeGroupDto[]): PickerOption[] {
  return groups
    .flatMap(g => g.categories)
    .filter(c => c.paymentForAccountId === null && !c.isHidden)
    .map(c => ({
      id: c.categoryId,
      label: `${c.emoji ?? '•'} ${c.name} (${formatTHB(c.available)})`,
    }))
}

export interface PayingCardWarning {
  /** The paying card's own shortfall once this payment lands. */
  shortfallAfter: number
  text: string
}

/**
 * Paying one card with another moves **Ready to Assign UP**. That is correct —
 * the paid card's envelope empties while the paying card's debt widens with no
 * offsetting envelope — but it is surprising enough that a user would read it
 * as a bug. So the dialog says it out loud, and names the paying card's own
 * shortfall so the loop can be closed rather than wondered at.
 *
 * The arithmetic is `PaymentEnvelopeMath.Shortfall`, forward one payment: the
 * outflow leg is an uncategorised NEGATIVE row, which never moves the paying
 * card's `Available`, so only its balance changes.
 */
export function payingCardWarning(
  from: BudgetAccountDto | null | undefined,
  fromEnvelopeAvailable: number,
  amount: number | null | undefined,
): PayingCardWarning | null {
  if (!from || from.type !== 'Credit') return null
  const a = Number(amount ?? 0)
  if (!(a > 0)) return null

  const shortfallAfter = Math.max(0, -(from.balance - a) - fromEnvelopeAvailable)
  return {
    shortfallAfter,
    text:
      `จ่ายด้วยบัตร ${from.name} เป็นการย้ายหนี้ ไม่ใช่จ่ายจริง — ` +
      `หนี้บัตร ${from.name} จะเพิ่มอีก ${formatTHB(a)} ` +
      `ซองจ่ายบัตร ${from.name} จะขาดอีก ${formatTHB(shortfallAfter)} ` +
      `และเงินพร้อมจัดสรรจะเพิ่มขึ้น ${formatTHB(a)}`,
  }
}
