import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { Provider as ReduxProvider } from 'react-redux'
import { MsalProvider } from '@azure/msal-react'
import { EventType, type AccountInfo } from '@azure/msal-browser'
import { GoogleOAuthProvider } from '@react-oauth/google'
import { registerSyncfusionLicense } from './shared/syncfusion/license'

import { msalInstance } from './shared/auth/msalConfig'
import { store } from './store'
import App from './App'
import './shared/telemetry/appInsights'
import {wireGoogleMapsTelemetry} from './shared/telemetry/googleMapsTelemetry'

// Syncfusion *Pure React* (@syncfusion/react-*) theme — Material 3.
//
// IMPORTANT: each package's `styles/material.css` only contains the
// CSS for its own components. The shared theme tokens
// (`--sf-color-*`, `--sf-spacing-*`) live in @syncfusion/react-base
// and MUST be imported FIRST or every other component looks
// unstyled / black-on-white. Then we import every transitive
// component the Scheduler depends on (calendars/dropdowns/lists/
// navigations/popups/buttons/inputs) plus our directly-used ones.
import '@syncfusion/react-base/styles/material.css'
import '@syncfusion/react-buttons/styles/material.css'
import '@syncfusion/react-inputs/styles/material.css'
import '@syncfusion/react-calendars/styles/material.css'
import '@syncfusion/react-lists/styles/material.css'
import '@syncfusion/react-navigations/styles/material.css'
import '@syncfusion/react-dropdowns/styles/material.css'
import '@syncfusion/react-splitbuttons/styles/material.css'
import '@syncfusion/react-popups/styles/material.css'
import '@syncfusion/react-grid/styles/material.css'
import '@syncfusion/react-scheduler/styles/material.css'
import '@syncfusion/ej2-react-interactive-chat/styles/material.css'

import './index.css'

// Service Worker registration — handles web-push notifications for the
// migraine tracker module (Task 15). We delay until the `load` event
// to avoid contending with the critical render. Failures are logged
// but never thrown — push is a strict enhancement, the app must
// continue to work without it (iOS Safari outside PWA mode, locked-
// down enterprise browsers, etc).
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js', { scope: '/' }).then(
      (reg) => {
        console.log('[sw] registered', reg.scope)
      },
      (err) => {
        console.warn('[sw] registration failed', err)
      },
    )
  })
}

// Syncfusion Community License — registered once at app boot, with BOTH the
// Pure React and legacy EJ2 bases (see ./shared/syncfusion/license).
registerSyncfusionLicense(import.meta.env.VITE_SYNCFUSION_LICENSE_KEY)

// Forward Google Maps auth failures + API console errors to App Insights.
// These are console.* / global-callback errors the AI SDK can't autocapture.
wireGoogleMapsTelemetry()

// msal-react's MsalProvider calls handleRedirectPromise internally;
// doing it ourselves as well throws no_token_request_cache_error in
// MSAL v5. We listen for LOGIN_SUCCESS to pick the redirect-returned
// account, and fall back to any cached account on cold loads. In v5
// the LOGIN_SUCCESS payload is AccountInfo (not AuthenticationResult).
async function bootstrap() {
  await msalInstance.initialize()

  msalInstance.addEventCallback((event) => {
    if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
      msalInstance.setActiveAccount(event.payload as AccountInfo)
    }
  })

  const accounts = msalInstance.getAllAccounts()
  if (accounts.length > 0 && !msalInstance.getActiveAccount()) {
    msalInstance.setActiveAccount(accounts[0])
  }

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <GoogleOAuthProvider clientId={import.meta.env.VITE_GOOGLE_CLIENT_ID ?? ''}>
        <MsalProvider instance={msalInstance}>
          <ReduxProvider store={store}>
            <App />
          </ReduxProvider>
        </MsalProvider>
      </GoogleOAuthProvider>
    </StrictMode>,
  )
}

bootstrap()
