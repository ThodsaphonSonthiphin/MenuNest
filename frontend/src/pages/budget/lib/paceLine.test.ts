import {describe, expect, it} from 'vitest'
import {formatPaceLine} from './paceLine'

// menunest-186: the pace line is the only part of the daily-allowance card
// that reacts to spending. It counts completed days only, so it renders
// nothing at all on the freeze day itself (paceDelta === 0) and nothing for
// a near-zero delta that isn't really "over" or "under" in any meaningful
// sense. Positive paceDelta is over pace; negative is under.
describe('formatPaceLine', () => {
  it('renders nothing at exactly zero (the freeze day itself)', () => {
    expect(formatPaceLine(0)).toBeNull()
  })

  it('renders nothing just under the 0.005 boundary', () => {
    expect(formatPaceLine(0.004)).toBeNull()
    expect(formatPaceLine(-0.004)).toBeNull()
  })

  it('reports "over" for a positive delta, formatted in THB', () => {
    expect(formatPaceLine(180)).toBe('you are ฿180.00 over')
  })

  it('reports "under" for a negative delta, with the sign stripped', () => {
    expect(formatPaceLine(-180)).toBe('you are ฿180.00 under')
  })

  it('formats large amounts with thousands separators', () => {
    expect(formatPaceLine(12345.6)).toBe('you are ฿12,345.60 over')
  })
})
