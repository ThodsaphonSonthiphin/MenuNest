import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest'

// api.ts imports msalConfig.ts at module scope, whose top-level Configuration
// object touches `window.location.origin` and constructs a real
// PublicClientApplication — neither works under vitest's default `node`
// environment (no DOM). Stub it out exactly like useDayRoute.test.ts does, so
// the rest of api.ts (including acquireAccessToken, the function under test)
// loads for real. vi.hoisted is required because vi.mock factories run before
// the top of this file, so any variable they reference must be created here.
const mocks = vi.hoisted(() => ({
  getActiveAccount: vi.fn(),
  getAllAccounts: vi.fn(),
  acquireTokenSilent: vi.fn(),
  acquireTokenRedirect: vi.fn(),
  apiScopes: [] as string[],
}))

vi.mock('../auth/msalConfig', () => ({
  msalInstance: {
    getActiveAccount: mocks.getActiveAccount,
    getAllAccounts: mocks.getAllAccounts,
    acquireTokenSilent: mocks.acquireTokenSilent,
    acquireTokenRedirect: mocks.acquireTokenRedirect,
  },
  // Exported as the SAME array reference every test mutates in place
  // (`.length = 0` + `.push(...)`) rather than reassigning — reassigning
  // the export wouldn't be visible to api.ts's already-bound import.
  apiScopes: mocks.apiScopes,
}))

import {acquireAccessToken} from './api'
import {getAppSession, hasAppSession, storeAppSession} from '../auth/appSession'

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

// Build a base64url JWT segment (no padding), the shape Google emits —
// getGoogleToken/isGoogleTokenExpired actually decode this.
function b64url(obj: unknown): string {
  return btoa(JSON.stringify(obj))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
}
function makeGoogleToken(expiresInSeconds: number): string {
  const payload = {sub: '1', email: 'a@b.c', exp: Math.floor(Date.now() / 1000) + expiresInSeconds}
  return `${b64url({alg: 'RS256', typ: 'JWT'})}.${b64url(payload)}.sig`
}

beforeEach(() => {
  mocks.getActiveAccount.mockReset().mockReturnValue(null)
  mocks.getAllAccounts.mockReset().mockReturnValue([])
  mocks.acquireTokenSilent.mockReset()
  mocks.acquireTokenRedirect.mockReset()
  mocks.apiScopes.length = 0
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('acquireAccessToken', () => {
  it('returns the app session access token without consulting MSAL or Google when it is not yet expired', async () => {
    stubStorage()
    storeAppSession({accessToken: 'session-a', refreshToken: 'session-r', expiresIn: 3600})
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    // A live MSAL account and a live Google token are both available, so if
    // either got consulted the test would see it — precedence means neither
    // should be touched while a valid app session exists.
    mocks.getActiveAccount.mockReturnValue({homeAccountId: 'x'})
    mocks.apiScopes.push('api://scope')
    localStorage.setItem('google_id_token', makeGoogleToken(3600))

    const token = await acquireAccessToken()

    expect(token).toBe('session-a')
    expect(fetchMock).not.toHaveBeenCalled()
    expect(mocks.acquireTokenSilent).not.toHaveBeenCalled()
  })

  it('refreshes an expired app session and returns the rotated access token', async () => {
    stubStorage()
    storeAppSession({accessToken: 'stale-a', refreshToken: 'stale-r', expiresIn: 0})
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(true, {accessToken: 'rotated-a', expiresIn: 3600, refreshToken: 'rotated-r'}),
    )
    vi.stubGlobal('fetch', fetchMock)

    const token = await acquireAccessToken()

    expect(token).toBe('rotated-a')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/session/refresh'),
      expect.objectContaining({method: 'POST'}),
    )
  })

  it('single-flights several concurrent callers against one expired session into exactly one refresh request', async () => {
    stubStorage()
    storeAppSession({accessToken: 'stale-a', refreshToken: 'stale-r', expiresIn: 0})
    let resolveFetch!: (v: unknown) => void
    const fetchMock = vi.fn().mockReturnValue(new Promise((resolve) => { resolveFetch = resolve }))
    vi.stubGlobal('fetch', fetchMock)

    // Simulates several RTK Query prepareHeaders calls (and a
    // useAuthDataManager mount) all observing the same expired session at
    // once — the exact scenario that used to sign the user out (Critical 1).
    const callers = [acquireAccessToken(), acquireAccessToken(), acquireAccessToken()]
    resolveFetch(jsonResponse(true, {accessToken: 'rotated-a', expiresIn: 3600, refreshToken: 'rotated-r'}))
    const results = await Promise.all(callers)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(results).toEqual(['rotated-a', 'rotated-a', 'rotated-a'])
  })

  it('clears the session and falls through to the next provider on an explicit refusal', async () => {
    stubStorage()
    storeAppSession({accessToken: 'stale-a', refreshToken: 'stale-r', expiresIn: 0})
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ok: false, status: 400, json: async () => ({})}))
    // No MSAL account, but a live Google token to fall through to.
    const googleToken = makeGoogleToken(3600)
    localStorage.setItem('google_id_token', googleToken)

    const token = await acquireAccessToken()

    expect(token).toBe(googleToken)
    expect(hasAppSession()).toBe(false)
  })

  it('keeps the stored session intact and returns its still-valid access token on a transient failure', async () => {
    stubStorage()
    storeAppSession({accessToken: 'stale-a', refreshToken: 'stale-r', expiresIn: 0})
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network down')))

    const token = await acquireAccessToken()

    // Must NOT clear the session or fall through — the 60s expiry leeway
    // means the stored access token can still be perfectly good (Important 3).
    expect(token).toBe('stale-a')
    expect(hasAppSession()).toBe(true)
    expect(getAppSession()).toEqual(expect.objectContaining({accessToken: 'stale-a', refreshToken: 'stale-r'}))
  })

  it('falls through to MSAL when there is no app session', async () => {
    stubStorage()
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    mocks.getActiveAccount.mockReturnValue({homeAccountId: 'x'})
    mocks.apiScopes.push('api://scope')
    mocks.acquireTokenSilent.mockResolvedValue({accessToken: 'msal-token'})

    const token = await acquireAccessToken()

    expect(token).toBe('msal-token')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('falls through to Google when there is no app session and no MSAL account', async () => {
    stubStorage()
    const googleToken = makeGoogleToken(3600)
    localStorage.setItem('google_id_token', googleToken)
    // No MSAL account at all — acquireAccessToken must skip straight past it.
    mocks.getActiveAccount.mockReturnValue(null)
    mocks.getAllAccounts.mockReturnValue([])

    const token = await acquireAccessToken()

    expect(token).toBe(googleToken)
    expect(mocks.acquireTokenSilent).not.toHaveBeenCalled()
  })
})
