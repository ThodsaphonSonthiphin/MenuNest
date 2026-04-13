import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useIsAuthenticated } from '@azure/msal-react'

/**
 * Gate: requires a signed-in Microsoft account. Unauthenticated users
 * are redirected to the login page, preserving the original path so
 * we can send them back after sign-in.
 */
export function ProtectedRoute() {
  const isAuthenticated = useIsAuthenticated()
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  return <Outlet />
}
