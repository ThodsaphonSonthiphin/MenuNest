export type PomodoroStatus = 'idle' | 'running' | 'paused'
export type PomodoroMode = 'focus' | 'break'

export interface PomodoroSettings {
  focusMin: number
  breakMin: number
  soundOn: boolean
  notifOn: boolean
}

export interface PomodoroDailyCount {
  date: string // YYYY-MM-DD in local time
  focusCompleted: number
}

export interface PomodoroState {
  status: PomodoroStatus
  mode: PomodoroMode
  startedAt: number | null
  pausedRemainingMs: number | null
  settings: PomodoroSettings
  dailyCount: PomodoroDailyCount
}

const STORAGE_KEY = 'menunest:pomodoro:v1'

const DEFAULT_SETTINGS: PomodoroSettings = {
  focusMin: 25,
  breakMin: 5,
  soundOn: true,
  notifOn: true,
}

export const todayKey = (now: number = Date.now()): string => {
  const d = new Date(now)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

export const defaultState = (now: number = Date.now()): PomodoroState => ({
  status: 'idle',
  mode: 'focus',
  startedAt: null,
  pausedRemainingMs: null,
  settings: { ...DEFAULT_SETTINGS },
  dailyCount: { date: todayKey(now), focusCompleted: 0 },
})

export const loadState = (now: number = Date.now()): PomodoroState => {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return defaultState(now)
    const parsed = JSON.parse(raw) as Partial<PomodoroState>
    // Reject anything that doesn't look like our v1 shape — falling back
    // is safer than rendering with half-populated state.
    if (
      !parsed ||
      typeof parsed !== 'object' ||
      !parsed.settings ||
      typeof parsed.settings.focusMin !== 'number'
    ) {
      return defaultState(now)
    }
    const today = todayKey(now)
    const dailyCount =
      parsed.dailyCount && parsed.dailyCount.date === today
        ? parsed.dailyCount
        : { date: today, focusCompleted: 0 }
    return {
      status: parsed.status ?? 'idle',
      mode: parsed.mode ?? 'focus',
      startedAt: parsed.startedAt ?? null,
      pausedRemainingMs: parsed.pausedRemainingMs ?? null,
      settings: { ...DEFAULT_SETTINGS, ...parsed.settings },
      dailyCount,
    }
  } catch {
    return defaultState(now)
  }
}

export const saveState = (state: PomodoroState): void => {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state))
  } catch {
    /* quota / disabled — best-effort */
  }
}

export const clearState = (): void => {
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    /* ignore */
  }
}

export const durationMsFor = (settings: PomodoroSettings, mode: PomodoroMode): number =>
  (mode === 'focus' ? settings.focusMin : settings.breakMin) * 60_000

export const computeRemainingMs = (state: PomodoroState, now: number): number => {
  const duration = durationMsFor(state.settings, state.mode)
  if (state.status === 'running' && state.startedAt != null) {
    return Math.max(0, duration - (now - state.startedAt))
  }
  if (state.status === 'paused' && state.pausedRemainingMs != null) {
    return state.pausedRemainingMs
  }
  return duration
}
