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
import {
  cancelBackgroundNotification,
  fireForegroundNotification,
  playCycleEndSound,
  requestNotificationPermissionOnce,
  scheduleBackgroundNotification,
} from './notifications'

const SCHED_ID = 'pomodoro-cycle-end'

export interface UsePomodoroTimer {
  state: PomodoroState
  remainingMs: number
  start: () => void
  pause: () => void
  resume: () => void
  reset: () => void
  updateSettings: (partial: Partial<PomodoroSettings>) => void
}

const scheduleEndFor = (state: PomodoroState) => {
  if (state.status !== 'running' || state.startedAt == null) return
  const dur = durationMsFor(state.settings, state.mode)
  const fireAt = state.startedAt + dur
  const title = state.mode === 'focus' ? 'Focus done' : 'Break over'
  const body =
    state.mode === 'focus'
      ? 'Time for a break.'
      : 'Back to focus.'
  scheduleBackgroundNotification({ id: SCHED_ID, fireAt, title, body })
}

export function usePomodoroTimer(): UsePomodoroTimer {
  const [state, setState] = useState<PomodoroState>(() => loadState())
  const [now, setNow] = useState<number>(() => Date.now())
  const stateRef = useRef(state)
  stateRef.current = state

  useEffect(() => {
    saveState(state)
  }, [state])

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])

  // Auto-transition + sound + foreground notification when the running
  // cycle reaches zero.
  useEffect(() => {
    if (state.status !== 'running' || state.startedAt == null) return
    const duration = durationMsFor(state.settings, state.mode)
    const elapsed = now - state.startedAt
    if (elapsed < duration) return

    const finishingMode = state.mode
    if (state.settings.soundOn) playCycleEndSound()
    if (state.settings.notifOn) {
      fireForegroundNotification(
        finishingMode === 'focus' ? 'Focus done' : 'Break over',
        finishingMode === 'focus' ? 'Time for a break.' : 'Back to focus.',
      )
    }
    // The foreground tick is firing the notification; cancel any pending
    // SW timer so we don't double-notify.
    cancelBackgroundNotification(SCHED_ID)

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
      const next: PomodoroState = {
        ...prev,
        mode: prev.mode === 'focus' ? 'break' : 'focus',
        startedAt: boundary,
        dailyCount,
      }
      // Schedule the NEXT cycle's background notification immediately.
      scheduleEndFor(next)
      return next
    })
  }, [now, state.status, state.startedAt, state.mode, state.settings])

  // Pause/visible the SW notification when the page is in foreground so
  // we don't get a duplicate. Re-schedule when the tab is hidden.
  useEffect(() => {
    const handler = () => {
      if (document.hidden) {
        scheduleEndFor(stateRef.current)
      } else {
        cancelBackgroundNotification(SCHED_ID)
      }
    }
    document.addEventListener('visibilitychange', handler)
    return () => document.removeEventListener('visibilitychange', handler)
  }, [])

  const start = useCallback(() => {
    void requestNotificationPermissionOnce()
    setState((prev) => {
      if (prev.status === 'running') return prev
      const next: PomodoroState = {
        ...prev,
        status: 'running',
        startedAt: Date.now(),
        pausedRemainingMs: null,
      }
      if (next.settings.notifOn) scheduleEndFor(next)
      return next
    })
  }, [])

  const pause = useCallback(() => {
    setState((prev) => {
      if (prev.status !== 'running') return prev
      const remaining = computeRemainingMs(prev, Date.now())
      cancelBackgroundNotification(SCHED_ID)
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
      const next: PomodoroState = {
        ...prev,
        status: 'running',
        startedAt: Date.now() - elapsed,
        pausedRemainingMs: null,
      }
      if (next.settings.notifOn) scheduleEndFor(next)
      return next
    })
  }, [])

  const reset = useCallback(() => {
    cancelBackgroundNotification(SCHED_ID)
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
        const adjustedStartedAt = Date.now() - (elapsed + (newDuration - oldDuration))
        const next: PomodoroState = {
          ...prev,
          settings: nextSettings,
          startedAt: adjustedStartedAt,
        }
        if (next.settings.notifOn) scheduleEndFor(next)
        else cancelBackgroundNotification(SCHED_ID)
        return next
      }
      if (prev.status === 'paused' && prev.pausedRemainingMs != null) {
        const oldDuration = durationMsFor(prev.settings, prev.mode)
        const newDuration = durationMsFor(nextSettings, prev.mode)
        const newRemaining = Math.max(
          0,
          prev.pausedRemainingMs + (newDuration - oldDuration),
        )
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
