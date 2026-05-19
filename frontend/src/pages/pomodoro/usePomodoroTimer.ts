import { useCallback, useEffect, useRef, useState } from 'react'
import {
  computeRemainingMs,
  durationMsFor,
  loadState,
  saveState,
  todayKey,
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

  // When a running cycle reaches zero, flip the mode, reset startedAt to
  // the boundary instant, and (focus only) increment dailyCount.
  useEffect(() => {
    if (state.status !== 'running' || state.startedAt == null) return
    const duration = durationMsFor(state.settings, state.mode)
    const elapsed = now - state.startedAt
    if (elapsed < duration) return

    setState((prev) => {
      if (prev.status !== 'running' || prev.startedAt == null) return prev
      const dur = durationMsFor(prev.settings, prev.mode)
      const boundary = prev.startedAt + dur
      const justCompletedFocus = prev.mode === 'focus'
      const today = todayKey(boundary)
      const dailyCount =
        prev.dailyCount.date === today
          ? {
              date: today,
              focusCompleted:
                prev.dailyCount.focusCompleted + (justCompletedFocus ? 1 : 0),
            }
          : { date: today, focusCompleted: justCompletedFocus ? 1 : 0 }
      return {
        ...prev,
        mode: prev.mode === 'focus' ? 'break' : 'focus',
        startedAt: boundary,
        dailyCount,
      }
    })
  }, [now, state.status, state.startedAt, state.mode, state.settings])

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
    setState((prev) => {
      const nextSettings = { ...prev.settings, ...partial }
      if (prev.status === 'running' && prev.startedAt != null) {
        const oldDuration = durationMsFor(prev.settings, prev.mode)
        const newDuration = durationMsFor(nextSettings, prev.mode)
        const elapsed = oldDuration - computeRemainingMs(prev, Date.now())
        // Anchor startedAt so the current remaining is preserved when
        // duration changes mid-cycle.
        const adjustedStartedAt = Date.now() - (elapsed + (newDuration - oldDuration))
        return { ...prev, settings: nextSettings, startedAt: adjustedStartedAt }
      }
      if (prev.status === 'paused' && prev.pausedRemainingMs != null) {
        const oldDuration = durationMsFor(prev.settings, prev.mode)
        const newDuration = durationMsFor(nextSettings, prev.mode)
        const newRemaining = Math.max(0, prev.pausedRemainingMs + (newDuration - oldDuration))
        return { ...prev, settings: nextSettings, pausedRemainingMs: newRemaining }
      }
      return { ...prev, settings: nextSettings }
    })
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
