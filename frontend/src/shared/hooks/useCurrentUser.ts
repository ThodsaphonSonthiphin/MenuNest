import { useMsal } from '@azure/msal-react'

/**
 * Thin facade over MSAL's account info + (later) the `/api/me`
 * endpoint. For now it just returns the signed-in account; once the
 * backend `/api/me` is wired up, this hook should also pull family
 * membership and expose it as a single source of truth.
 */
export function useCurrentUser() {
  const { accounts, instance } = useMsal()
  const account = accounts[0] ?? null

  return {
    isAuthenticated: !!account,
    account,
    displayName: account?.name ?? account?.username ?? '',
    email: account?.username ?? '',
    // Placeholder — the real value will come from GET /api/me once the
    // backend is implemented. Until then assume "no family" so the
    // guard routes still redirect through /join-family.
    familyId: null as string | null,
    signOut: () => instance.logoutRedirect(),
  }
}
