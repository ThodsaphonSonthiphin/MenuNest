import { afterEach, describe, expect, it } from 'vitest'
import { formatDateThai } from './formatDate'

describe('formatDateThai', () => {
  const originalTz = process.env.TZ

  afterEach(() => {
    process.env.TZ = originalTz
  })

  it('formats a YYYY-MM-DD date string with Thai day/month/year', () => {
    const result = formatDateThai('2024-03-05')
    expect(result).toContain('5') // day
    expect(result).toContain('2567') // Buddhist-era year for 2024
  })

  it('does not shift to the previous day in a negative-UTC-offset timezone', () => {
    // `new Date('2024-01-01')` (bare YYYY-MM-DD) parses as UTC midnight, which
    // in America/Los_Angeles (UTC-8) renders as 31 Dec 2023 -- the bug this
    // helper fixes. Constructing from local year/month/day parts must keep
    // the intended calendar day regardless of the viewer's timezone.
    process.env.TZ = 'America/Los_Angeles'
    const result = formatDateThai('2024-01-01')
    expect(result).toContain('1 ม.ค.')
    expect(result).not.toContain('31 ธ.ค.')
  })

  it('handles end-of-month / leap-year dates correctly', () => {
    const result = formatDateThai('2024-02-29')
    expect(result).toContain('29 ก.พ.')
  })
})
