import { Navigate, Outlet } from 'react-router-dom'
import { useCurrentUser } from '../hooks/useCurrentUser'

/**
 * Gate: the user is authenticated and must belong to a family. If
 * they don't, send them to `/join-family` to create one or join an
 * existing one.
 */
export function FamilyRequiredRoute() {
  const { familyId } = useCurrentUser()

  if (!familyId) {
    return <Navigate to="/join-family" replace />
  }

  return <Outlet />
}
