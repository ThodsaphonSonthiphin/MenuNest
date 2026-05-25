import {ApplicationInsights} from '@microsoft/applicationinsights-web'
import {ReactPlugin} from '@microsoft/applicationinsights-react-js'

const connectionString = import.meta.env.VITE_APPINSIGHTS_CONNECTION_STRING

export const reactPlugin = new ReactPlugin()

/**
 * No-op shim used when the connection string is missing (local dev,
 * forgotten CI secret). `disableTelemetry: true` is NOT enough — the
 * SDK's `loadAppInsights()` still throws "Please provide instrumentation
 * key" before it ever consults the flag. So we just skip construction
 * entirely and hand callers a stub.
 */
function createNoOpInsights(): ApplicationInsights {
  const noop = () => {}
  return {
    trackException: noop,
    trackTrace: noop,
    trackEvent: noop,
    trackPageView: noop,
    trackMetric: noop,
    trackDependencyData: noop,
    flush: noop,
    setAuthenticatedUserContext: noop,
    clearAuthenticatedUserContext: noop,
    loadAppInsights: noop,
  } as unknown as ApplicationInsights
}

export const appInsights: ApplicationInsights = connectionString
  ? (() => {
      const ai = new ApplicationInsights({
        config: {
          connectionString,
          extensions: [reactPlugin],
          enableAutoRouteTracking: true,
          enableCorsCorrelation: true,
          enableRequestHeaderTracking: true,
          enableResponseHeaderTracking: true,
          autoTrackPageVisitTime: false,
          disableExceptionTracking: false,
        },
      })
      ai.loadAppInsights()
      return ai
    })()
  : createNoOpInsights()

/**
 * Convenience for setting / clearing the signed-in user across the app.
 * Call after auth success; pass `null` on sign-out.
 */
export function setUser(userId: string | null) {
  if (!connectionString) return
  if (userId) appInsights.setAuthenticatedUserContext(userId, undefined, true)
  else appInsights.clearAuthenticatedUserContext()
}
