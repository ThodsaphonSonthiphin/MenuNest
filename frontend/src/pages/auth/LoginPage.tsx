import { useEffect, useState } from 'react'
import { useMsal, useIsAuthenticated } from '@azure/msal-react'
import { InteractionStatus } from '@azure/msal-browser'
import { Navigate, useNavigate } from 'react-router-dom'
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { GoogleLogin } from '@react-oauth/google'
import { loginRequest } from '../../shared/auth/msalConfig'
import { setGoogleToken, isGoogleAuthenticated } from '../../shared/auth/googleAuth'
import { setUser } from '../../shared/telemetry/appInsights'

function decodeJwtSub(token: string): string | null {
  try {
    const payload = token.split('.')[1]
    if (!payload) return null
    const json = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')))
    return typeof json.sub === 'string' ? json.sub : null
  } catch { return null }
}

export function LoginPage() {
  const { instance, inProgress } = useMsal()
  const isAuthenticated = useIsAuthenticated()
  const navigate = useNavigate()
  const [googleError, setGoogleError] = useState<string | null>(null)

  // Tag the Microsoft-authenticated user in telemetry once MSAL confirms
  // the active account (covers both fresh redirect and returning sessions).
  useEffect(() => {
    if (isAuthenticated) {
      const account = instance.getActiveAccount()
      if (account?.localAccountId) {
        setUser(account.localAccountId)
      }
    }
  }, [isAuthenticated, instance])

  // If the user lands on /login while already authenticated (e.g. via
  // a direct URL or after a stale redirect), send them back into the
  // app instead of making them sign in again.
  if (inProgress === InteractionStatus.None && (isAuthenticated || isGoogleAuthenticated())) {
    return <Navigate to="/" replace />
  }

  const handleSignIn = () => {
    instance.loginRedirect(loginRequest).catch((err) => {
      // eslint-disable-next-line no-console
      console.error('Sign-in failed', err)
    })
  }

  const pending = inProgress !== InteractionStatus.None

  return (
    <section className="page page--login">
      <div className="login-card">
        <div className="login-card__logo">🪺</div>
        <h1>Nest</h1>
        <p className="login-card__tagline">ทุกเรื่องของครอบครัว รวมไว้ที่เดียว</p>

        <Button
          type="button"
          variant={Variant.Filled}
          color={Color.Primary}
          size={Size.Large}
          onClick={handleSignIn}
          disabled={pending}
          style={{ width: '100%' }}
        >
          {pending ? 'Signing in…' : 'Sign in with Microsoft'}
        </Button>

        <div style={{ textAlign: 'center', color: 'var(--color-text-muted)', margin: '16px 0', fontSize: 14 }}>
          or
        </div>

        <div style={{ display: 'flex', justifyContent: 'center' }}>
          <GoogleLogin
            onSuccess={(credentialResponse) => {
              if (credentialResponse.credential) {
                setGoogleToken(credentialResponse.credential)
                const sub = decodeJwtSub(credentialResponse.credential)
                if (sub) setUser(sub)
                navigate('/', { replace: true })
              }
            }}
            onError={() => setGoogleError('Google sign-in failed. Please try again.')}
            size="large"
            width={320}
            text="signin_with"
          />
        </div>

        {googleError && (
          <p className="field-error" style={{ textAlign: 'center', marginTop: 8 }}>
            {googleError}
          </p>
        )}

        <p className="login-card__footer">
          Sign in with your Microsoft or Google account.
        </p>
      </div>
    </section>
  )
}
