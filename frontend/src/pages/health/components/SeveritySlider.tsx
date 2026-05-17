/**
 * Pain-severity slider used by Quick Log Attack + the "update severity"
 * modal on Active Episode.
 *
 * Visual brief from the mocks:
 *   docs/mocks/patient-quick-log-mock.html (Quick Log — large display)
 *   docs/mocks/patient-active-episode-mock.html (read-only readout)
 *
 * The big number is rendered above the slider so the user sees the
 * current value while they drag — important because the thumb itself
 * has no number on it (consistent with iOS-style range inputs).
 */
export interface SeveritySliderProps {
  value: number
  onChange: (next: number) => void
  min?: number
  max?: number
  /** Optional question/prompt text rendered above the number. */
  question?: string
  /** Hide the wrapping card (used inside an existing card / modal). */
  bare?: boolean
}

export function SeveritySlider({
  value,
  onChange,
  min = 1,
  max = 10,
  question,
  bare,
}: SeveritySliderProps) {
  const clamped = Math.min(Math.max(value, min), max)

  const body = (
    <>
      {question && <div className="health-severity-section__question">{question}</div>}
      <div className="health-severity-display">
        <span className="health-severity-num">{clamped}</span>
        <span className="health-severity-num--sub">/{max}</span>
      </div>
      <div className="health-slider-wrap">
        <input
          type="range"
          className="health-slider"
          min={min}
          max={max}
          value={clamped}
          onChange={(e) => onChange(Number(e.target.value))}
          aria-label="Severity"
        />
        <div className="health-slider-labels">
          <span>
            {min}
            <br />
            เบา
          </span>
          <span>
            {Math.round((min + max) / 2)}
            <br />
            กลาง
          </span>
          <span>
            {max}
            <br />
            รุนแรง
          </span>
        </div>
      </div>
    </>
  )

  if (bare) return <div>{body}</div>
  return <div className="health-severity-section">{body}</div>
}
