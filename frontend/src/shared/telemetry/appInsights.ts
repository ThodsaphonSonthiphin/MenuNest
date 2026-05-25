import {ApplicationInsights} from '@microsoft/applicationinsights-web'
import {ReactPlugin} from '@microsoft/applicationinsights-react-js'

const connectionString = import.meta.env.VITE_APPINSIGHTS_CONNECTION_STRING

export const reactPlugin = new ReactPlugin()

export const appInsights = new ApplicationInsights({
  config: {
    connectionString,
    // If the env var is empty, init the SDK in a sink-less state — calls
    // become no-ops instead of throwing.
    disableTelemetry: !connectionString,
    extensions: [reactPlugin],
    enableAutoRouteTracking: true,
    enableCorsCorrelation: true,
    enableRequestHeaderTracking: true,
    enableResponseHeaderTracking: true,
    autoTrackPageVisitTime: false,
    disableExceptionTracking: false,
  },
})

appInsights.loadAppInsights()

/**
 * Convenience for setting / clearing the signed-in user across the app.
 * Call after auth success; pass `null` on sign-out.
 */
export function setUser(userId: string | null) {
  if (!connectionString) return
  if (userId) appInsights.setAuthenticatedUserContext(userId, undefined, true)
  else appInsights.clearAuthenticatedUserContext()
}
