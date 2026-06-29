// frontend/src/pages/trips/components/SegmentedTabs.tsx
//
// Uses @syncfusion/ej2-react-navigations TabComponent (ej2 legacy wrapper).
// TODO: migrate to Pure React Tab when @syncfusion/react-navigations ships one
//
// Why ej2 and not @syncfusion/react-navigations:
//   @syncfusion/react-navigations@33.x exports only Toolbar and ContextMenu
//   (src/index.d.ts re-exports only ./toolbar and ./context-menu). Tab lives
//   exclusively in the ej2 layer. Per project guideline §2 we fall back to
//   the ej2 React wrapper rather than hand-rolling the control.

import { useEffect, useRef } from 'react'
import {
  TabComponent,
  TabItemDirective,
  TabItemsDirective,
} from '@syncfusion/ej2-react-navigations'
// TODO: migrate to Pure React Tab when @syncfusion/react-navigations ships one

// SelectingEventArgs shape (from ej2-navigations/src/tab/tab.d.ts). The
// `selecting` event fires BEFORE the item is selected and is cancellable.
//   selectingIndex – number (0-based) the user/programme is moving TO
//   isInteracted   – true only when the user clicked/keyboard-ed; false (or
//                    undefined) for programmatic select() calls
//   cancel         – set true to stop ej2 from performing the selection
interface SelectingEventArgs {
  selectingIndex: number
  isInteracted?: boolean
  cancel?: boolean
}

export function SegmentedTabs<T extends string>({
  value,
  options,
  onChange,
}: {
  value: T
  options: { label: string; value: T }[]
  onChange: (v: T) => void
}) {
  const tabRef = useRef<TabComponent>(null)

  const selectedIndex = options.findIndex((o) => o.value === value)

  // `value` (from the parent / Redux) is the SINGLE source of truth for which
  // tab is active. ej2 Tab is uncontrolled internally — left to itself it owns
  // its own selection index, which can drift ahead of `value` under a
  // re-render/animation race. Once ej2's index drifts past `value`, a click on
  // the tab ej2 already thinks is active is silently dropped (clickHandler only
  // acts when `trgIndex !== this.selectedItem`) — the "tab click does nothing"
  // bug. We close that gap by making ej2 fully controlled: CANCEL its own
  // selection on user interaction and re-derive the selection from `value`.
  // (Same "cancel the built-in, drive it yourself" pattern the project uses for
  // the Scheduler — see frontend-guidelines §2.)
  useEffect(() => {
    if (
      tabRef.current &&
      selectedIndex >= 0 &&
      tabRef.current.selectedItem !== selectedIndex
    ) {
      tabRef.current.select(selectedIndex)
    }
  }, [selectedIndex])

  function handleSelecting(args: SelectingEventArgs) {
    // Programmatic select() (from the effect above) — let ej2 perform it so the
    // header tracks `value`. isInteracted is false/undefined in that path.
    if (!args.isInteracted) return
    // Genuine user interaction: block ej2 from owning the change, then push the
    // intent up. `value` flows back down and the effect re-selects ej2 — so
    // ej2's internal index can never get ahead of `value`.
    args.cancel = true
    const picked = options[args.selectingIndex]
    if (picked) onChange(picked.value)
  }

  return (
    <TabComponent
      ref={tabRef}
      selectedItem={selectedIndex >= 0 ? selectedIndex : 0}
      selecting={handleSelecting}
    >
      <TabItemsDirective>
        {options.map((o) => (
          <TabItemDirective
            key={o.value}
            header={{ text: o.label }}
            // No content — parent renders content based on `value` prop.
            content={''}
          />
        ))}
      </TabItemsDirective>
    </TabComponent>
  )
}
