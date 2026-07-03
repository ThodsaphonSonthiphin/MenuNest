import {describe, it, expect} from 'vitest'
import {hmsToDate, dateToHms} from './time'

describe('hmsToDate', () => {
  it('parses "HH:mm:ss" into a local-time Date', () => {
    const d = hmsToDate('09:00:00')!
    expect(d.getHours()).toBe(9)
    expect(d.getMinutes()).toBe(0)
    expect(d.getSeconds()).toBe(0)
  })
  it('parses a single-digit / padded value', () => {
    const d = hmsToDate('08:05:00')!
    expect(d.getHours()).toBe(8)
    expect(d.getMinutes()).toBe(5)
  })
  it('returns null for null', () => {
    expect(hmsToDate(null)).toBeNull()
  })
})

describe('dateToHms', () => {
  it('formats a Date into zero-padded "HH:mm:ss"', () => {
    const d = new Date()
    d.setHours(8, 5, 0, 0)
    expect(dateToHms(d)).toBe('08:05:00')
  })
  it('returns null for null', () => {
    expect(dateToHms(null)).toBeNull()
  })
  it('round-trips with hmsToDate', () => {
    expect(dateToHms(hmsToDate('22:30:00'))).toBe('22:30:00')
  })
})
