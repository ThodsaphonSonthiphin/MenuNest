const GOOGLE_TOKEN_KEY = 'google_id_token'

export function getGoogleToken(): string | null {
  return sessionStorage.getItem(GOOGLE_TOKEN_KEY)
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
 * Decode the payload of a JWT without verification (for display only —
 * the backend validates the token fully).
 */
export function decodeGoogleIdToken(
  token: string,
): { sub: string; email: string; name: string; picture?: string } | null {
  try {
    const payload = token.split('.')[1]
    const decoded = JSON.parse(atob(payload))
    return decoded
  } catch {
    return null
  }
}
