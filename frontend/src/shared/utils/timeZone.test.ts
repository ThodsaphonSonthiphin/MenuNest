import {describe, it, expect, vi} from 'vitest'
import {getViewerTimeZone} from './timeZone'

describe('getViewerTimeZone', () => {
  it("returns the browser's resolved IANA time zone", () => {
    const expected = Intl.DateTimeFormat().resolvedOptions().timeZone
    expect(getViewerTimeZone()).toBe(expected)
  })

  it("falls back to 'UTC' when Intl resolves an empty time zone", () => {
    const spy = vi.spyOn(Intl, 'DateTimeFormat').mockReturnValue({resolvedOptions: () => ({timeZone: ''})} as any)
    expect(getViewerTimeZone()).toBe('UTC')
    spy.mockRestore()
  })
})
