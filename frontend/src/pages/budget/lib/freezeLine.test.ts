import {describe, expect, it} from 'vitest'
import {formatFreezeLine} from './freezeLine'

// menunest-181: the card is frozen — frozenOn can lag behind today by any
// number of days within the current month (only a Budgeting event or a
// month rollover moves it, never a plain day rollover).
describe('formatFreezeLine', () => {
  it('reads "Set this morning" when frozenOn is today', () => {
    expect(formatFreezeLine('2026-08-22', '2026-08-22'))
      .toBe("Set this morning · won't change if you spend more today")
  })

  it('states the actual date when frozenOn is a prior day this month', () => {
    expect(formatFreezeLine('2026-08-20', '2026-08-22'))
      .toBe("Set Aug 20 · won't change if you spend more today")
  })

  it('does not zero-pad the day', () => {
    expect(formatFreezeLine('2026-08-05', '2026-08-22'))
      .toBe("Set Aug 5 · won't change if you spend more today")
  })

  it('formats every month abbreviation correctly (December boundary)', () => {
    expect(formatFreezeLine('2026-12-01', '2026-12-31'))
      .toBe("Set Dec 1 · won't change if you spend more today")
  })

  it('never parses the date string through Date() (no UTC-offset day shift)', () => {
    // If this were implemented via `new Date('2026-01-01')`, a
    // negative-UTC-offset test runner TZ would read it back as Dec 31 —
    // the exact bug formatDateThai.test.ts exists to catch. Splitting the
    // ISO string on '-' sidesteps it entirely, so the result is stable
    // regardless of the process's TZ.
    expect(formatFreezeLine('2026-01-01', '2026-01-15'))
      .toBe("Set Jan 1 · won't change if you spend more today")
  })
})
