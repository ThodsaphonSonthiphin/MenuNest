import { useMsal, useIsAuthenticated } from '@azure/msal-react'
import { InteractionStatus } from '@azure/msal-browser'
import { Navigate } from 'react-router-dom'
import { loginRequest } from '../../shared/auth/msalConfig'

export function LoginPage() {
  const { instance, inProgress } = useMsal()
  const isAuthenticated = useIsAuthenticated()

  // If the user lands on /login while already authenticated (e.g. via
  // a direct URL or after a stale redirect), send them back into the
  // app instead of making them sign in again.
  if (inProgress === InteractionStatus.None && isAuthenticated) {
    return <Navigate to="/" replace />
  }

  const handleSignIn = () => {
    instance.loginRedirect(loginRequest).catch((err) => {
      // eslint-disable-next-line no-console
      console.error('Sign-in failed', err)
    })
  }

  return (
    <section className="page page--login">
      <div className="login-card">
        <div className="login-card__logo">🍽️</div>
        <h1>MenuNest</h1>
        <p className="login-card__tagline">วางแผนมื้ออาหารกับครอบครัว</p>

        <button
          type="button"
          className="btn btn--microsoft"
          onClick={handleSignIn}
          disabled={inProgress !== InteractionStatus.None}
        >
          {inProgress === InteractionStatus.None ? 'Sign in with Microsoft' : 'Signing in…'}
        </button>

        <p className="login-card__footer">
          Supports work, school, and personal Microsoft accounts.
        </p>
      </div>
    </section>
  )
}
