import {describe, it, expect} from 'vitest'
import {resolveBestTime} from './bestTime'
import type {BestTimeWindow} from '../../../shared/api/api'

const w = (start: string, end: string, note: string | null = null): BestTimeWindow => ({start, end, note})
const morning = w('06:00:00', '09:00:00', 'แดดร่ม')
const evening = w('17:00:00', '19:00:00', 'แดดร่ม')

describe('resolveBestTime', () => {
  it('returns null when no windows', () => {
    expect(resolveBestTime([], 8 * 60)).toBeNull()
    expect(resolveBestTime(undefined, 8 * 60)).toBeNull()
  })
  it('returns null when arrival is inside any window (bounds inclusive)', () => {
    expect(resolveBestTime([morning, evening], 7 * 60)).toBeNull()
    expect(resolveBestTime([morning, evening], 6 * 60)).toBeNull()
    expect(resolveBestTime([morning, evening], 9 * 60)).toBeNull()
    expect(resolveBestTime([morning, evening], 18 * 60)).toBeNull()
  })
  it('between windows: nearest is the missed morning (dir after), upcoming is the evening', () => {
    const off = resolveBestTime([morning, evening], 12 * 60 + 30)!
    expect(off.nearest).toBe(morning)
    expect(off.dir).toBe('after')
    expect(off.upcoming).toBe(evening)
  })
  it('before all windows: nearest = first, dir before, upcoming = first', () => {
    const off = resolveBestTime([morning, evening], 5 * 60)!
    expect(off.nearest).toBe(morning)
    expect(off.dir).toBe('before')
    expect(off.upcoming).toBe(morning)
  })
  it('after all windows: dir after, no upcoming', () => {
    const off = resolveBestTime([morning, evening], 21 * 60)!
    expect(off.nearest).toBe(evening)
    expect(off.dir).toBe('after')
    expect(off.upcoming).toBeNull()
  })
})
