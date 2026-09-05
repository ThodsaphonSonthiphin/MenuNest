import {describe, it, expect} from 'vitest'
import {normalizeSortDirection, formatTripDate} from './tripsGrid'

describe('normalizeSortDirection', () => {
  it('capitalises what the UrlAdaptor emits', () => {
    expect(normalizeSortDirection('ascending')).toBe('Ascending')
    expect(normalizeSortDirection('descending')).toBe('Descending')
  })

  it('passes the already-capitalised form through', () => {
    expect(normalizeSortDirection('Ascending')).toBe('Ascending')
    expect(normalizeSortDirection('Descending')).toBe('Descending')
  })

  it('defaults anything unrecognised to ascending', () => {
    expect(normalizeSortDirection('')).toBe('Ascending')
    expect(normalizeSortDirection(undefined)).toBe('Ascending')
    expect(normalizeSortDirection(null)).toBe('Ascending')
    expect(normalizeSortDirection('sideways')).toBe('Ascending')
  })
})

describe('formatTripDate', () => {
  it('renders a DateOnly string as dd/MM/yyyy', () => {
    expect(formatTripDate('2026-03-01')).toBe('01/03/2026')
    expect(formatTripDate('2026-12-25')).toBe('25/12/2026')
  })

  it('ignores a time component rather than shifting the day', () => {
    expect(formatTripDate('2026-03-01T00:00:00')).toBe('01/03/2026')
  })

  it('returns an empty string for anything that is not a date', () => {
    expect(formatTripDate(null)).toBe('')
    expect(formatTripDate(undefined)).toBe('')
    expect(formatTripDate('')).toBe('')
    expect(formatTripDate('not a date')).toBe('')
    expect(formatTripDate(20260301)).toBe('')
  })
})
