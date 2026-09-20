// frontend/src/shared/components/BottomSheet.tsx
//
// The **Plan sheet**'s container (menunest-234) — written as a SHARED component rather than
// inside the trips page so Discover can adopt it later without a second implementation
// existing (menunest-237).
//
// Three things are definitional and must not be "improved" without revisiting the ADR:
//
//  1. **Non-modal.** There is no scrim, and nothing here covers the map outside the sheet's
//     own box, so the map keeps pan, zoom and tap at every detent.
//  2. **Drag from the handle and the header only.** The body is a plain scroll container.
//     This deliberately sidesteps nested drag/scroll hand-off, which is the classic source
//     of janky bottom sheets (spec §4.2). A future round may add drag-from-top-of-scroll.
//  3. **The sheet is always rendered at the `full` detent's height and revealed with
//     `translateY`.** Animating height would reflow the list on every frame of a drag; a
//     transform does not, and it keeps the body's scroll position across detent changes.
import {useCallback, useEffect, useLayoutEffect, useRef, useState} from 'react'
import type {ReactNode} from 'react'
import {detentHeight, nearestDetent, nextDetent, nextDetentLabel, type Detent} from '../../pages/trips/lib/detent'
import './BottomSheet.css'

/** Movement past this many px is a drag, not a tap. */
const DRAG_SLOP_PX = 4

export interface BottomSheetProps {
  detent: Detent
  onDetentChange(d: Detent): void
  /** The always-visible block. Drags from it; measured to give `summary` its height. */
  header: ReactNode
  /** The scrollable plan below the header. */
  children?: ReactNode
  /**
   * The sheet's settled visible height, reported on every detent change. The map's
   * `fitBounds` padding is computed from it — from the SETTLED height only, never from the
   * live drag, or the map refits on every pointer move.
   */
  onSettledHeightChange?(px: number): void
  className?: string
}

/** Viewport height in px, tracking `dvh` so the mobile URL bar can't strand the sheet. */
function useViewportHeight(): number {
  const [h, setH] = useState(() => (typeof window === 'undefined' ? 0 : window.innerHeight))
  useEffect(() => {
    const onResize = () => setH(window.innerHeight)
    window.addEventListener('resize', onResize)
    // visualViewport moves when the mobile URL bar collapses WITHOUT firing `resize` on
    // some browsers; without this the sheet keeps the taller viewport's geometry.
    window.visualViewport?.addEventListener('resize', onResize)
    return () => {
      window.removeEventListener('resize', onResize)
      window.visualViewport?.removeEventListener('resize', onResize)
    }
  }, [])
  return h
}

export function BottomSheet({
  detent,
  onDetentChange,
  header,
  children,
  onSettledHeightChange,
  className,
}: BottomSheetProps) {
  const viewportH = useViewportHeight()
  const headRef = useRef<HTMLDivElement | null>(null)
  // The grab handle plus the header — what `summary` resolves to. Measured, not guessed:
  // it depends on the Thai text, the safe-area inset and whether a Stop is selected.
  const [summaryH, setSummaryH] = useState(150)
  // Live height while a drag is in flight; null the rest of the time.
  const [dragH, setDragH] = useState<number | null>(null)
  const dragRef = useRef<{pointerId: number; startY: number; startH: number} | null>(null)
  // A drag that started on the handle still ends in a `click`. Without this, letting go
  // after nudging the handle would ALSO cycle the detent — the drag's result, undone.
  const movedRef = useRef(false)

  useLayoutEffect(() => {
    const el = headRef.current
    if (!el) return
    const measure = () => setSummaryH(el.getBoundingClientRect().height)
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const fullH = detentHeight('full', viewportH, summaryH)
  const settledH = detentHeight(detent, viewportH, summaryH)
  const visibleH = dragH ?? settledH

  // Report the SETTLED height only. Reporting `visibleH` would hand the map a new padding
  // on every pointer move and re-fit the camera mid-drag.
  useEffect(() => {
    onSettledHeightChange?.(settledH)
  }, [settledH, onSettledHeightChange])

  const onPointerDown = useCallback((e: React.PointerEvent) => {
    // The grab handle drags as well as taps; every OTHER control in the header (นำทาง, ✕,
    // the day-start editor) keeps its own tap, so only bare chrome and the handle drag.
    const hit = e.target as HTMLElement
    if (!hit.closest('.plan-sheet-handle') && hit.closest('button, a, input, select, textarea')) return
    dragRef.current = {pointerId: e.pointerId, startY: e.clientY, startH: visibleH}
    movedRef.current = false
    // Pointer capture is taken in onPointerMove, NOT here: capturing on pointerdown
    // retargets the whole gesture — including the trailing `click` — onto this container,
    // so the handle's own onClick would never fire and the tap-to-cycle path would be dead.
  }, [visibleH])

  const onPointerMove = useCallback((e: React.PointerEvent) => {
    const d = dragRef.current
    if (!d || d.pointerId !== e.pointerId) return
    const dy = d.startY - e.clientY // dragging UP (smaller clientY) grows the sheet
    if (!movedRef.current) {
      if (Math.abs(dy) <= DRAG_SLOP_PX) return // still a tap, not a drag
      movedRef.current = true
      e.currentTarget.setPointerCapture(e.pointerId)
    }
    setDragH(Math.max(0, Math.min(fullH, d.startH + dy)))
  }, [fullH])

  const endDrag = useCallback((e: React.PointerEvent) => {
    const d = dragRef.current
    if (!d || d.pointerId !== e.pointerId) return
    dragRef.current = null
    const released = dragH
    setDragH(null)
    if (released == null || !movedRef.current) return
    const next = nearestDetent(released, viewportH, summaryH)
    if (next !== detent) onDetentChange(next)
  }, [dragH, viewportH, summaryH, detent, onDetentChange])

  return (
    <div
      className={`plan-sheet${dragH != null ? ' dragging' : ''}${className ? ' ' + className : ''}`}
      data-testid="plan-sheet"
      data-detent={detent}
      style={{height: fullH, transform: `translateY(${Math.max(0, fullH - visibleH)}px)`}}
    >
      {/* Handle + header are one measured block: `summary` resolves to exactly what is
          visible at rest, and both of them drag (spec §4.2). */}
      <div
        ref={headRef}
        className="plan-sheet-top"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
      >
        <button
          type="button"
          className="plan-sheet-handle"
          data-testid="plan-sheet-handle"
          // `aria-expanded` is the sheet's open/closed axis; the label names the state the
          // tap moves TO, which is also the keyboard/AT route through all three detents.
          aria-expanded={detent !== 'summary'}
          aria-label={nextDetentLabel(detent)}
          onClick={() => { if (!movedRef.current) onDetentChange(nextDetent(detent)) }}
        >
          <span className="plan-sheet-grab" aria-hidden="true" />
        </button>
        <div className="plan-sheet-head">{header}</div>
      </div>

      {/* Rendered at every detent so the body keeps its scroll position across a change;
          at `summary` it is simply below the fold. */}
      <div className="plan-sheet-body" data-testid="plan-sheet-body">{children}</div>
    </div>
  )
}
