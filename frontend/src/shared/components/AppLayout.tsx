import { Outlet } from 'react-router-dom'
import { NavBar } from './NavBar'
import { ConfirmProvider } from './ConfirmProvider'

export function AppLayout() {
  return (
    <ConfirmProvider>
      <div className="app-shell">
        <NavBar />
        <main className="app-main">
          <Outlet />
        </main>
      </div>
    </ConfirmProvider>
  )
}
