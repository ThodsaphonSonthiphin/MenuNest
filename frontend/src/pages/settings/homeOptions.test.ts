import { describe, it, expect } from 'vitest'
import { HOME_OPTIONS, homeOptions, resolveHomePath } from './homeOptions'

describe('homeOptions', () => {
  it('returns all pages for a user with a family', () => {
    expect(homeOptions(true)).toEqual(HOME_OPTIONS)
  })

  it('hides family-gated pages for a user with no family', () => {
    const opts = homeOptions(false)
    expect(opts.every((o) => !o.requiresFamily)).toBe(true)
    expect(opts.map((o) => o.path)).toEqual(['/health', '/pomodoro', '/trips'])
  })
})

describe('resolveHomePath', () => {
  it('returns a stored path that is in the allowlist', () => {
    expect(resolveHomePath('/pomodoro')).toBe('/pomodoro')
    expect(resolveHomePath('/budget')).toBe('/budget')
  })

  it('falls back to /budget for null, empty, or unknown values', () => {
    expect(resolveHomePath(null)).toBe('/budget')
    expect(resolveHomePath(undefined)).toBe('/budget')
    expect(resolveHomePath('')).toBe('/budget')
    expect(resolveHomePath('/not-a-real-route')).toBe('/budget')
  })
})
