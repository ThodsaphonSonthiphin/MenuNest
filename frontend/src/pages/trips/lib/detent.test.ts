// frontend/src/pages/trips/lib/detent.test.ts
import {describe, it, expect} from 'vitest'
import {
  detentHeight,
  nearestDetent,
  nextDetent,
  nextDetentLabel,
  TOP_CHROME_PX,
  type Detent,
} from './detent'

// A 660px phone with a ~150px summary header — the mock's own frame dimensions.
const VH = 660
const SUMMARY = 150

describe('nextDetent', () => {
  it('cycles summary → half → full → summary', () => {
    expect(nextDetent('summary')).toBe('half')
    expect(nextDetent('half')).toBe('full')
    expect(nextDetent('full')).toBe('summary')
  })
})

describe('nextDetentLabel', () => {
  it('names the state the tap moves TO, not the current one', () => {
    expect(nextDetentLabel('summary')).toContain('ครึ่งจอ')
    expect(nextDetentLabel('half')).toContain('เต็มจอ')
    expect(nextDetentLabel('full')).toContain('สรุป')
  })
})

describe('detentHeight', () => {
  it('uses the MEASURED summary height at the summary detent', () => {
    expect(detentHeight('summary', VH, SUMMARY)).toBe(SUMMARY)
  })

  it('is 55% / 85% of the viewport for half / full', () => {
    expect(detentHeight('half', VH, SUMMARY)).toBeCloseTo(363)
    expect(detentHeight('full', VH, SUMMARY)).toBeCloseTo(561)
  })

  it('never covers the floating top bar', () => {
    // A very short viewport: 85% of it would otherwise eat the whole top chrome.
    const shortVh = 400
    expect(detentHeight('full', shortVh, 100)).toBe(shortVh - TOP_CHROME_PX)
  })

  it('never lets a taller detent resolve shorter than the one below it', () => {
    // A tall summary header on a short viewport — half's 55% would land under it.
    const h = detentHeight('half', 400, 300)
    expect(h).toBeGreaterThanOrEqual(300)
  })

  it('is monotonic across the cycle', () => {
    const heights = (['summary', 'half', 'full'] as Detent[]).map((d) => detentHeight(d, VH, SUMMARY))
    expect(heights[0]).toBeLessThanOrEqual(heights[1])
    expect(heights[1]).toBeLessThanOrEqual(heights[2])
  })
})

describe('nearestDetent', () => {
  it('settles on the detent the release is closest to', () => {
    expect(nearestDetent(SUMMARY + 10, VH, SUMMARY)).toBe('summary')
    expect(nearestDetent(360, VH, SUMMARY)).toBe('half')
    expect(nearestDetent(555, VH, SUMMARY)).toBe('full')
  })

  it('settles on the SHORTER detent on an exact tie — releasing mid-way never steals map area', () => {
    const summary = detentHeight('summary', VH, SUMMARY)
    const half = detentHeight('half', VH, SUMMARY)
    expect(nearestDetent((summary + half) / 2, VH, SUMMARY)).toBe('summary')
  })

  it('clamps past either end', () => {
    expect(nearestDetent(0, VH, SUMMARY)).toBe('summary')
    expect(nearestDetent(10_000, VH, SUMMARY)).toBe('full')
  })
})
