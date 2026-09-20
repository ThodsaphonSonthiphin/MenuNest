// frontend/src/pages/trips/lib/fitPadding.ts
//
// Padding handed to `map.fitBounds` so the active Day's Stops are framed in the part of
// the map that is actually VISIBLE — not underneath the floating top bar, the Plan sheet
// or the Plan panel (spec §6.2). Pure, so the arithmetic is unit-testable in a repo with
// no component harness.

/** Clearance for the floating top bar + Day chips on the mobile surface. */
export const MOBILE_TOP_PX = 88
/** Side gutters on the mobile surface. */
export const MOBILE_SIDE_PX = 20
/** Breathing room between the lowest pin and the top edge of the Plan sheet. */
export const SHEET_CLEARANCE_PX = 24
/** Smallest strip of map fitBounds is allowed to frame into, vertically. */
export const MIN_FIT_STRIP_PX = 80

/** Desktop insets: the Plan panel is 376px wide, inset 16px, so its right edge is at 392. */
export const DESKTOP_EDGE_PX = 28
export const DESKTOP_PANEL_LEFT_PX = 408
export const DESKTOP_COLLAPSED_LEFT_PX = 52

/**
 * Mobile / tablet padding.
 *
 * The bottom padding is the sheet's own visible height plus a little clearance — but it
 * is CLAMPED. At the `full` detent the sheet is ~85% of the viewport, and a padding taller
 * than its own container makes `fitBounds` behave unpredictably (it has no viewport left
 * to fit into). Clamping keeps at least `MIN_FIT_STRIP_PX` of map between the top bar and
 * the sheet, which is exactly the strip §4.1 promises at the `full` detent.
 */
export function mobileFitPadding(sheetHeightPx: number, viewportHeightPx: number): google.maps.Padding {
  const maxBottom = Math.max(
    SHEET_CLEARANCE_PX,
    viewportHeightPx - MOBILE_TOP_PX - MIN_FIT_STRIP_PX,
  )
  const bottom = Math.min(Math.max(0, sheetHeightPx) + SHEET_CLEARANCE_PX, maxBottom)
  return {top: MOBILE_TOP_PX, right: MOBILE_SIDE_PX, bottom, left: MOBILE_SIDE_PX}
}

/**
 * Desktop padding. The Plan panel floats over the LEFT edge of the map, so only the left
 * inset changes when it collapses — the map canvas itself never resizes, which is why
 * `FitBounds`'s ResizeObserver cannot see this change and the padding identity has to
 * drive the re-fit instead.
 */
export function desktopFitPadding(panelCollapsed: boolean): google.maps.Padding {
  return {
    top: DESKTOP_EDGE_PX,
    right: DESKTOP_EDGE_PX,
    bottom: DESKTOP_EDGE_PX,
    left: panelCollapsed ? DESKTOP_COLLAPSED_LEFT_PX : DESKTOP_PANEL_LEFT_PX,
  }
}
