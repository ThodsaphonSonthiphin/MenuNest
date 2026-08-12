import {clearGoogleToken} from './googleAuth'
import {clearAppSession} from './appSession'

// Query marker appended when a 401 forces us to /login. LoginPage reads
// it (isReauthBounce) to suppress its auto-bounce-to-"/" — otherwise an
// apparently-authenticated session whose token the backend keeps
// rejecting (e.g. an MSAL token with a misconfigured audience, which
// acquireTokenSilent happily returns but the API 401s) would ping-pong
// between /login and the app in an infinite hard-reload loop. A Google
// session can't loop (its token is cleared above), but MSAL state is
// owned by msal-browser and survives the reload, so we need this guard.
const REAUTH_LOGIN_URL = '/login?reauth=expired'

// sessionStorage key set the instant the user starts an interactive
// Microsoft sign-in, and consumed the instant that sign-in completes. It
// is what lets LoginPage tell "the user just signed in for real" apart
// from "a cached MSAL account is lying around" — a distinction the
// ?reauth=expired guard above cannot make on its own, and without which
// a successful sign-in started from a reauth URL can never get into the
// app (MSAL restores the marker URL after the redirect — see LoginPage).
// sessionStorage, not localStorage: the flag belongs to the one tab
// doing the redirect dance, and MSAL's bounce is a same-tab navigation.
const INTERACTIVE_LOGIN_KEY = 'menunest.msal.interactive-login'

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
  clearAppSession()
  clearGoogleToken()
  // A 401 proves the session is unusable, so spend the "just signed in
  // interactively" credit here: otherwise a fresh sign-in whose token the
  // backend still rejects would be waved back into the app by LoginPage
  // and bounce out again on the next 401.
  clearInteractiveLoginStarted()
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

/** Record that an interactive Microsoft sign-in is under way in this tab. */
export function markInteractiveLoginStarted(): void {
  try {
    sessionStorage.setItem(INTERACTIVE_LOGIN_KEY, '1')
  } catch {
    // Storage unavailable (private mode / blocked storage) — the flag only
    // smooths the reauth case, sign-in still works without it.
  }
}

/** Forget any in-flight interactive sign-in (it failed, or was consumed). */
export function clearInteractiveLoginStarted(): void {
  try {
    sessionStorage.removeItem(INTERACTIVE_LOGIN_KEY)
  } catch {
    // See markInteractiveLoginStarted.
  }
}

/**
 * True exactly once per {@link markInteractiveLoginStarted} — reads the
 * flag and clears it. Single-use on purpose: if the backend still rejects
 * the freshly-issued token, the resulting 401 lands on
 * /login?reauth=expired and finds no flag, so it stops there instead of
 * ping-ponging.
 */
export function takeInteractiveLoginStarted(): boolean {
  try {
    const started = sessionStorage.getItem(INTERACTIVE_LOGIN_KEY) !== null
    clearInteractiveLoginStarted()
    return started
  } catch {
    return false
  }
}
