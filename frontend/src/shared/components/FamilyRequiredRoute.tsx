import { Navigate, Outlet } from 'react-router-dom'
import { useCurrentUser } from '../hooks/useCurrentUser'

/**
 * Gate: the user is authenticated and must belong to a family. Waits
 * for `/api/me` to reply before deciding — without this delay, the
 * first render fires with `familyId: null` (no data yet) and sends
 * the user to `/join-family` even if they already belong to one.
 */
export function FamilyRequiredRoute() {
  const { familyId, isLoadingProfile, profileError } = useCurrentUser()

  if (isLoadingProfile) {
    return <ProfileLoadingFallback />
  }

  if (profileError) {
    return <ProfileErrorFallback />
  }

  if (!familyId) {
    return <Navigate to="/join-family" replace />
  }

  return <Outlet />
}

function ProfileLoadingFallback() {
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
      Loading your profile…
    </div>
  )
}

function ProfileErrorFallback() {
  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        flexDirection: 'column',
        gap: 12,
        color: 'var(--color-danger)',
      }}
    >
      <p>Could not load your profile.</p>
      <button
        type="button"
        className="btn btn--outline"
        onClick={() => window.location.reload()}
      >
        Try again
      </button>
    </div>
  )
}
