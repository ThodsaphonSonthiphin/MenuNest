// frontend/src/pages/trips/lib/fitPadding.test.ts
import {describe, it, expect} from 'vitest'
import {
  desktopFitPadding,
  mobileFitPadding,
  DESKTOP_COLLAPSED_LEFT_PX,
  DESKTOP_PANEL_LEFT_PX,
  MIN_FIT_STRIP_PX,
  MOBILE_TOP_PX,
  SHEET_CLEARANCE_PX,
} from './fitPadding'

const VH = 660

describe('mobileFitPadding', () => {
  it('clears the floating top bar and the side gutters', () => {
    const p = mobileFitPadding(150, VH)
    expect(p.top).toBe(MOBILE_TOP_PX)
    expect(p.left).toBe(20)
    expect(p.right).toBe(20)
  })

  it('keeps the pins above the sheet at the summary detent', () => {
    expect(mobileFitPadding(150, VH).bottom).toBe(150 + SHEET_CLEARANCE_PX)
  })

  it('clamps the bottom at the full detent so a usable strip always remains', () => {
    // 85% of 660 is 561; 561 + 24 would leave fitBounds no viewport to fit into.
    const p = mobileFitPadding(561, VH)
    expect(p.bottom).toBe(VH - MOBILE_TOP_PX - MIN_FIT_STRIP_PX)
    expect((p.top ?? 0) + (p.bottom ?? 0)).toBeLessThanOrEqual(VH - MIN_FIT_STRIP_PX)
  })

  it('never returns a negative bottom for a nonsense sheet height', () => {
    expect(mobileFitPadding(-50, VH).bottom).toBe(SHEET_CLEARANCE_PX)
  })
})

describe('desktopFitPadding', () => {
  it('clears the floating Plan panel when it is open', () => {
    expect(desktopFitPadding(false).left).toBe(DESKTOP_PANEL_LEFT_PX)
  })

  it('reclaims the canvas when the panel is collapsed', () => {
    expect(desktopFitPadding(true).left).toBe(DESKTOP_COLLAPSED_LEFT_PX)
  })

  it('changes identity with the collapsed state — this is what re-runs fitBounds', () => {
    // The map container does NOT resize when the panel collapses, so ResizeObserver never
    // fires; a new padding object is the only signal FitBounds gets (spec §6.2).
    expect(desktopFitPadding(true)).not.toEqual(desktopFitPadding(false))
  })
})
