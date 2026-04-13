import { useMsal, useIsAuthenticated } from '@azure/msal-react'
import { useGetMeQuery } from '../api/api'

/**
 * Thin facade over MSAL account info + the backend `/api/me`
 * endpoint. The backend call auto-provisions a User row on first
 * sign-in and returns the caller's family membership, so this hook
 * is the single source of truth for "who am I, and do I have a
 * family?" across the app.
 *
 * The query is skipped when MSAL hasn't produced an account yet,
 * otherwise RTK Query would fire with no bearer token and 401.
 */
export function useCurrentUser() {
  const { instance, accounts } = useMsal()
  const isAuthenticated = useIsAuthenticated()
  const account = accounts[0] ?? null

  const {
    data: me,
    isLoading: isLoadingProfile,
    isFetching: isFetchingProfile,
    error: profileError,
  } = useGetMeQuery(undefined, { skip: !isAuthenticated })

  return {
    isAuthenticated,
    account,
    // MSAL token claims are available immediately; /api/me reply
    // (source of truth for backend-validated profile) lands a moment
    // later. Prefer the backend value when available.
    displayName: me?.displayName ?? account?.name ?? account?.username ?? '',
    email: me?.email ?? account?.username ?? '',
    userId: me?.userId ?? null,
    familyId: me?.familyId ?? null,
    familyName: me?.familyName ?? null,
    familyInviteCode: me?.familyInviteCode ?? null,
    // True until /api/me has replied at least once for this session.
    isLoadingProfile: isAuthenticated && (isLoadingProfile || isFetchingProfile && !me),
    profileError,
    signOut: () => instance.logoutRedirect(),
  }
}
