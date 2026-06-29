// frontend/src/pages/trips/components/SegmentedTabs.tsx
//
// Controlled segmented tab control built from Syncfusion Pure React Buttons
// (@syncfusion/react-buttons — tier-1 per frontend-guidelines §2, preferred
// over the ej2 legacy layer). `value` is the SINGLE source of truth: the render
// reflects it, and a click reports the new value up via onChange. There is no
// internal selection state, so the active segment can never drift out of sync
// with `value`.
//
// (Previously backed by the ej2 TabComponent, which owns its selection index
// internally. Under a re-render/animation race that index could move ahead of
// `value`; a click on the tab ej2 already thought was active was then silently
// dropped — the "tab click does nothing" bug. A stateless control removes that
// whole failure class.)
//
// Styling: `.seg-tabs` / `.seg-tab` / `.seg-tab.active` in trips-tokens.css.

import { Button, Variant } from '@syncfusion/react-buttons'

export function SegmentedTabs<T extends string>({
  value,
  options,
  onChange,
}: {
  value: T
  options: { label: string; value: T }[]
  onChange: (v: T) => void
}) {
  return (
    <div className="seg-tabs" role="tablist">
      {options.map((o) => {
        const active = o.value === value
        return (
          <Button
            key={o.value}
            type="button"
            role="tab"
            aria-selected={active}
            variant={Variant.Standard}
            className={`seg-tab${active ? ' active' : ''}`}
            onClick={() => onChange(o.value)}
          >
            {o.label}
          </Button>
        )
      })}
    </div>
  )
}
