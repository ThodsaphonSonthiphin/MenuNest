const GOOGLE_TOKEN_KEY = 'google_id_token'

// Treat a token as expired slightly early so we never send one that
// would die mid-flight. Google ID tokens live ~1h, so 30s is safe.
const EXPIRY_LEEWAY_SECONDS = 30

// All fields optional: decodeGoogleIdToken does an unchecked cast of an
// unverified payload, so callers must treat every claim as possibly
// absent (a malformed or non-Google JWT can parse to an arbitrary
// object). Real Google ID tokens always carry sub/email/name.
export interface GoogleIdTokenPayload {
  sub?: string
  email?: string
  name?: string
  picture?: string
  exp?: number
  iat?: number
}

export function getGoogleToken(): string | null {
  const token = sessionStorage.getItem(GOOGLE_TOKEN_KEY)
  if (!token) return null
  // Self-heal: a present-but-expired token is worse than no token — it
  // sails past presence-only guards and then 401s every API call. Drop
  // it so callers (route guard, header builder) treat us as signed out.
  if (isGoogleTokenExpired(token)) {
    sessionStorage.removeItem(GOOGLE_TOKEN_KEY)
    return null
  }
  return token
}

export function setGoogleToken(token: string): void {
  sessionStorage.setItem(GOOGLE_TOKEN_KEY, token)
}

export function clearGoogleToken(): void {
  sessionStorage.removeItem(GOOGLE_TOKEN_KEY)
}

export function isGoogleAuthenticated(): boolean {
  return !!getGoogleToken()
}

/**
 * True when the token is unreadable, carries no `exp`, or `exp` is in
 * the past (with a small leeway). Such a token must trigger re-auth
 * rather than be sent and rejected with a 401.
 */
export function isGoogleTokenExpired(token: string): boolean {
  const payload = decodeGoogleIdToken(token)
  if (!payload || typeof payload.exp !== 'number') return true
  const nowSeconds = Date.now() / 1000
  return payload.exp <= nowSeconds + EXPIRY_LEEWAY_SECONDS
}

/**
 * Decode the payload of a JWT without verification (for display/expiry
 * only — the backend validates the token fully). Handles base64url and
 * missing padding, the shape Google emits.
 */
export function decodeGoogleIdToken(token: string): GoogleIdTokenPayload | null {
  try {
    const part = token.split('.')[1]
    if (!part) return null
    const b64 = part.replace(/-/g, '+').replace(/_/g, '/')
    const padded = b64.padEnd(Math.ceil(b64.length / 4) * 4, '=')
    return JSON.parse(atob(padded)) as GoogleIdTokenPayload
  } catch {
    return null
  }
}
