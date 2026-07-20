export type HomeOption = {
  path: string
  label: string
  requiresFamily: boolean
}

/** Top-level NavBar pages eligible as a Home page (ADR-084). */
export const HOME_OPTIONS: HomeOption[] = [
  { path: '/health', label: 'Health', requiresFamily: false },
  { path: '/pomodoro', label: 'Pomodoro', requiresFamily: false },
  { path: '/trips', label: 'Trips', requiresFamily: false },
  { path: '/discover', label: 'ไปไหนดี', requiresFamily: false },
  { path: '/recipes', label: 'Recipes', requiresFamily: true },
  { path: '/stock', label: 'Stock', requiresFamily: true },
  { path: '/meal-plan', label: 'Meal Plan', requiresFamily: true },
  { path: '/shopping', label: 'Shopping', requiresFamily: true },
  { path: '/budget', label: 'Budget', requiresFamily: true },
  { path: '/ai-assistant', label: 'AI', requiresFamily: true },
]

const DEFAULT_HOME = '/budget'

/** The family-aware selectable set: hide family-gated pages when the user has no family. */
export function homeOptions(hasFamily: boolean): HomeOption[] {
  return HOME_OPTIONS.filter((o) => hasFamily || !o.requiresFamily)
}

/**
 * Resolve where "/" should land: the stored HomePath when it is a known
 * home-eligible route, else the default (/budget). Family gating of the
 * resolved route is left to the route guards (ADR-084), keeping this a
 * loop-proof pure lookup.
 */
export function resolveHomePath(homePath: string | null | undefined): string {
  if (homePath && HOME_OPTIONS.some((o) => o.path === homePath)) {
    return homePath
  }
  return DEFAULT_HOME
}
