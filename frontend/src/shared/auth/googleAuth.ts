const GOOGLE_TOKEN_KEY = 'google_id_token'
const REMEMBER_ME_KEY = 'auth_remember_me'

function isRememberMeEnabled(): boolean {
  return localStorage.getItem(REMEMBER_ME_KEY) === '1'
}

export function getGoogleToken(): string | null {
  return localStorage.getItem(GOOGLE_TOKEN_KEY) ?? sessionStorage.getItem(GOOGLE_TOKEN_KEY)
}

export function setGoogleToken(token: string, rememberMe = isRememberMeEnabled()): void {
  clearGoogleToken()
  if (rememberMe) {
    localStorage.setItem(GOOGLE_TOKEN_KEY, token)
    return
  }
  sessionStorage.setItem(GOOGLE_TOKEN_KEY, token)
}

export function clearGoogleToken(): void {
  localStorage.removeItem(GOOGLE_TOKEN_KEY)
  sessionStorage.removeItem(GOOGLE_TOKEN_KEY)
}

export function isGoogleAuthenticated(): boolean {
  return !!getGoogleToken()
}

export function getRememberMePreference(): boolean {
  return isRememberMeEnabled()
}

export function setRememberMePreference(enabled: boolean): void {
  if (enabled) {
    localStorage.setItem(REMEMBER_ME_KEY, '1')
    return
  }
  localStorage.setItem(REMEMBER_ME_KEY, '0')
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
