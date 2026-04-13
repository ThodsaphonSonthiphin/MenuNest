import { PublicClientApplication, LogLevel } from '@azure/msal-browser'
import type { Configuration } from '@azure/msal-browser'

const clientId = import.meta.env.VITE_MSAL_CLIENT_ID
const authority = import.meta.env.VITE_MSAL_AUTHORITY

if (!clientId) {
  // Intentionally loud — MSAL throws a confusing error later if this is missing.
  // eslint-disable-next-line no-console
  console.warn('VITE_MSAL_CLIENT_ID is not set. See .env.example.')
}

export const msalConfig: Configuration = {
  auth: {
    clientId,
    authority,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: 'sessionStorage',
  },
  system: {
    loggerOptions: {
      logLevel: LogLevel.Warning,
      piiLoggingEnabled: false,
      loggerCallback: (level, message) => {
        if (level <= LogLevel.Error) {
          // eslint-disable-next-line no-console
          console.error(`[MSAL] ${message}`)
        }
      },
    },
  },
}

/** The scope our SPA requests for calling our own API. */
export const apiScopes: string[] = [import.meta.env.VITE_API_SCOPE].filter(Boolean)

/** Scopes requested at login (OpenID Connect basics + our API). */
export const loginRequest = {
  scopes: ['openid', 'profile', 'email', 'offline_access', ...apiScopes],
}

export const msalInstance = new PublicClientApplication(msalConfig)
