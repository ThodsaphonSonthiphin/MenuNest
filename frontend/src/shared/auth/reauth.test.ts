import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest'
import {
  clearInteractiveLoginStarted,
  handleAuthFailure,
  isReauthBounce,
  markInteractiveLoginStarted,
  takeInteractiveLoginStarted,
} from './reauth'

/** Minimal in-memory Storage stand-in — vitest runs in `node`, no DOM. */
function fakeStorage() {
  const map = new Map<string, string>()
  return {
    getItem: (k: string) => (map.has(k) ? map.get(k)! : null),
    setItem: (k: string, v: string) => void map.set(k, v),
    removeItem: (k: string) => void map.delete(k),
  }
}

function stubEnv(pathname: string) {
  const removeItem = vi.fn()
  const assign = vi.fn()
  vi.stubGlobal('localStorage', {getItem: vi.fn(() => null), setItem: vi.fn(), removeItem})
  vi.stubGlobal('window', {location: {pathname, assign}})
  return {removeItem, assign}
}

beforeEach(() => {
  vi.stubGlobal('sessionStorage', fakeStorage())
})

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

describe('interactive-login flag', () => {
  it('is false when no sign-in was started', () => {
    expect(takeInteractiveLoginStarted()).toBe(false)
  })

  it('is true once after a sign-in is started, then false', () => {
    markInteractiveLoginStarted()
    expect(takeInteractiveLoginStarted()).toBe(true)
    // Single-use: a second read must not wave the user in again, so a
    // still-rejected token bounces out once instead of ping-ponging.
    expect(takeInteractiveLoginStarted()).toBe(false)
  })

  it('is false after an explicit clear (redirect never left the page)', () => {
    markInteractiveLoginStarted()
    clearInteractiveLoginStarted()
    expect(takeInteractiveLoginStarted()).toBe(false)
  })

  it('is spent by a 401 — handleAuthFailure clears it', () => {
    stubEnv('/budget')
    markInteractiveLoginStarted()
    handleAuthFailure()
    expect(takeInteractiveLoginStarted()).toBe(false)
  })

  it('survives storage being unavailable', () => {
    vi.stubGlobal('sessionStorage', undefined)
    expect(() => markInteractiveLoginStarted()).not.toThrow()
    expect(() => clearInteractiveLoginStarted()).not.toThrow()
    expect(takeInteractiveLoginStarted()).toBe(false)
  })
})
