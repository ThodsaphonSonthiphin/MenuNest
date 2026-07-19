import { Navigate } from 'react-router-dom'
import { useCurrentUser } from '../hooks/useCurrentUser'
import { resolveHomePath } from '../../pages/settings/homeOptions'

/**
 * Resolves "/" to the user's chosen Home page. Waits for /api/me to load
 * (so a family member is not briefly sent to the /budget default before
 * their homePath is known), then redirects. Route guards
 * (ProtectedRoute / FamilyRequiredRoute) then apply to the target.
 */
export function HomeRedirect() {
  const { homePath, isLoadingProfile } = useCurrentUser()

  if (isLoadingProfile) {
    return null
  }

  return <Navigate to={resolveHomePath(homePath)} replace />
}
