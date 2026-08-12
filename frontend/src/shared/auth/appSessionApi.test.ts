import {afterEach, describe, expect, it, vi} from 'vitest'
import {TransientSessionError, exchangeForAppSession, refreshAppSession, revokeAppSession} from './appSessionApi'
import {getAppSession, hasAppSession, storeAppSession} from './appSession'

/** Minimal in-memory Storage stand-in — vitest runs in `node`, no DOM. */
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

function jsonResponse(ok: boolean, body: unknown) {
  return {ok, json: async () => body}
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('exchangeForAppSession', () => {
  it('stores the session and returns true when the exchange succeeds', async () => {
    stubStorage()
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(true, {accessToken: 'a', expiresIn: 3600, refreshToken: 'r'}),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await exchangeForAppSession('provider-token')

    expect(result).toBe(true)
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/session/exchange'),
      expect.objectContaining({
        method: 'POST',
        headers: {Authorization: 'Bearer provider-token'},
      }),
    )
    expect(getAppSession()).toEqual(
      expect.objectContaining({accessToken: 'a', refreshToken: 'r'}),
    )
  })

  it('returns false without storing anything when the exchange responds non-200', async () => {
    const {store} = stubStorage()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(false, {})))

    const result = await exchangeForAppSession('provider-token')

    expect(result).toBe(false)
    expect(store.setItem).not.toHaveBeenCalled()
  })

  it('returns false when fetch throws, so a down API never blocks sign-in', async () => {
    stubStorage()
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network down')))

    const result = await exchangeForAppSession('provider-token')

    expect(result).toBe(false)
  })
})

describe('refreshAppSession', () => {
  it('rotates the session and returns it when the server accepts the refresh token', async () => {
    stubStorage()
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(true, {accessToken: 'new-a', expiresIn: 3600, refreshToken: 'new-r'}),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await refreshAppSession('old-r')

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/session/refresh'),
      expect.objectContaining({
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({refresh_token: 'old-r'}),
      }),
    )
    expect(result).toEqual(expect.objectContaining({accessToken: 'new-a', refreshToken: 'new-r'}))
  })

  it('clears the session and returns null on a bare 400 with no invalid_grant body', async () => {
    stubStorage()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})
    expect(hasAppSession()).toBe(true)
    // A malformed/absent body yields a bare 400 (Production) with no parseable
    // error field — the check must key off the status, not the body content.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ok: false, status: 400, json: async () => { throw new Error('no body') }}))

    const result = await refreshAppSession('r')

    expect(result).toBeNull()
    expect(hasAppSession()).toBe(false)
  })

  it('keeps the session another TAB already rotated instead of clearing it on the losing 400', async () => {
    // Two tabs left open overnight both wake with the same expired session.
    // The single-flight guard is module-scoped, so it does not span tabs: the
    // loser presents the already-rotated (single-use) code and gets a 400. It
    // must NOT clear the shared localStorage keys the winning tab just wrote —
    // that would 401 both tabs back to /login.
    stubStorage()
    storeAppSession({accessToken: 'winner-a', refreshToken: 'winner-r', expiresIn: 3600})
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ok: false, status: 400, json: async () => ({})}))

    const result = await refreshAppSession('stale-r')

    expect(result).toEqual(expect.objectContaining({accessToken: 'winner-a', refreshToken: 'winner-r'}))
    expect(hasAppSession()).toBe(true)
  })

  it('clears the session and returns null on a 500 (Development shape), not just a 400', async () => {
    stubStorage()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ok: false, status: 500, json: async () => ({})}))

    const result = await refreshAppSession('r')

    expect(result).toBeNull()
    expect(hasAppSession()).toBe(false)
  })

  it('keeps the existing session and throws TransientSessionError on a network error, so a blip does not sign the user out', async () => {
    stubStorage()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network down')))

    await expect(refreshAppSession('r')).rejects.toBeInstanceOf(TransientSessionError)
    expect(getAppSession()).toEqual(expect.objectContaining({accessToken: 'a', refreshToken: 'r'}))
  })

  it('keeps the existing session and throws TransientSessionError on an unparseable 200 body', async () => {
    stubStorage()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => { throw new Error('bad json') },
    }))

    await expect(refreshAppSession('r')).rejects.toBeInstanceOf(TransientSessionError)
    expect(getAppSession()).toEqual(expect.objectContaining({accessToken: 'a', refreshToken: 'r'}))
  })

  it('single-flights concurrent refresh calls into exactly one request (the backend refresh grant is single-use)', async () => {
    stubStorage()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})
    let resolveFetch!: (v: unknown) => void
    const fetchMock = vi.fn().mockReturnValue(new Promise((resolve) => { resolveFetch = resolve }))
    vi.stubGlobal('fetch', fetchMock)

    // Two concurrent callers, e.g. an RTK Query prepareHeaders call and a
    // useAuthDataManager mount, both racing to rotate the same session.
    const first = refreshAppSession('r')
    const second = refreshAppSession('r')

    resolveFetch(jsonResponse(true, {accessToken: 'new-a', expiresIn: 3600, refreshToken: 'new-r'}))
    const [firstResult, secondResult] = await Promise.all([first, second])

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(firstResult).toEqual(expect.objectContaining({accessToken: 'new-a'}))
    expect(secondResult).toEqual(expect.objectContaining({accessToken: 'new-a'}))
  })

  it('starts a fresh request for a later, non-overlapping refresh call', async () => {
    stubStorage()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(true, {accessToken: 'new-a', expiresIn: 3600, refreshToken: 'new-r'}),
    )
    vi.stubGlobal('fetch', fetchMock)

    await refreshAppSession('r')
    await refreshAppSession('new-r')

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})

describe('revokeAppSession', () => {
  it('posts the refresh token to /api/session/logout', async () => {
    stubStorage()
    const fetchMock = vi.fn().mockResolvedValue({ok: true})
    vi.stubGlobal('fetch', fetchMock)

    await revokeAppSession('r')

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/session/logout'),
      expect.objectContaining({
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({refresh_token: 'r'}),
      }),
    )
  })

  it('never throws even when the request fails', async () => {
    stubStorage()
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network down')))

    await expect(revokeAppSession('r')).resolves.toBeUndefined()
  })
})
