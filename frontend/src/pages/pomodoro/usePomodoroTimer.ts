import { useCallback, useEffect, useRef, useState } from 'react'
import {
  computeRemainingMs,
  durationMsFor,
  loadState,
  saveState,
  type PomodoroSettings,
  type PomodoroState,
} from './pomodoroStorage'

export interface UsePomodoroTimer {
  state: PomodoroState
  remainingMs: number
  start: () => void
  pause: () => void
  resume: () => void
  reset: () => void
  updateSettings: (partial: Partial<PomodoroSettings>) => void
}

export function usePomodoroTimer(): UsePomodoroTimer {
  const [state, setState] = useState<PomodoroState>(() => loadState())
  const [now, setNow] = useState<number>(() => Date.now())
  const stateRef = useRef(state)
  stateRef.current = state

  // Persist every state change to localStorage so reload / cross-tab
  // navigation always see the latest snapshot.
  useEffect(() => {
    saveState(state)
  }, [state])

  // Tick once per second while mounted. `now` is the only thing the tick
  // touches — remaining is derived from `state.startedAt` so we don't
  // accumulate drift.
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])

  const start = useCallback(() => {
    setState((prev) => {
      if (prev.status === 'running') return prev
      return {
        ...prev,
        status: 'running',
        startedAt: Date.now(),
        pausedRemainingMs: null,
      }
    })
  }, [])

  const pause = useCallback(() => {
    setState((prev) => {
      if (prev.status !== 'running') return prev
      const remaining = computeRemainingMs(prev, Date.now())
      return {
        ...prev,
        status: 'paused',
        startedAt: null,
        pausedRemainingMs: remaining,
      }
    })
  }, [])

  const resume = useCallback(() => {
    setState((prev) => {
      if (prev.status !== 'paused' || prev.pausedRemainingMs == null) return prev
      const duration = durationMsFor(prev.settings, prev.mode)
      const elapsed = duration - prev.pausedRemainingMs
      return {
        ...prev,
        status: 'running',
        startedAt: Date.now() - elapsed,
        pausedRemainingMs: null,
      }
    })
  }, [])

  const reset = useCallback(() => {
    setState((prev) => ({
      ...prev,
      status: 'idle',
      startedAt: null,
      pausedRemainingMs: null,
    }))
  }, [])

  const updateSettings = useCallback((partial: Partial<PomodoroSettings>) => {
    setState((prev) => ({ ...prev, settings: { ...prev.settings, ...partial } }))
  }, [])

  return {
    state,
    remainingMs: computeRemainingMs(state, now),
    start,
    pause,
    resume,
    reset,
    updateSettings,
  }
}
