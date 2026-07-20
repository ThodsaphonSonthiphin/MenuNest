import {describe, it, expect} from 'vitest'
import {UV_ALERT_OPTIONS, FEELS_ALERT_OPTIONS, selectedAlertValue} from './weatherAlertOptions'

describe('weatherAlertOptions', () => {
  it('UV presets incl. off', () => expect(UV_ALERT_OPTIONS.map(o => o.value)).toEqual([3, 6, 8, 0]))
  it('feels presets incl. off', () => expect(FEELS_ALERT_OPTIONS.map(o => o.value)).toEqual([38, 40, 42, 0]))
  it('null → default', () => expect(selectedAlertValue(null, 6)).toBe(6))
  it('undefined → default', () => expect(selectedAlertValue(undefined, 40)).toBe(40))
  it('0 (off) preselects verbatim', () => expect(selectedAlertValue(0, 6)).toBe(0))
  it('stored value preselects', () => expect(selectedAlertValue(8, 6)).toBe(8))
})
