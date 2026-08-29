import {createContext, useCallback, useMemo, useState, type ReactNode} from 'react'
import {ShortcutRail} from './ShortcutRail'

export interface RailAction {
  key: string
  label: string
  /** A single character rendered as the item's text prefix. */
  icon: string
  /** Desktop-only keyboard hint, e.g. "⌘Z" (menunest-200). */
  hint?: string
  disabled?: boolean
  onPress: () => void
}

export interface RailDeclaration {
  actions: RailAction[]
}

interface RailContextValue {
  declare: (d: RailDeclaration | null) => void
}

export const ShortcutRailContext = createContext<RailContextValue | null>(null)

/**
 * Mirrors ConfirmProvider, which sits immediately outside this in AppLayout:
 * a cross-cutting UI capability any page can opt into (menunest-199). A page
 * that declares nothing gets no rail — which is why /budget can have one while
 * AccountDetailPage, whose bottom-right corner is taken by `.bdg-fab`, simply
 * does not, and the corner collision never has to be resolved.
 */
export function ShortcutRailProvider({children}: {children: ReactNode}) {
  const [declaration, setDeclaration] = useState<RailDeclaration | null>(null)

  const declare = useCallback((d: RailDeclaration | null) => setDeclaration(d), [])
  const value = useMemo<RailContextValue>(() => ({declare}), [declare])

  return (
    <ShortcutRailContext.Provider value={value}>
      {children}
      {declaration && <ShortcutRail actions={declaration.actions} />}
    </ShortcutRailContext.Provider>
  )
}
