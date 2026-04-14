import { useMsal, useIsAuthenticated } from '@azure/msal-react'
import { useGetMeQuery } from '../api/api'
import { isGoogleAuthenticated, getGoogleToken, decodeGoogleIdToken, clearGoogleToken } from '../auth/googleAuth'

/**
 * Thin facade over MSAL account info + the backend `/api/me`
 * endpoint. The backend call auto-provisions a User row on first
 * sign-in and returns the caller's family membership, so this hook
 * is the single source of truth for "who am I, and do I have a
 * family?" across the app.
 *
 * The query is skipped when neither MSAL nor Google has produced a
 * session yet, otherwise RTK Query would fire with no bearer token
 * and 401.
 */
export function useCurrentUser() {
  const { instance, accounts } = useMsal()
  const isMsalAuth = useIsAuthenticated()
  const account = accounts[0] ?? null

  const isAuthenticated = isMsalAuth || isGoogleAuthenticated()

  const {
    data: me,
    isLoading: isLoadingProfile,
    isFetching: isFetchingProfile,
    error: profileError,
  } = useGetMeQuery(undefined, { skip: !isAuthenticated })

  // Decode Google token for immediate display (before /api/me responds)
  const googleToken = getGoogleToken()
  const googleUser = googleToken ? decodeGoogleIdToken(googleToken) : null

  const signOut = () => {
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
    familyName: me?.familyName ?? null,
    familyInviteCode: me?.familyInviteCode ?? null,
    authProvider: me?.authProvider ?? (isMsalAuth ? 'Microsoft' : googleUser ? 'Google' : null),
    isLoadingProfile: isAuthenticated && (isLoadingProfile || (isFetchingProfile && !me)),
    profileError,
    signOut,
  }
}
