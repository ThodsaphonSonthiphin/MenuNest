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

/** Rotate the session. Returns null when the server refused it (revoked/expired). */
export async function refreshAppSession(refreshToken: string): Promise<AppSession | null> {
  try {
    const res = await fetch(`${API_BASE}/api/session/refresh`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({refresh_token: refreshToken}),
    })
    if (!res.ok) {
      // Any non-200 means the session is dead — a malformed/absent body
      // yields a bare 400 in Production and a 500 in Development, so keying
      // off the `invalid_grant` string in the body would strand the user.
      clearAppSession()
      return null
    }
    const body = (await res.json()) as TokenResponse
    storeAppSession(body)
    return getAppSession()
  } catch {
    // A network blip must not sign the user out — keep the session and retry later.
    return null
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
