// The MenuNest-minted durable session (ADR-161). These keys are OURS: msal-browser
// v5 encrypts only its own entries with a key held in a session cookie, so anything
// we write under our own names survives a browser restart untouched.
const ACCESS_KEY = 'menunest.session.access'
const REFRESH_KEY = 'menunest.session.refresh'
const EXPIRES_KEY = 'menunest.session.expiresAt'

// Treat a token as expired early so we never send one that dies mid-flight,
// mirroring the leeway in googleAuth.ts.
const EXPIRY_LEEWAY_MS = 60_000

export interface AppSession {
  accessToken: string
  refreshToken: string
  expiresAtMs: number
}

/**
 * All-or-nothing on the way in, matching {@link getAppSession} on the way out.
 * Three separate writes can fail half way (quota exceeded, Safari private mode),
 * leaving a NEW access token beside a STALE refresh token — all three keys
 * present, so getAppSession's presence check waves it through, and the session
 * can then never be renewed. A partial write must degrade to no session.
 */
export function storeAppSession(tokens: {
  accessToken: string
  refreshToken: string
  expiresIn: number
}): void {
  try {
    localStorage.setItem(ACCESS_KEY, tokens.accessToken)
    localStorage.setItem(REFRESH_KEY, tokens.refreshToken)
    localStorage.setItem(EXPIRES_KEY, String(Date.now() + tokens.expiresIn * 1000))
  } catch {
    clearAppSession()
  }
}

/**
 * The stored session, or null when any part of it is missing or unreadable.
 * All-or-nothing on purpose: a half-written session would let a caller send a
 * token it cannot renew.
 */
export function getAppSession(): AppSession | null {
  const accessToken = localStorage.getItem(ACCESS_KEY)
  const refreshToken = localStorage.getItem(REFRESH_KEY)
  const rawExpiry = localStorage.getItem(EXPIRES_KEY)
  if (!accessToken || !refreshToken || !rawExpiry) return null

  const expiresAtMs = Number(rawExpiry)
  if (!Number.isFinite(expiresAtMs)) return null

  return {accessToken, refreshToken, expiresAtMs}
}

export function clearAppSession(): void {
  localStorage.removeItem(ACCESS_KEY)
  localStorage.removeItem(REFRESH_KEY)
  localStorage.removeItem(EXPIRES_KEY)
}

export function isAppSessionExpired(expiresAtMs: number, nowMs: number = Date.now()): boolean {
  return expiresAtMs <= nowMs + EXPIRY_LEEWAY_MS
}

export function hasAppSession(): boolean {
  return getAppSession() !== null
}
