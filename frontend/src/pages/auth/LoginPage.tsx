import { useMsal } from '@azure/msal-react'
import { loginRequest } from '../../shared/auth/msalConfig'

export function LoginPage() {
  const { instance } = useMsal()

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

        <button type="button" className="btn btn--microsoft" onClick={handleSignIn}>
          Sign in with Microsoft
        </button>

        <p className="login-card__footer">
          Supports work, school, and personal Microsoft accounts.
        </p>
      </div>
    </section>
  )
}
