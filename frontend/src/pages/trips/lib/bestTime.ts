import type {BestTimeWindow} from '../../../shared/api/api'

export interface OffWindow {
  nearest: BestTimeWindow
  dir: 'before' | 'after'
  upcoming: BestTimeWindow | null
}

const toMin = (hms: string): number => {
  const [h, m] = hms.slice(0, 5).split(':').map(Number)
  return h * 60 + m
}

/**
 * null when `arrivalMin` is inside ANY window (bounds inclusive). Otherwise the nearest
 * window (smallest time gap), the direction relative to it, and the next window that starts
 * after arrival (if any) — the basis for the off-window Timing flag (ADR-127).
 */
export function resolveBestTime(windows: BestTimeWindow[] | undefined, arrivalMin: number): OffWindow | null {
  const list = windows ?? []
  if (list.length === 0) return null
  for (const win of list) {
    if (arrivalMin >= toMin(win.start) && arrivalMin <= toMin(win.end)) return null
  }
  let nearest = list[0]
  let bestGap = Infinity
  for (const win of list) {
    const s = toMin(win.start)
    const e = toMin(win.end)
    const gap = arrivalMin < s ? s - arrivalMin : arrivalMin - e
    if (gap < bestGap) {
      bestGap = gap
      nearest = win
    }
  }
  const dir: 'before' | 'after' = arrivalMin < toMin(nearest.start) ? 'before' : 'after'
  const upcoming =
    list
      .filter((win) => toMin(win.start) > arrivalMin)
      .sort((a, b) => toMin(a.start) - toMin(b.start))[0] ?? null
  return {nearest, dir, upcoming}
}
