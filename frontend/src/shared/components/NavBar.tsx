import { useEffect, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { useCurrentUser } from '../hooks/useCurrentUser'
import { useBreakpoint } from '../hooks/useBreakpoint'

const navItems = [
  { to: '/recipes', label: 'Recipes' },
  { to: '/stock', label: 'Stock' },
  { to: '/meal-plan', label: 'Meal Plan' },
  { to: '/shopping', label: 'Shopping' },
  { to: '/ai-assistant', label: 'AI' },
]

export function NavBar() {
  const { displayName, signOut } = useCurrentUser()
  const breakpoint = useBreakpoint()
  const location = useLocation()
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [accountOpen, setAccountOpen] = useState(false)

  const isMobile = breakpoint === 'mobile'

  // Close the slide-in drawer whenever the user navigates.
  useEffect(() => {
    setDrawerOpen(false)
    setAccountOpen(false)
  }, [location.pathname])

  return (
    <nav className="app-navbar">
      {isMobile && (
        <button
          type="button"
          className="app-navbar__hamburger"
          onClick={() => setDrawerOpen((v) => !v)}
          aria-label="Toggle menu"
          aria-expanded={drawerOpen}
        >
          ☰
        </button>
      )}

      <NavLink to="/" className="app-navbar__brand">
        🍽️ MenuNest
      </NavLink>

      {!isMobile && (
        <ul className="app-navbar__links">
          {navItems.map((item) => (
            <li key={item.to}>
              <NavLink
                to={item.to}
                className={({ isActive }) =>
                  isActive ? 'app-navbar__link app-navbar__link--active' : 'app-navbar__link'
                }
              >
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      )}

      <div className="app-navbar__right">
        <button
          type="button"
          className="app-navbar__account-trigger"
          onClick={() => setAccountOpen((v) => !v)}
          aria-haspopup="menu"
          aria-expanded={accountOpen}
        >
          {displayName ? displayName.split(' ')[0] : 'Account'} ▾
        </button>
        {accountOpen && (
          <ul className="app-navbar__account-menu" role="menu">
            <li>
              <NavLink to="/ingredients" role="menuitem">
                Manage ingredients
              </NavLink>
            </li>
            <li>
              <NavLink to="/family" role="menuitem">
                Manage family
              </NavLink>
            </li>
            <li>
              <Button
                variant={Variant.Standard}
                color={Color.Secondary}
                onClick={signOut}
                style={{ width: '100%', justifyContent: 'flex-start' }}
              >
                Sign out
              </Button>
            </li>
          </ul>
        )}
      </div>

      {isMobile && drawerOpen && (
        <>
          <div className="app-drawer-backdrop" onClick={() => setDrawerOpen(false)} />
          <aside className="app-drawer" role="dialog" aria-modal="true">
            <ul className="app-drawer__links">
              {navItems.map((item) => (
                <li key={item.to}>
                  <NavLink
                    to={item.to}
                    className={({ isActive }) =>
                      isActive ? 'app-drawer__link app-drawer__link--active' : 'app-drawer__link'
                    }
                  >
                    {item.label}
                  </NavLink>
                </li>
              ))}
              <li className="app-drawer__divider" aria-hidden />
              <li>
                <NavLink to="/ingredients" className="app-drawer__link">
                  Manage ingredients
                </NavLink>
              </li>
              <li>
                <NavLink to="/family" className="app-drawer__link">
                  Manage family
                </NavLink>
              </li>
            </ul>
          </aside>
        </>
      )}
    </nav>
  )
}
