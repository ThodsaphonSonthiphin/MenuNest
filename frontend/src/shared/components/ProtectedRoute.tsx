import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useIsAuthenticated, useMsal } from '@azure/msal-react'
import { InteractionStatus } from '@azure/msal-browser'

/**
 * Gate: requires a signed-in Microsoft account. Unauthenticated users
 * are redirected to the login page, preserving the original path so
 * we can send them back after sign-in.
 *
 * Waits for MSAL to finish any in-flight interaction before deciding.
 * Without this gate, the initial render fires before MsalProvider's
 * internal useEffect publishes the cached accounts, so
 * `useIsAuthenticated()` returns false for one tick and the app
 * bounces authenticated users straight to /login.
 */
export function ProtectedRoute() {
  const isAuthenticated = useIsAuthenticated()
  const { inProgress } = useMsal()
  const location = useLocation()

  if (inProgress !== InteractionStatus.None) {
    // MSAL is still starting up / processing a redirect — hold the
    // guard decision until it settles.
    return <AuthLoadingFallback />
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  return <Outlet />
}

function AuthLoadingFallback() {
  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: 'var(--color-text-muted)',
      }}
    >
      Signing you in…
    </div>
  )
}
