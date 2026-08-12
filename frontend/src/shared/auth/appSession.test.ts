import {afterEach, describe, expect, it, vi} from 'vitest'
import {
  clearAppSession,
  getAppSession,
  hasAppSession,
  isAppSessionExpired,
  storeAppSession,
} from './appSession'

function stubStorage(initial: Record<string, string> = {}) {
  const map = new Map(Object.entries(initial))
  const store = {
    getItem: vi.fn((k: string) => map.get(k) ?? null),
    setItem: vi.fn((k: string, v: string) => void map.set(k, v)),
    removeItem: vi.fn((k: string) => void map.delete(k)),
  }
  vi.stubGlobal('localStorage', store)
  return {store, map}
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('storeAppSession / getAppSession', () => {
  it('round-trips a stored session and derives an absolute expiry', () => {
    stubStorage()
    const before = Date.now()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})

    const session = getAppSession()
    expect(session?.accessToken).toBe('a')
    expect(session?.refreshToken).toBe('r')
    expect(session!.expiresAtMs).toBeGreaterThanOrEqual(before + 3600 * 1000)
  })

  it('returns null when nothing is stored', () => {
    stubStorage()
    expect(getAppSession()).toBeNull()
  })

  it('returns null when the refresh token is missing, rather than a half session', () => {
    stubStorage({'menunest.session.access': 'a', 'menunest.session.expiresAt': '99999999999999'})
    expect(getAppSession()).toBeNull()
  })

  it('leaves no session at all when a write fails part way, rather than a mismatched one', () => {
    // Quota exceeded / Safari private mode: the access token lands, the refresh
    // token does not. All three keys would be present but the pair would be
    // mismatched — getAppSession's presence check cannot see that, so the
    // session would look fine and could never be renewed.
    const map = new Map<string, string>([
      ['menunest.session.access', 'old-a'],
      ['menunest.session.refresh', 'old-r'],
      ['menunest.session.expiresAt', String(Date.now() + 3_600_000)],
    ])
    let calls = 0
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((k: string) => map.get(k) ?? null),
      setItem: vi.fn((k: string, v: string) => {
        calls += 1
        if (calls === 2) throw new Error('QuotaExceededError')
        map.set(k, v)
      }),
      removeItem: vi.fn((k: string) => void map.delete(k)),
    })

    expect(() => storeAppSession({accessToken: 'new-a', refreshToken: 'new-r', expiresIn: 3600}))
      .not.toThrow()
    expect(getAppSession()).toBeNull()
    expect(hasAppSession()).toBe(false)
  })

  it('returns null when the stored expiry is not a number', () => {
    stubStorage({
      'menunest.session.access': 'a',
      'menunest.session.refresh': 'r',
      'menunest.session.expiresAt': 'not-a-number',
    })
    expect(getAppSession()).toBeNull()
  })
})

describe('isAppSessionExpired', () => {
  it('is false well before expiry', () => {
    expect(isAppSessionExpired(10_000_000, 9_000_000)).toBe(false)
  })

  it('is true once past expiry', () => {
    expect(isAppSessionExpired(9_000_000, 10_000_000)).toBe(true)
  })

  it('is true inside the 60s leeway so a token never dies mid-flight', () => {
    const now = 1_000_000
    expect(isAppSessionExpired(now + 30_000, now)).toBe(true)
    expect(isAppSessionExpired(now + 90_000, now)).toBe(false)
  })
})

describe('clearAppSession / hasAppSession', () => {
  it('removes every key it wrote', () => {
    const {store} = stubStorage()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})
    expect(hasAppSession()).toBe(true)

    clearAppSession()
    expect(store.removeItem).toHaveBeenCalledWith('menunest.session.access')
    expect(store.removeItem).toHaveBeenCalledWith('menunest.session.refresh')
    expect(store.removeItem).toHaveBeenCalledWith('menunest.session.expiresAt')
    expect(hasAppSession()).toBe(false)
  })
})
