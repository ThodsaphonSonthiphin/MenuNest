// frontend/src/pages/trips/components/SegmentedTabs.tsx
//
// Decision: Implemented as a custom segmented control (NOT Syncfusion Tab).
// Rationale: @syncfusion/react-navigations@33.1.44 exports only Toolbar and
// ContextMenu — no Tab component (verified in node_modules/@syncfusion/
// react-navigations/src/index.d.ts which re-exports only ./toolbar and
// ./context-menu; the Tab component lives in the non-React @syncfusion/
// ej2-navigations package). Per the brief's fallback rule: "implement
// SegmentedTabs as a small custom segmented control … if the Pure-React Tab
// cannot cleanly do 'controlled header-only with parent-rendered content'."

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
      {options.map((o) => (
        <button
          key={o.value}
          role="tab"
          aria-selected={o.value === value}
          className={`seg-tab${o.value === value ? ' active' : ''}`}
          onClick={() => onChange(o.value)}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}
