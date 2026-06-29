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

// SelectEventArgs shape (from ej2-navigations/src/tab/tab.js):
//   selectedItem    – the tab header DOM element
//   selectedIndex   – number (0-based)
//   selectedContent – content pane DOM element
//   isSwiped        – boolean
//   isInteracted    – boolean  ← true only when the user clicked/keyboard-ed;
//                               false for programmatic select() calls
interface SelectEventArgs {
  selectedIndex: number
  isInteracted: boolean
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

  // Sync controlled value → ej2 Tab when the parent drives a change
  // (selectedItem prop is one-shot on mount; after that the component owns
  // its own selection state internally, so we drive updates via ref).
  useEffect(() => {
    if (tabRef.current && tabRef.current.selectedItem !== selectedIndex) {
      tabRef.current.select(selectedIndex)
    }
  }, [selectedIndex])

  function handleSelected(args: SelectEventArgs) {
    // Guard: only call onChange for genuine user interactions, not for
    // programmatic selections triggered by the effect above (which would
    // create an onChange→state→render→effect→select loop).
    if (!args.isInteracted) return
    const picked = options[args.selectedIndex]
    if (picked && picked.value !== value) {
      onChange(picked.value)
    }
  }

  return (
    <TabComponent
      ref={tabRef}
      selectedItem={selectedIndex >= 0 ? selectedIndex : 0}
      selected={handleSelected}
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
