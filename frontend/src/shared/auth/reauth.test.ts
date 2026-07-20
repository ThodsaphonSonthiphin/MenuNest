import {afterEach, describe, expect, it, vi} from 'vitest'
import {handleAuthFailure, isReauthBounce} from './reauth'

function stubEnv(pathname: string) {
  const removeItem = vi.fn()
  const assign = vi.fn()
  vi.stubGlobal('localStorage', {getItem: vi.fn(() => null), setItem: vi.fn(), removeItem})
  vi.stubGlobal('window', {location: {pathname, assign}})
  return {removeItem, assign}
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('handleAuthFailure', () => {
  it('clears the Google token and redirects to /login with a reauth marker from an app page', () => {
    const {removeItem, assign} = stubEnv('/budget')
    handleAuthFailure()
    expect(removeItem).toHaveBeenCalledWith('google_id_token')
    expect(assign).toHaveBeenCalledWith('/login?reauth=expired')
  })

  it('does not redirect when already on /login (avoids a redirect loop)', () => {
    const {removeItem, assign} = stubEnv('/login')
    handleAuthFailure()
    expect(removeItem).toHaveBeenCalledWith('google_id_token')
    expect(assign).not.toHaveBeenCalled()
  })
})

describe('isReauthBounce', () => {
  it('is true when the location carries the reauth marker', () => {
    expect(isReauthBounce('?reauth=expired')).toBe(true)
  })

  it('is false for a normal /login visit (no query)', () => {
    expect(isReauthBounce('')).toBe(false)
  })

  it('is false for an unrelated query value', () => {
    expect(isReauthBounce('?reauth=other&foo=bar')).toBe(false)
  })
})
