import { useEffect, useState } from 'react'

export type Breakpoint = 'mobile' | 'tablet' | 'desktop'

const TABLET_MIN = 640
const DESKTOP_MIN = 1024

function detect(): Breakpoint {
  if (typeof window === 'undefined') return 'desktop'
  const w = window.innerWidth
  if (w < TABLET_MIN) return 'mobile'
  if (w < DESKTOP_MIN) return 'tablet'
  return 'desktop'
}

/**
 * Subscribes to viewport width changes and returns a coarse
 * breakpoint label. Components use this to swap layouts (Scheduler
 * view, NavBar drawer state, dialog full-screen, etc.) without
 * sprinkling `window.innerWidth` checks across the app.
 */
export function useBreakpoint(): Breakpoint {
  const [bp, setBp] = useState<Breakpoint>(detect)

  useEffect(() => {
    const onResize = () => setBp(detect())
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])

  return bp
}

export const BP_TABLET_MIN = TABLET_MIN
export const BP_DESKTOP_MIN = DESKTOP_MIN
