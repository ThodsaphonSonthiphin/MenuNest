import { useEffect, useState } from 'react'
import { useMsal, useIsAuthenticated } from '@azure/msal-react'
import { InteractionStatus } from '@azure/msal-browser'
import { Navigate, useNavigate } from 'react-router-dom'
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { GoogleLogin } from '@react-oauth/google'
import { apiScopes, loginRequest } from '../../shared/auth/msalConfig'
import { setGoogleToken, isGoogleAuthenticated } from '../../shared/auth/googleAuth'
import {
  clearInteractiveLoginStarted,
  isReauthBounce,
  markInteractiveLoginStarted,
  takeInteractiveLoginStarted,
} from '../../shared/auth/reauth'
import { setUser } from '../../shared/telemetry/appInsights'
import { exchangeForAppSession } from '../../shared/auth/appSessionApi'

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

  // Exchange the Microsoft provider token for a durable app session (ADR-161)
  // once MSAL has settled with an active account. Exchange is an upgrade,
  // never a gate — sign-in has already succeeded via MSAL either way.
  useEffect(() => {
    if (inProgress !== InteractionStatus.None) return
    if (isAuthenticated) {
      const account = instance.getActiveAccount()
      if (account) {
        instance
          .acquireTokenSilent({ scopes: apiScopes, account })
          .then((r) => exchangeForAppSession(r.accessToken))
          .catch(() => {
            // Exchange is an upgrade, never a gate — sign-in already succeeded.
          })
      }
    }
  }, [isAuthenticated, inProgress, instance])

  // An interactive sign-in that just completed must always be let into
  // the app — even on a ?reauth=expired URL, where the guard below would
  // otherwise re-render this card forever. MSAL caches the page that
  // started the redirect (redirectStartPage → ORIGIN_URI) and, with the
  // default navigateToLoginRequestUrl, hard-navigates back to it before
  // processing the response hash. So a sign-in started from
  // /login?reauth=expired lands back on /login?reauth=expired with the
  // marker intact, the guard suppresses the bounce-to-"/", and the user
  // is stuck clicking a button that authenticates successfully every
  // time and never goes anywhere. handleSignIn below pins the start page
  // to the origin so the bounce doesn't happen at all; this effect is the
  // safety net for any other route back here holding the marker.
  useEffect(() => {
    // msal-react holds inProgress at Startup/HandleRedirect until
    // handleRedirectPromise settles, so None here means the redirect is
    // fully resolved and isAuthenticated is trustworthy.
    if (inProgress !== InteractionStatus.None) return
    if (isAuthenticated) {
      if (takeInteractiveLoginStarted()) {
        navigate('/', { replace: true })
      }
    } else {
      // Settled with no session: whatever interactive attempt got us here
      // is over (declined consent, cancelled, error). Drop the flag so it
      // can't wave a later, unrelated render into the app.
      clearInteractiveLoginStarted()
    }
  }, [isAuthenticated, inProgress, navigate])

  // If the user lands on /login while already authenticated (e.g. via
  // a direct URL or after a stale redirect), send them back into the
  // app instead of making them sign in again. The exception is a 401
  // reauth bounce (?reauth=expired): the backend just rejected this
  // session, so bouncing it back in would loop — show the login UI so
  // the user can re-authenticate (or pick a different account).
  if (
    !isReauthBounce(window.location.search) &&
    inProgress === InteractionStatus.None &&
    (isAuthenticated || isGoogleAuthenticated())
  ) {
    return <Navigate to="/" replace />
  }

  const handleSignIn = () => {
    markInteractiveLoginStarted()
    instance
      .loginRedirect({
        ...loginRequest,
        // Come back to the app root rather than to this /login URL. MSAL
        // defaults redirectStartPage to the current href and returns
        // there before handling the response hash — which, since
        // redirectUri is the bare origin, is always a wasted full reload,
        // and on a ?reauth=expired URL is an infinite loop (the restored
        // marker makes the guard above re-render this card after a
        // *successful* sign-in). Pinning it to the origin makes the
        // landing URL match, so MSAL handles the hash in place.
        redirectStartPage: `${window.location.origin}/`,
      })
      .catch((err) => {
        // The redirect never left the page — don't leave a stale flag
        // behind to wave the next render into the app.
        clearInteractiveLoginStarted()
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
                // Fire-and-forget, matching the Microsoft path below: the
                // exchange is an upgrade, never a gate, and `fetch` has no
                // timeout — awaiting it here would leave a successfully
                // signed-in user stuck on this card if the API is slow or
                // cold-starting.
                void exchangeForAppSession(credentialResponse.credential)
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
