import type { DoctorReportDto } from '../../../../shared/api/healthTypes'

/**
 * Navy / slate gradient summary card at the bottom of the report. Four
 * bullet lines:
 *
 *   - Diagnosis fit (migraine without/with aura counts + ICHD-3 verdict)
 *   - Pattern (episodic vs chronic, menstrual exacerbation if present)
 *   - Treatment response (compact "Triptan 100% • NSAID 60%" line)
 *   - Risk flags (re-states the danger/warning headlines)
 *
 * Everything is derived from props the parent already has — no extra
 * API call. The text is intentionally terse so a clinician can read it
 * in one breath at the bottom of the page.
 *
 * Mock: docs/mocks/doctor-report-mock.html — "🩺 Clinical summary" navy card.
 */
export interface ClinicalSummaryProps {
  report: DoctorReportDto
}

export function ClinicalSummary({ report }: ClinicalSummaryProps) {
  const { summary, days, treatmentEfficacy, patterns, clinicalFlags } = report

  const withAura = summary.attacksWithAura
  const withoutAura = Math.max(0, summary.totalAttacks - withAura)

  const isChronic = summary.totalAttacks >= 15
  const hasMenstrualExacerbation =
    patterns.attackRateOutsidePeriod > 0 &&
    patterns.attackRateDuringPeriod / patterns.attackRateOutsidePeriod >= 2

  // Compose the treatment response line — top 3 by relief %.
  const treatmentLine = [...treatmentEfficacy]
    .sort((a, b) => b.reliefPercentage - a.reliefPercentage)
    .slice(0, 3)
    .map((t) => `${shortenDrugName(t.drugName)} ${Math.round(t.reliefPercentage)}%`)
    .join(' • ')

  const dangerFlags = clinicalFlags.filter((f) => f.severity === 'danger')
  const warningFlags = clinicalFlags.filter((f) => f.severity === 'warning')

  const auraStatement =
    summary.totalAttacks === 0
      ? 'ไม่มี attacks ในช่วงนี้'
      : `Migraine without aura (${withoutAura})${
          withAura > 0 ? ` + with aura (${withAura})` : ''
        } — ICHD-3 ${isCriteriaLikelyMet(report, days.length > 0) ? 'criteria met' : 'criteria pending'}`

  return (
    <div className="health-report-card health-report-card--summary">
      <h2 className="health-report-h2 health-report-h2--inverse">🩺 Clinical summary</h2>
      <div className="health-report-summary-body">
        <div>
          <strong>Diagnosis fit:</strong> {auraStatement}
        </div>
        <div>
          <strong>Pattern:</strong>{' '}
          {isChronic ? 'Chronic' : 'Episodic'}
          {hasMenstrualExacerbation
            ? ` with menstrual exacerbation (${(
                patterns.attackRateDuringPeriod / patterns.attackRateOutsidePeriod
              ).toFixed(1)}× rate during period)`
            : ''}
        </div>
        {treatmentLine && (
          <div>
            <strong>Treatment response:</strong> {treatmentLine}
          </div>
        )}
        <div>
          <strong>Risk flags:</strong>{' '}
          {dangerFlags.length === 0 && warningFlags.length === 0
            ? 'ไม่มี risk flag'
            : [...dangerFlags, ...warningFlags].map((f) => f.title).join(' • ')}
        </div>
      </div>
    </div>
  )
}

/** Trim doses off drug names: "Sumatriptan 50mg" → "Sumatriptan". */
function shortenDrugName(name: string): string {
  return name.replace(/\s+\d+\s*(mg|mcg|g|ml).*$/i, '').trim() || name
}

/**
 * Cheap ICHD-3 sanity check — fires when there is enough data to assert
 * the migraine criteria are plausible. We reuse the same heuristic as
 * AuraSymptomsSection but at a coarser level: at least one attack with
 * any pain quality + at least one associated symptom across the report.
 */
function isCriteriaLikelyMet(report: DoctorReportDto, hasDays: boolean): boolean {
  if (!hasDays || report.summary.totalAttacks === 0) return false
  let hasQuality = false
  let hasAssoc = false
  for (const d of report.days) {
    for (const ep of d.episodes) {
      if (ep.quality !== null) hasQuality = true
      if (ep.associatedSymptoms.length > 0) hasAssoc = true
    }
  }
  return hasQuality && hasAssoc
}
