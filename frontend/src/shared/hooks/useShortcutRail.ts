import {useContext, useEffect} from 'react'
import {ShortcutRailContext, type RailDeclaration} from '../components/ShortcutRailProvider'

/**
 * Declares this page's shortcut rail for as long as the page is mounted, and
 * clears it on unmount so a rail never outlives the page that asked for it.
 *
 * The `declaration` must be memoised by the caller — an object literal rebuilt
 * every render would re-run this effect every render.
 */
export function useShortcutRail(declaration: RailDeclaration | null) {
  const ctx = useContext(ShortcutRailContext)
  if (!ctx) throw new Error('useShortcutRail must be used inside ShortcutRailProvider')

  const {declare} = ctx
  useEffect(() => {
    declare(declaration)
    return () => declare(null)
  }, [declare, declaration])
}
