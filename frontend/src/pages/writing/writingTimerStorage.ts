export interface WritingTimerState {
  date: string // YYYY-MM-DD, local time
  startedAtMs: number
}

const STORAGE_KEY = 'menunest:writing:v1'

export const todayKey = (now: number = Date.now()): string => {
  const d = new Date(now)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/**
 * Returns today's persisted start timestamp if one exists for today's date,
 * else starts (and persists) a fresh one at `now`. A stale entry from a
 * different date is treated as absent — each day gets its own session,
 * matching the habit's daily-reset nature.
 */
export const loadOrStartTodaysSession = (now: number = Date.now()): number => {
  const today = todayKey(now)
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) {
      const parsed = JSON.parse(raw) as Partial<WritingTimerState>
      if (parsed && typeof parsed.startedAtMs === 'number' && parsed.date === today) {
        return parsed.startedAtMs
      }
    }
  } catch {
    /* corrupt storage — fall through to a fresh session */
  }
  const state: WritingTimerState = { date: today, startedAtMs: now }
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state))
  } catch {
    /* quota / disabled — best-effort, timer still works in-memory this load */
  }
  return now
}
