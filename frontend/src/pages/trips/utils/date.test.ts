import {describe, it, expect} from 'vitest'
import {ymdToDate, dateToYmd, endDate} from './date'

describe('ymdToDate', () => {
  it('parses "yyyy-MM-dd" into a local-midnight Date', () => {
    const d = ymdToDate('2026-11-14')!
    expect(d.getFullYear()).toBe(2026)
    expect(d.getMonth()).toBe(10) // November (0-based)
    expect(d.getDate()).toBe(14)
    expect(d.getHours()).toBe(0)
  })
  it('tolerates a time/zone suffix (API DateOnly serialized with time)', () => {
    const d = ymdToDate('2026-11-14T00:00:00Z')!
    expect(d.getFullYear()).toBe(2026)
    expect(d.getMonth()).toBe(10)
    expect(d.getDate()).toBe(14)
  })
  it('returns null for null / empty / malformed input', () => {
    expect(ymdToDate(null)).toBeNull()
    expect(ymdToDate(undefined)).toBeNull()
    expect(ymdToDate('')).toBeNull()
    expect(ymdToDate('not-a-date')).toBeNull()
  })
})

describe('dateToYmd', () => {
  it('formats a Date into zero-padded "yyyy-MM-dd" using local fields', () => {
    const d = new Date(2026, 0, 5) // 5 Jan 2026, local midnight
    expect(dateToYmd(d)).toBe('2026-01-05')
  })
  it('returns null for null / invalid Date', () => {
    expect(dateToYmd(null)).toBeNull()
    expect(dateToYmd(new Date('nonsense'))).toBeNull()
  })
  it('round-trips with ymdToDate (no UTC drift)', () => {
    expect(dateToYmd(ymdToDate('2026-11-14'))).toBe('2026-11-14')
    expect(dateToYmd(ymdToDate('2026-01-01'))).toBe('2026-01-01')
    expect(dateToYmd(ymdToDate('2026-12-31'))).toBe('2026-12-31')
  })
})

describe('endDate', () => {
  it('returns the start itself for a 1-day trip', () => {
    const start = ymdToDate('2026-11-14')
    expect(dateToYmd(endDate(start, 1))).toBe('2026-11-14')
  })
  it('adds (dayCount − 1) days, inclusive', () => {
    const start = ymdToDate('2026-11-14')
    expect(dateToYmd(endDate(start, 5))).toBe('2026-11-18')
  })
  it('crosses a month boundary correctly', () => {
    const start = ymdToDate('2026-01-30')
    expect(dateToYmd(endDate(start, 3))).toBe('2026-02-01')
  })
  it('clamps a non-positive dayCount to a single day', () => {
    const start = ymdToDate('2026-11-14')
    expect(dateToYmd(endDate(start, 0))).toBe('2026-11-14')
  })
  it('returns null when start is null', () => {
    expect(endDate(null, 3)).toBeNull()
  })
})
