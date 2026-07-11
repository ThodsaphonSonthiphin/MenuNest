import {describe, it, expect} from 'vitest'
import {hmsToDate, dateToHms, formatDurationMinutes} from './time'

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

describe('formatDurationMinutes', () => {
  it('formats under an hour as plain minutes', () => {
    expect(formatDurationMinutes(0)).toBe('0 น.')
    expect(formatDurationMinutes(45)).toBe('45 น.')
  })
  it('formats an hour or more as hours + minutes', () => {
    expect(formatDurationMinutes(60)).toBe('1 ชม. 0 น.')
    expect(formatDurationMinutes(133)).toBe('2 ชม. 13 น.')
  })
  it('rounds fractional minutes', () => {
    expect(formatDurationMinutes(133.4)).toBe('2 ชม. 13 น.')
  })
  it('clamps negative input to 0', () => {
    expect(formatDurationMinutes(-5)).toBe('0 น.')
  })
})
