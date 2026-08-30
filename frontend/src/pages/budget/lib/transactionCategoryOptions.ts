import type {EnvelopeGroupDto} from '../../../shared/api/api'
import type {PickerOption} from './paymentOptions'

/**
 * The envelopes `TransactionDialog` may offer as an ordinary transaction's
 * category, mirroring `CreateTransactionHandler` / `UpdateTransactionHandler`'s
 * own refusal (`OrdinaryEnvelopeRule`) so the user never picks something the
 * server will reject.
 *
 * A **Payment envelope** is excluded, for exactly the reason
 * `fundingEnvelopeOptions` excludes it one level down (menunest-203): its
 * Available is DERIVED from its own card's rows, and `GetMonthlySummaryHandler`
 * branches on `paymentForAccountId` rather than walking the transaction list
 * for one. A row on a cash account carrying a Payment envelope is therefore
 * counted by neither the derivation nor the ordinary walk — while the cash
 * balance still falls, so the money leaves Ready to Assign and lands nowhere.
 *
 * Money reaches a Payment envelope by being ASSIGNED to it and leaves it by
 * paying the account (จ่ายบัตร / จ่ายค่างวด) — never by categorising here.
 *
 * The "— Uncategorized —" entry leads, as it always has: an uncategorised row
 * is income, not an error.
 */
export function transactionCategoryOptions(
  groups: EnvelopeGroupDto[],
  uncategorizedId: string,
  uncategorizedLabel = '— Uncategorized —',
): PickerOption[] {
  return [
    {id: uncategorizedId, label: uncategorizedLabel},
    ...groups
      .flatMap(g => g.categories)
      .filter(c => c.paymentForAccountId === null)
      .map(c => ({id: c.categoryId, label: `${c.emoji ?? '•'} ${c.name}`})),
  ]
}
