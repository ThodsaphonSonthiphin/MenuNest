import {afterEach, describe, expect, it, vi} from 'vitest'

// useAuthDataManager.ts pulls in api.ts (→ msalConfig.ts, which builds a real
// PublicClientApplication against `window.location.origin`) and
// @azure/msal-react. Neither loads under vitest's `node` environment, and
// neither is under test here — AuthAdaptor is a plain class. Stub both so the
// module itself loads for real. vi.mock factories are hoisted above the imports.
vi.mock('../auth/msalConfig', () => ({
  msalInstance: {
    getActiveAccount: () => null,
    getAllAccounts: () => [],
    acquireTokenSilent: async () => ({accessToken: ''}),
    acquireTokenRedirect: async () => undefined,
  },
  apiScopes: [] as string[],
}))
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({instance: {}, accounts: []}),
}))

import {AuthAdaptor} from './useAuthDataManager'
import {getAppSession} from '../auth/appSession'

/**
 * The only part of the `Request` contract `beforeSend` touches is `headers`,
 * and WebApiAdaptor's own `beforeSend` only sets `Accept` on it.
 */
function stubRequest() {
  return {headers: new Headers()} as unknown as Request
}

function stubStorage(initial: Record<string, string> = {}) {
  const map = new Map(Object.entries(initial))
  vi.stubGlobal('localStorage', {
    getItem: vi.fn((k: string) => map.get(k) ?? null),
    setItem: vi.fn((k: string, v: string) => void map.set(k, v)),
    removeItem: vi.fn((k: string) => void map.delete(k)),
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

const dm = {} as never

describe('AuthAdaptor.beforeSend', () => {
  it('sends the token the getter returns', () => {
    const adaptor = new AuthAdaptor(() => 'tok-1')
    const req = stubRequest()

    adaptor.beforeSend(dm, req)

    expect(req.headers.get('Authorization')).toBe('Bearer tok-1')
  })

  it('re-reads the getter on every request, so a rotated token is sent', () => {
    // The regression this guards: the adaptor used to capture the token string
    // in its constructor. A page held open past the 1-hour access-token
    // lifetime then sent the same expired bearer forever, because the hook's
    // effect never re-runs once MSAL's cache is purged.
    let token = 'tok-old'
    const adaptor = new AuthAdaptor(() => token)

    const first = stubRequest()
    adaptor.beforeSend(dm, first)
    expect(first.headers.get('Authorization')).toBe('Bearer tok-old')

    token = 'tok-rotated'
    const second = stubRequest()
    adaptor.beforeSend(dm, second)
    expect(second.headers.get('Authorization')).toBe('Bearer tok-rotated')
  })

  it('sends an empty bearer when no token is available rather than throwing', () => {
    const adaptor = new AuthAdaptor(() => '')
    const req = stubRequest()

    adaptor.beforeSend(dm, req)

    // Headers strips the trailing space the template literal leaves behind.
    expect(req.headers.get('Authorization')).toBe('Bearer')
  })

  it('falls back to the acquired provider token when there is no app session, instead of an empty bearer', () => {
    // The regression this guards: useAuthDataManager's real getter is
    // `() => getAppSession()?.accessToken ?? token`. With no app session
    // stored (exchange failed, or never attempted) it must fall back to the
    // MSAL/Google token the hook's effect already acquired — not to '', which
    // would send `Authorization: Bearer ` with nothing after it even though a
    // perfectly valid provider token is sitting in hand.
    stubStorage() // no app session keys stored -> getAppSession() returns null
    const providerToken = 'provider-tok'
    const adaptor = new AuthAdaptor(() => getAppSession()?.accessToken ?? providerToken)
    const req = stubRequest()

    adaptor.beforeSend(dm, req)

    expect(req.headers.get('Authorization')).toBe('Bearer provider-tok')
  })
})
