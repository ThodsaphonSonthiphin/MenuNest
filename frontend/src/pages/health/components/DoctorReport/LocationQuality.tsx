import type { DoctorReportDay } from '../../../../shared/api/healthTypes'
import { SymptomLocation, SymptomQuality } from '../../../../shared/api/healthTypes'

/**
 * Two-column donut: pain location distribution + pain quality
 * distribution. Both donuts share an inline-SVG renderer with three
 * configurable slices (mock uses three for location, two for quality).
 *
 * Mock: docs/mocks/doctor-report-mock.html — "📍 Location & quality" card.
 */
export interface LocationQualityProps {
  days: DoctorReportDay[]
}

interface Tally {
  total: number
  left: number
  right: number
  bilateral: number
  throbbing: number
  pressure: number
  stabbing: number
  burning: number
}

function tally(days: DoctorReportDay[]): Tally {
  const t: Tally = {
    total: 0,
    left: 0,
    right: 0,
    bilateral: 0,
    throbbing: 0,
    pressure: 0,
    stabbing: 0,
    burning: 0,
  }
  for (const d of days) {
    for (const ep of d.episodes) {
      t.total++
      switch (ep.location) {
        case SymptomLocation.Left:
          t.left++
          break
        case SymptomLocation.Right:
          t.right++
          break
        case SymptomLocation.Bilateral:
          t.bilateral++
          break
        // Frontal/Temporal/Occipital not surfaced in the mock — they map
        // to "other" implicitly. Doctors who care about lobar
        // distribution can look at individual episodes.
      }
      switch (ep.quality) {
        case SymptomQuality.Throbbing:
          t.throbbing++
          break
        case SymptomQuality.Pressure:
          t.pressure++
          break
        case SymptomQuality.Stabbing:
          t.stabbing++
          break
        case SymptomQuality.Burning:
          t.burning++
          break
      }
    }
  }
  return t
}

export function LocationQuality({ days }: LocationQualityProps) {
  const t = tally(days)

  const locationSlices = [
    { key: 'left', label: 'Left', count: t.left, color: '#4f46e5' },
    { key: 'right', label: 'Right', count: t.right, color: '#06b6d4' },
    { key: 'bilateral', label: 'Bilateral', count: t.bilateral, color: '#64748b' },
  ]
  const qualitySlices = [
    { key: 'throbbing', label: 'Throbbing', count: t.throbbing, color: '#f59e0b' },
    { key: 'pressure', label: 'Pressure', count: t.pressure, color: '#94a3b8' },
    { key: 'stabbing', label: 'Stabbing', count: t.stabbing, color: '#ef4444' },
    { key: 'burning', label: 'Burning', count: t.burning, color: '#f97316' },
  ].filter((s) => s.count > 0 || s.key === 'throbbing' || s.key === 'pressure')

  return (
    <div className="health-report-card">
      <h2 className="health-report-h2">📍 Location & quality</h2>
      <div className="health-report-two-col">
        <Donut title="Location" slices={locationSlices} />
        <Donut title="Quality" slices={qualitySlices} />
      </div>
    </div>
  )
}

interface Slice {
  key: string
  label: string
  count: number
  color: string
}

function Donut({ title, slices }: { title: string; slices: Slice[] }) {
  const total = slices.reduce((sum, s) => sum + s.count, 0)
  // Circumference of an r=24 circle ≈ 150.8.
  const C = 2 * Math.PI * 24
  let offset = 0

  return (
    <div>
      <div className="health-report-two-col-title">{title}</div>
      <div className="health-report-distribution">
        <svg viewBox="0 0 60 60" width={60} height={60}>
          {total === 0 ? (
            <circle cx={30} cy={30} r={24} fill="none" stroke="#94a3b8" strokeWidth={12} />
          ) : (
            slices
              .filter((s) => s.count > 0)
              .map((s) => {
                const len = (s.count / total) * C
                const elem = (
                  <circle
                    key={s.key}
                    cx={30}
                    cy={30}
                    r={24}
                    fill="none"
                    stroke={s.color}
                    strokeWidth={12}
                    strokeDasharray={`${len} ${C - len}`}
                    strokeDashoffset={-offset}
                    transform="rotate(-90 30 30)"
                  />
                )
                offset += len
                return elem
              })
          )}
        </svg>
        <div className="health-report-distribution-legend">
          {slices.map((s) => (
            <div key={s.key} className="health-report-legend-item">
              <span className="health-report-legend-dot" style={{ background: s.color }} />
              <span className="health-report-legend-label">{s.label}</span>
              <span className="health-report-legend-value">
                {s.count}
                {total > 0 ? ` (${Math.round((s.count / total) * 100)}%)` : ''}
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
