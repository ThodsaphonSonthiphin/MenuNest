import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { Provider as ReduxProvider } from 'react-redux'
import { MsalProvider } from '@azure/msal-react'
import { registerLicense } from '@syncfusion/ej2-base'

import { msalInstance } from './shared/auth/msalConfig'
import { store } from './store'
import App from './App'

import './index.css'

// Syncfusion Community License — registered once at app boot.
const syncfusionLicense = import.meta.env.VITE_SYNCFUSION_LICENSE_KEY
if (syncfusionLicense) {
  registerLicense(syncfusionLicense)
}

// Ensure MSAL has picked up any redirect response before rendering.
// `initialize()` bootstraps the client; `handleRedirectPromise()`
// actually consumes the auth response in `window.location.hash`
// after Microsoft redirects the user back here. Without this call,
// tokens are never parsed and the app treats the user as anonymous.
async function bootstrap() {
  await msalInstance.initialize()

  try {
    const redirectResult = await msalInstance.handleRedirectPromise()
    if (redirectResult?.account) {
      msalInstance.setActiveAccount(redirectResult.account)
    }
  } catch (err) {
    // eslint-disable-next-line no-console
    console.error('[MSAL] handleRedirectPromise failed', err)
  }

  const accounts = msalInstance.getAllAccounts()
  if (accounts.length > 0 && !msalInstance.getActiveAccount()) {
    msalInstance.setActiveAccount(accounts[0])
  }

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <MsalProvider instance={msalInstance}>
        <ReduxProvider store={store}>
          <App />
        </ReduxProvider>
      </MsalProvider>
    </StrictMode>,
  )
}

bootstrap()
