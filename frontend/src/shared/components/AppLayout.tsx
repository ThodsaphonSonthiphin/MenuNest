import { Outlet } from 'react-router-dom'
import { AppInsightsErrorBoundary } from '@microsoft/applicationinsights-react-js'
import { NavBar } from './NavBar'
import { ConfirmProvider } from './ConfirmProvider'
import { reactPlugin } from '../telemetry/appInsights'

function TelemetryErrorFallback() {
  return (
    <div style={{ padding: 32, textAlign: 'center' }}>
      <p>Something went wrong while rendering this page.</p>
      <button type="button" onClick={() => window.location.reload()}>Reload</button>
    </div>
  )
}

export function AppLayout() {
  return (
    <AppInsightsErrorBoundary appInsights={reactPlugin} onError={TelemetryErrorFallback}>
      <ConfirmProvider>
        <div className="app-shell">
          <NavBar />
          <main className="app-main">
            <Outlet />
          </main>
        </div>
      </ConfirmProvider>
    </AppInsightsErrorBoundary>
  )
}
