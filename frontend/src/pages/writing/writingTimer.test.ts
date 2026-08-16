import { describe, expect, it } from 'vitest'
import { TIMER_DURATION_MS, computeRemainingMs, isTimerDone } from './writingTimer'

describe('writingTimer', () => {
  it('TIMER_DURATION_MS is exactly 7 minutes', () => {
    expect(TIMER_DURATION_MS).toBe(7 * 60 * 1000)
  })

  it('computeRemainingMs counts down from the full duration at start', () => {
    const startedAt = 1_000_000
    expect(computeRemainingMs(startedAt, startedAt)).toBe(TIMER_DURATION_MS)
  })

  it('computeRemainingMs decreases as wall-clock time passes, regardless of ticks missed', () => {
    const startedAt = 1_000_000
    // Simulates a screen lock: no ticks fired, but 3 minutes of wall-clock
    // time passed before the next tick — the timer must reflect all of it.
    const threeMinutesLater = startedAt + 3 * 60 * 1000
    expect(computeRemainingMs(startedAt, threeMinutesLater)).toBe(4 * 60 * 1000)
  })

  it('computeRemainingMs never goes negative', () => {
    const startedAt = 1_000_000
    const wayLater = startedAt + TIMER_DURATION_MS + 60 * 60 * 1000
    expect(computeRemainingMs(startedAt, wayLater)).toBe(0)
  })

  it('computeRemainingMs stays bounded to TIMER_DURATION_MS even if the clock jumps backward', () => {
    const startedAt = 1_000_000
    expect(computeRemainingMs(startedAt, startedAt - 1000)).toBe(TIMER_DURATION_MS)
  })

  it('isTimerDone is false before the duration elapses and true at/after it', () => {
    const startedAt = 1_000_000
    expect(isTimerDone(startedAt, startedAt)).toBe(false)
    expect(isTimerDone(startedAt, startedAt + TIMER_DURATION_MS - 1)).toBe(false)
    expect(isTimerDone(startedAt, startedAt + TIMER_DURATION_MS)).toBe(true)
    expect(isTimerDone(startedAt, startedAt + TIMER_DURATION_MS + 1)).toBe(true)
  })
})
