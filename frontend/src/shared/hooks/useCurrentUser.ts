import { useMsal, useIsAuthenticated } from '@azure/msal-react'
import { useGetMeQuery } from '../api/api'
import { isGoogleAuthenticated, getGoogleToken, decodeGoogleIdToken, clearGoogleToken } from '../auth/googleAuth'
import { clearAppSession, getAppSession, hasAppSession } from '../auth/appSession'
import { revokeAppSession } from '../auth/appSessionApi'

/**
 * Thin facade over MSAL account info + the backend `/api/me`
 * endpoint. The backend call auto-provisions a User row on first
 * sign-in and returns the caller's family membership, so this hook
 * is the single source of truth for "who am I, and do I have a
 * family?" across the app.
 *
 * The query is skipped when neither MSAL, Google, nor our own durable app
 * session has produced credentials yet, otherwise RTK Query would fire with
 * no bearer token and 401.
 *
 * The app-session check matters on its own: after a browser restart,
 * msal-browser v5 has purged its cache (ADR-161), so a Microsoft user who
 * is still signed in has ONLY the app session — no MSAL account, no Google
 * token. Without checking it here, this hook would report the user as
 * signed out even though ProtectedRoute let them in, `/api/me` would never
 * fire, and FamilyRequiredRoute would wrongly bounce them to /join-family.
 */
export function useCurrentUser() {
  const { instance, accounts } = useMsal()
  const isMsalAuth = useIsAuthenticated()
  const account = accounts[0] ?? null

  const isAuthenticated = isMsalAuth || isGoogleAuthenticated() || hasAppSession()

  const {
    data: me,
    isLoading: isLoadingProfile,
    isFetching: isFetchingProfile,
    error: profileError,
  } = useGetMeQuery(undefined, { skip: !isAuthenticated })

  // Decode Google token for immediate display (before /api/me responds)
  const googleToken = getGoogleToken()
  const googleUser = googleToken ? decodeGoogleIdToken(googleToken) : null

  const signOut = async () => {
    const session = getAppSession()
    if (session) await revokeAppSession(session.refreshToken)
    clearAppSession()
    clearGoogleToken()
    if (isMsalAuth) {
      instance.logoutRedirect()
    } else {
      window.location.href = '/login'
    }
  }

  return {
    isAuthenticated,
    account,
    displayName: me?.displayName ?? account?.name ?? googleUser?.name ?? '',
    email: me?.email ?? account?.username ?? googleUser?.email ?? '',
    userId: me?.userId ?? null,
    familyId: me?.familyId ?? null,
    homePath: me?.homePath ?? null,
    activeTargetRule: me?.activeTargetRule ?? null,
    uvWarnThreshold: me?.uvWarnThreshold ?? null,
    feelsLikeWarnThreshold: me?.feelsLikeWarnThreshold ?? null,
    familyName: me?.familyName ?? null,
    familyInviteCode: me?.familyInviteCode ?? null,
    authProvider: me?.authProvider ?? (isMsalAuth ? 'Microsoft' : googleUser ? 'Google' : null),
    isLoadingProfile: isAuthenticated && (isLoadingProfile || (isFetchingProfile && !me)),
    profileError,
    signOut,
  }
}
