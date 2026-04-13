import { NavLink } from 'react-router-dom'
import { useCurrentUser } from '../hooks/useCurrentUser'

const navItems = [
  { to: '/recipes', label: 'Recipes' },
  { to: '/stock', label: 'Stock' },
  { to: '/meal-plan', label: 'Meal Plan' },
  { to: '/shopping', label: 'Shopping' },
]

export function NavBar() {
  const { displayName, signOut } = useCurrentUser()

  return (
    <nav className="app-navbar">
      <NavLink to="/" className="app-navbar__brand">
        🍽️ MenuNest
      </NavLink>

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

      <div className="app-navbar__right">
        <details className="app-navbar__menu">
          <summary>{displayName || 'Account'}</summary>
          <ul>
            <li>
              <NavLink to="/ingredients">Manage ingredients</NavLink>
            </li>
            <li>
              <NavLink to="/family">Manage family</NavLink>
            </li>
            <li>
              <button type="button" onClick={signOut}>
                Sign out
              </button>
            </li>
          </ul>
        </details>
      </div>
    </nav>
  )
}
