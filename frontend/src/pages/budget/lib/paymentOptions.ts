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
 * What paying with a **Credit** card does to that card, which differs entirely
 * with what is being paid — hence `toAccountType`, without which this function
 * cannot tell the two apart and silently reports the card-to-card figures for
 * both.
 *
 * Both branches carry `PaymentEnvelopeMath` forward one payment. The paying
 * card's balance falls by the amount either way; what changes is whether its
 * own `Available` follows.
 *
 * **Paying a card** — the outflow leg is UNCATEGORISED, so
 * `PaymentEnvelopeMath.Available` never counts it and the paying card's
 * `Available` stands still while its balance falls: its shortfall grows by the
 * full amount. And Ready to Assign moves **UP** — the paid card's envelope
 * empties while this card's debt widens with no offsetting envelope. That is
 * correct but surprising enough to read as a bug, so it is said out loud.
 *
 * **Paying a loan** (menunest-214) — the outflow leg is CATEGORISED, so
 * `Available = assigned − categorised − …` rises by the very amount the
 * balance falls. The shortfall is therefore **unchanged**, and Ready to Assign
 * does not move either: the funding Envelope falls while the Payment envelope
 * rises, and no cash account is touched. Reporting a rise here — as this
 * function did before — overstates the shortfall by the whole payment and
 * states a falsehood about Ready to Assign.
 */
export function payingCardWarning(
  from: BudgetAccountDto | null | undefined,
  fromEnvelopeAvailable: number,
  amount: number | null | undefined,
  toAccountType: BudgetAccountType,
): PayingCardWarning | null {
  if (!from || from.type !== 'Credit') return null
  const a = Number(amount ?? 0)
  if (!(a > 0)) return null

  if (toAccountType === 'Loan') {
    // Balance −a and Available +a cancel: max(0, −balance − available).
    const shortfallAfter = Math.max(0, -from.balance - fromEnvelopeAvailable)
    return {
      shortfallAfter,
      text:
        `จ่ายด้วยบัตร ${from.name} จะทำให้ยอดบัตรเพิ่มอีก ${formatTHB(a)} — ` +
        `เงินในซองที่เลือกจะย้ายไปอยู่ในซองจ่ายบัตร ${from.name} แทน ` +
        `ซองนั้นจึงยังขาดอีก ${formatTHB(shortfallAfter)} เท่าเดิม ` +
        `และเงินพร้อมจัดสรรไม่เปลี่ยน`,
    }
  }

  const shortfallAfter = Math.max(0, -(from.balance - a) - fromEnvelopeAvailable)
  return {
    shortfallAfter,
    text:
      `จ่ายด้วยบัตร ${from.name} เป็นการย้ายยอดค้าง ไม่ใช่จ่ายจริง — ` +
      `ยอดบัตร ${from.name} จะเพิ่มอีก ${formatTHB(a)} ` +
      `ซองจ่ายบัตร ${from.name} จะขาดอีก ${formatTHB(shortfallAfter)} ` +
      `และเงินพร้อมจัดสรรจะเพิ่มขึ้น ${formatTHB(a)}`,
  }
}
