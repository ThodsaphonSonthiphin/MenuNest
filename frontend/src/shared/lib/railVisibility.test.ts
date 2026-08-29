import {describe, expect, it} from 'vitest'
import {decideRailVisibility, initialRailScrollState} from './railVisibility'

describe('decideRailVisibility', () => {
  it('hides after a downward flick past the threshold', () => {
    const s = decideRailVisibility({...initialRailScrollState, lastY: 100}, {scrollTop: 200, isOpen: false})
    expect(s.hidden).toBe(true)
  })

  it('shows again on an upward flick', () => {
    const s = decideRailVisibility({hidden: true, lastY: 200}, {scrollTop: 100, isOpen: false})
    expect(s.hidden).toBe(false)
  })

  it('ignores jitter below the threshold and does not move the anchor', () => {
    const s = decideRailVisibility({hidden: false, lastY: 100}, {scrollTop: 104, isOpen: false})
    expect(s.hidden).toBe(false)
    expect(s.lastY).toBe(100)
  })

  it('never hides while the dial is open', () => {
    const s = decideRailVisibility({hidden: false, lastY: 100}, {scrollTop: 400, isOpen: true})
    expect(s.hidden).toBe(false)
  })

  it('shows the rail again when the dial opens while hidden', () => {
    const s = decideRailVisibility({hidden: true, lastY: 400}, {scrollTop: 400, isOpen: true})
    expect(s.hidden).toBe(false)
  })

  it('does not hide within the first 40px of the page', () => {
    const s = decideRailVisibility({hidden: false, lastY: 0}, {scrollTop: 30, isOpen: false})
    expect(s.hidden).toBe(false)
  })
})
