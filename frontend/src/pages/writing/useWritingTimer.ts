import { useEffect, useRef, useState } from 'react'
import { computeRemainingMs, isTimerDone } from './writingTimer'

export interface UseWritingTimer {
  remainingMs: number
  isDone: boolean
  startedAtMs: number
}

/**
 * Wall-clock 7-minute timer that starts the instant this hook mounts (the
 * writing page IS the trigger — one-tap-access) and never pauses
 * (timer-resilience): ticking is only ever a re-render, the underlying
 * remaining-time math is pure wall-clock arithmetic, so a screen lock or
 * app switch that stops ticks entirely still shows the correct time left
 * the moment ticking resumes.
 */
export function useWritingTimer(): UseWritingTimer {
  const startedAtRef = useRef<number>(Date.now())
  const [now, setNow] = useState<number>(() => Date.now())

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])

  const startedAtMs = startedAtRef.current
  return {
    remainingMs: computeRemainingMs(startedAtMs, now),
    isDone: isTimerDone(startedAtMs, now),
    startedAtMs,
  }
}
