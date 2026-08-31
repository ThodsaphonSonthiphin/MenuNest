import type {EnvelopeDto, EnvelopeGroupDto} from '../../../shared/api/api'
import type {PickerOption} from './paymentOptions'
import {formatTHB} from './formatTHB'

/**
 * The sentinel the source dropdown carries for "Ready to Assign". It is NOT a
 * category id — Ready to Assign is derived (`sum(accounts) − sum(envelope
 * .available)`) and owns no row anywhere — so `CoverOverspendingDialog` maps it
 * back to a null `fromCategoryId` on the wire (menunest-215). A `__` prefix
 * keeps it clear of any Guid a real envelope could ever have.
 */
export const READY_TO_ASSIGN = '__ready-to-assign__'

/**
 * The sources `CoverOverspendingDialog` may offer for an overspent envelope.
 *
 * **Ready to Assign leads the list** whenever it holds money (#115). Before
 * menunest-215 the list was envelopes only, so a user with money still to place
 * had to first assign it to some envelope and then move it — or, seeing no
 * usable source, conclude the app had lost their money. Money not yet placed is
 * the most natural thing to cover an overspend with, so it is offered first.
 *
 * Then every OTHER envelope holding spare cash, mirroring what the server will
 * accept: the overspent envelope itself is refused by
 * `CoverOverspendingValidator` ("Source and overspent category must differ."),
 * and an envelope with nothing spare would only push its own balance negative,
 * trading one overspend for another.
 *
 * `readyToAssign` is offered only while it is strictly positive. That is a UI
 * choice, not a mirrored server refusal — the server allows over-assigning, and
 * `RtaHero`/`SuggestedFixCard` already handle a negative Ready to Assign as a
 * designed, recoverable state. Offering an empty or overdrawn Ready to Assign
 * as a *source* would simply be nonsense at the point of choosing.
 */
export function coverSourceOptions(
  groups: EnvelopeGroupDto[],
  overspent: EnvelopeDto,
  readyToAssign: number,
): PickerOption[] {
  const options: PickerOption[] = []

  if (readyToAssign > 0) {
    options.push({
      id: READY_TO_ASSIGN,
      label: `💰 เงินที่ยังไม่ได้จัดสรร (${formatTHB(readyToAssign)})`,
    })
  }

  for (const c of groups.flatMap(g => g.categories)) {
    if (c.categoryId === overspent.categoryId) continue
    if (c.available <= 0) continue
    options.push({
      id: c.categoryId,
      label: `${c.emoji ?? '•'} ${c.name} (${formatTHB(c.available)})`,
    })
  }

  return options
}

/**
 * The `fromCategoryId` to put on the wire for a chosen source: a real category
 * id, or null for Ready to Assign. Null is what tells
 * `CoverOverspendingHandler` to increment the overspent envelope alone.
 */
export function toFromCategoryId(selected: string): string | null {
  return selected === READY_TO_ASSIGN ? null : selected
}
