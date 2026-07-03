// frontend/src/pages/trips/components/DwellStepper.tsx
// Plain buttons (not Syncfusion) so the circular −/+ and pill presets match the
// redesign pixel-for-pixel without fighting the .sf-btn chrome. Styled by the
// .stop-editor-dialog .se-dwell rules in TripDetailPage.css.

const CHIPS = [30, 60, 90, 120]

export function DwellStepper({value, onChange}: {value: number; onChange: (v: number) => void}) {
  return (
    <div className="se-dwell">
      <div className="se-dwell-row">
        <button
          type="button"
          className="se-step se-step-minus"
          onClick={() => onChange(Math.max(15, value - 15))}
          aria-label="ลดเวลา 15 นาที"
        >
          −
        </button>
        <div className="se-dwell-center">
          <div className="se-dwell-num">{value}</div>
          <div className="se-dwell-unit">นาที</div>
        </div>
        <button
          type="button"
          className="se-step se-step-plus"
          onClick={() => onChange(value + 15)}
          aria-label="เพิ่มเวลา 15 นาที"
        >
          +
        </button>
      </div>
      <div className="se-dwell-chips">
        {CHIPS.map((c) => (
          <button
            type="button"
            key={c}
            className={`se-chip${c === value ? ' active' : ''}`}
            aria-pressed={c === value}
            onClick={() => onChange(c)}
          >
            {c}
          </button>
        ))}
      </div>
    </div>
  )
}
