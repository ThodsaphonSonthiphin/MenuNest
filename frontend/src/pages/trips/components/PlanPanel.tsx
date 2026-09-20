// frontend/src/pages/trips/components/PlanPanel.tsx
//
// The desktop **Plan panel** (menunest-236): the same contents the mobile **Plan sheet**
// carries, in a different container. It floats over the left edge of the full-bleed map and
// collapses out of the way via a rail button on its right edge — collapsed, the map is the
// whole canvas.
//
// There is no detent here: the panel is full height, so the list is always visible, which is
// why desktop needs no **Compact stop card** (spec §6.3).
import type {ReactNode} from 'react'
import {ChevronLeftIcon, ChevronRightIcon} from './TripFormIcons'

export function PlanPanel({
  collapsed,
  onToggle,
  header,
  children,
}: {
  collapsed: boolean
  onToggle(): void
  /** Trip header + Day chips + the day summary row — always visible, never scrolled away. */
  header: ReactNode
  children?: ReactNode
}) {
  return (
    <>
      <div className={`plan-panel${collapsed ? ' collapsed' : ''}`} data-testid="plan-panel">
        <div className="plan-panel-head">{header}</div>
        <div className="plan-panel-body">{children}</div>
      </div>
      {/* Outside the panel so it stays reachable once the panel is translated off-canvas. */}
      <button
        type="button"
        className={`plan-panel-rail${collapsed ? ' collapsed' : ''}`}
        data-testid="plan-panel-rail"
        aria-expanded={!collapsed}
        aria-label={collapsed ? 'เปิดแผงแผน' : 'พับแผงแผน'}
        onClick={onToggle}
      >
        {collapsed ? <ChevronRightIcon /> : <ChevronLeftIcon />}
      </button>
    </>
  )
}
