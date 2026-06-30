import {clearGoogleToken} from './googleAuth'

// Query marker appended when a 401 forces us to /login. LoginPage reads
// it (isReauthBounce) to suppress its auto-bounce-to-"/" — otherwise an
// apparently-authenticated session whose token the backend keeps
// rejecting (e.g. an MSAL token with a misconfigured audience, which
// acquireTokenSilent happily returns but the API 401s) would ping-pong
// between /login and the app in an infinite hard-reload loop. A Google
// session can't loop (its token is cleared above), but MSAL state is
// owned by msal-browser and survives the reload, so we need this guard.
const REAUTH_LOGIN_URL = '/login?reauth=expired'

/**
 * Called when an API request returns 401 — the bearer was missing,
 * expired, or rejected. Drop any stale Google token and bounce to the
 * login screen, unless we're already on /login (which would loop).
 *
 * This is the SPA half of the auth contract: the API is a stateless
 * resource server that answers an invalid token with `401 +
 * WWW-Authenticate: Bearer` and never redirects, so the client is
 * responsible for turning that 401 into a re-login.
 */
export function handleAuthFailure(): void {
  clearGoogleToken()
  if (typeof window !== 'undefined' && !window.location.pathname.startsWith('/login')) {
    window.location.assign(REAUTH_LOGIN_URL)
  }
}

/**
 * True when the current location was reached via a 401 reauth bounce
 * (see {@link handleAuthFailure}). LoginPage uses this to avoid
 * auto-redirecting a still-"authenticated" session straight back into
 * the app, which would loop when the backend keeps rejecting it.
 */
export function isReauthBounce(search: string): boolean {
  return new URLSearchParams(search).get('reauth') === 'expired'
}
