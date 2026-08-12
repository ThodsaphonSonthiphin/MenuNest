import {clearAppSession, getAppSession, storeAppSession, type AppSession} from './appSession'

const API_BASE = import.meta.env.VITE_API_BASE_URL || ''

interface TokenResponse {
  accessToken: string
  expiresIn: number
  refreshToken: string
}

/**
 * Trade a freshly-obtained Microsoft/Google token for a durable app session.
 * Returns false on any failure — the caller must carry on with the provider
 * token, because a failed exchange may never block sign-in.
 */
export async function exchangeForAppSession(providerToken: string): Promise<boolean> {
  try {
    const res = await fetch(`${API_BASE}/api/session/exchange`, {
      method: 'POST',
      headers: {Authorization: `Bearer ${providerToken}`},
    })
    if (!res.ok) return false
    const body = (await res.json()) as TokenResponse
    storeAppSession(body)
    return true
  } catch {
    return false
  }
}

/**
 * Thrown by {@link refreshAppSession} when the request could not be completed
 * at all (network blip, timeout, unparseable response) — as opposed to the
 * server explicitly refusing the refresh token. The caller MUST tell these
 * apart: a refusal means the session is dead and must be cleared; a
 * transient failure means the session may still be perfectly good (the 60s
 * expiry leeway means the old access token can still be valid) and must be
 * preserved rather than discarded (review finding: Task 7, Important 3).
 */
export class TransientSessionError extends Error {}

// Concurrent callers (every RTK Query request's prepareHeaders, plus every
// useAuthDataManager mount) can all observe the same expired session at
// once. The backend refresh grant is strictly single-use — AppSessionStore
// .TakeAsync removes the row on first use and 400s the second — so without
// this guard the losing caller would clear the session the FIRST caller
// just rotated in. Memoise the in-flight rotation so every concurrent
// caller shares the one real request (review finding: Task 7, Critical 1).
let inFlightRefresh: Promise<AppSession | null> | null = null

/**
 * Rotate the session. Returns the rotated session when the server accepts
 * the refresh token, or null when it explicitly refuses it (revoked/expired
 * — the caller must treat the session as dead). Throws
 * {@link TransientSessionError} when the request could not be completed at
 * all; the caller should keep the existing session in that case.
 */
export function refreshAppSession(refreshToken: string): Promise<AppSession | null> {
  if (!inFlightRefresh) {
    inFlightRefresh = doRefresh(refreshToken).finally(() => {
      inFlightRefresh = null
    })
  }
  return inFlightRefresh
}

async function doRefresh(refreshToken: string): Promise<AppSession | null> {
  let res: Response
  try {
    res = await fetch(`${API_BASE}/api/session/refresh`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({refresh_token: refreshToken}),
    })
  } catch {
    // A network blip is not proof the session is dead — surface it
    // distinctly so the caller can fall back to the still-valid stored
    // token instead of discarding a perfectly good, long-lived session.
    throw new TransientSessionError('refresh request failed (network)')
  }

  if (!res.ok) {
    // Any non-200 means the server explicitly refused this refresh token —
    // a malformed/absent body yields a bare 400 in Production and a 500 in
    // Development, so keying off the `invalid_grant` string in the body
    // would strand the user. This IS a definitive refusal.
    clearAppSession()
    return null
  }

  try {
    const body = (await res.json()) as TokenResponse
    storeAppSession(body)
    return getAppSession()
  } catch {
    // The server accepted the refresh but the response body was garbage —
    // treat like any other transient failure, not a refusal.
    throw new TransientSessionError('refresh response was not parseable')
  }
}

/** Best-effort revoke of THIS device's session only (ADR-159). */
export async function revokeAppSession(refreshToken: string): Promise<void> {
  try {
    await fetch(`${API_BASE}/api/session/logout`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({refresh_token: refreshToken}),
    })
  } catch {
    // Ignore — the local clear below is what the user actually sees.
  }
}
