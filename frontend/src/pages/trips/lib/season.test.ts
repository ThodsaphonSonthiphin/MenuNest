import {describe, it, expect} from 'vitest'
import {monthStatus, rangeLabel, monthOfDate} from './season'
import type {SeasonPeriod} from '../../../shared/api/api'

const bad = (months: number[], note = 'x'): SeasonPeriod => ({kind: 'Bad', months, note})
const good = (months: number[], note = 'y'): SeasonPeriod => ({kind: 'Good', months, note})

describe('monthStatus', () => {
  it('bad wins over good on the same month', () => {
    const s = monthStatus([good([5]), bad([5])], 5)
    expect(s.kind).toBe('bad')
  })
  it('returns the first matching bad period on overlap', () => {
    const s = monthStatus([bad([5], 'first'), bad([5], 'second')], 5)
    expect(s.kind === 'bad' && s.period.note).toBe('first')
  })
  it('good when only a good period matches', () => {
    expect(monthStatus([good([10, 11])], 11).kind).toBe('good')
  })
  it('none when nothing matches', () => {
    expect(monthStatus([bad([5])], 0).kind).toBe('none')
  })
})

describe('rangeLabel', () => {
  it('compresses a contiguous run', () => {
    expect(rangeLabel([5, 6, 7, 8, 9])).toBe('มิ.ย.–ต.ค.')
  })
  it('wraps across the year boundary into separate runs', () => {
    expect(rangeLabel([10, 11, 0, 1])).toBe('ม.ค.–ก.พ., พ.ย.–ธ.ค.')
  })
  it('renders a single month without a dash', () => {
    expect(rangeLabel([11])).toBe('ธ.ค.')
  })
})

describe('monthOfDate', () => {
  it('parses 0-based month from an ISO date', () => {
    expect(monthOfDate('2026-07-12')).toBe(6)
    expect(monthOfDate('2026-01-01T00:00:00')).toBe(0)
  })
})
