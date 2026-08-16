/** The freewrite duration (daily-unit, habit-mechanics): 7 minutes, fixed. */
export const TIMER_DURATION_MS = 7 * 60 * 1000

/**
 * Wall-clock remaining time. Deliberately takes only a start timestamp and
 * "now" — no pause/resume state exists, per timer-resilience: the timer
 * keeps running through a screen lock or app switch, it never pauses.
 *
 * Bounded to [0, TIMER_DURATION_MS] to handle edge cases like backward system
 * clock adjustments (NTP corrections, manual clock changes, RTC glitches).
 */
export function computeRemainingMs(startedAtMs: number, nowMs: number): number {
  const elapsed = nowMs - startedAtMs
  return Math.min(TIMER_DURATION_MS, Math.max(0, TIMER_DURATION_MS - elapsed))
}

/** True once the full 7 minutes have elapsed since startedAtMs. */
export function isTimerDone(startedAtMs: number, nowMs: number): boolean {
  return computeRemainingMs(startedAtMs, nowMs) <= 0
}
