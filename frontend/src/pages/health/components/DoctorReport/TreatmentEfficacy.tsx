import type { TreatmentEfficacyDto } from '../../../../shared/api/healthTypes'
import { DrugType } from '../../../../shared/api/healthTypes'

/**
 * Acute-treatment efficacy table. Columns: Drug | Doses | Relief % | Avg
 * onset. Relief % colorised:
 *   - ≥75%   green  (high efficacy)
 *   - 40-74% amber  (mid)
 *   - <40%   red    (low, consider switching)
 *
 * The footer aggregates failed-treatment counts across the table so the
 * doctor can see "X failures" without scanning every row.
 *
 * Mock: docs/mocks/doctor-report-mock.html — "💊 Acute treatment
 * efficacy" card.
 */
export interface TreatmentEfficacyProps {
  items: TreatmentEfficacyDto[]
}

function drugTypeLabel(t: DrugType): string {
  switch (t) {
    case DrugType.Analgesic:
      return 'simple analgesic'
    case DrugType.Nsaid:
      return 'NSAID'
    case DrugType.Triptan:
      return 'triptan'
    case DrugType.Other:
      return 'other'
    default:
      return 'other'
  }
}

function reliefTone(pct: number): 'high' | 'mid' | 'low' {
  if (pct >= 75) return 'high'
  if (pct >= 40) return 'mid'
  return 'low'
}

export function TreatmentEfficacy({ items }: TreatmentEfficacyProps) {
  // Sort by relief percentage desc so the most effective treatment is at
  // the top — the doctor's most likely first-line choice.
  const sorted = [...items].sort((a, b) => b.reliefPercentage - a.reliefPercentage)

  const totalFailed = sorted.reduce(
    (sum, i) => sum + Math.max(0, i.doseCount - i.reliefCount),
    0,
  )

  return (
    <div className="health-report-card">
      <h2 className="health-report-h2">💊 Acute treatment efficacy</h2>
      {sorted.length === 0 ? (
        <div className="health-report-empty">ไม่มี doses ในช่วงนี้</div>
      ) : (
        <table className="health-report-efficacy-table">
          <thead>
            <tr>
              <th>Drug</th>
              <th>Doses</th>
              <th>Relief %</th>
              <th>Avg onset</th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((row) => {
              const pct = Math.round(row.reliefPercentage)
              const onset =
                row.averageOnsetMinutes > 0
                  ? `${Math.round(row.averageOnsetMinutes)} min`
                  : '—'
              return (
                <tr key={row.drugId}>
                  <td>
                    <div className="health-report-efficacy-drug">{row.drugName}</div>
                    <div className="health-report-efficacy-drug-type">
                      {drugTypeLabel(row.drugType)}
                    </div>
                  </td>
                  <td>{row.doseCount}</td>
                  <td>
                    <span
                      className={`health-report-efficacy-pct health-report-efficacy-pct--${reliefTone(pct)}`}
                    >
                      {pct}%
                    </span>
                  </td>
                  <td>{onset}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
      {totalFailed > 0 && (
        <div className="health-report-card-footer">
          💡 <strong>Failed treatments</strong>: {totalFailed} doses ที่ไม่ได้ผล (relief
          rate &lt; 100%)
        </div>
      )}
    </div>
  )
}
