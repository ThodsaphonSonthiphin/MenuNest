import {describe, it, expect} from 'vitest'
import {addStopDayLabel} from './addStopCapture'

const days = [{id: 'a'}, {id: 'b'}, {id: 'c'}]

describe('addStopDayLabel', () => {
  it('labels by 1-based day index', () => {
    expect(addStopDayLabel(days, 'b')).toBe('วัน 2')
  })
  it('appends the destination when present', () => {
    expect(addStopDayLabel(days, 'a', 'ระยอง')).toBe('วัน 1 · ระยอง')
  })
  it('ignores a blank destination', () => {
    expect(addStopDayLabel(days, 'c', '   ')).toBe('วัน 3')
  })
  it('returns null when the day id is not found', () => {
    expect(addStopDayLabel(days, 'z')).toBeNull()
  })
})