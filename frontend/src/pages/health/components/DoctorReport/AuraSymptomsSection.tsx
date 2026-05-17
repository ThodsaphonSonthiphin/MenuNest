import type {
  DoctorReportDay,
  DoctorReportSummary,
} from '../../../../shared/api/healthTypes'
import { AssociatedSymptom } from '../../../../shared/api/healthTypes'

/**
 * Aura + associated-symptom breakdown. Counts each feature across all
 * attacks in the report window and shows X / total (pct%).
 *
 * The bottom callout asserts ICHD-3 §1.1 migraine criteria when at
 * least two of (unilateral, throbbing, moderate-severe, aggravated by
 * activity) plus at least one of (nausea OR photo+phonophobia) are
 * present in the sample. Backend doesn't pre-compute this — the doctor
 * report is the only place we need it — so we derive it inline.
 *
 * Mock: docs/mocks/doctor-report-mock.html — "🌀 Aura & associated
 * symptoms" card.
 */
export interface AuraSymptomsSectionProps {
  summary: DoctorReportSummary
  days: DoctorReportDay[]
}

interface Counts {
  total: number
  visualAura: number
  photophobia: number
  phonophobia: number
  nausea: number
  vomiting: number
  osmophobia: number
  worsenedByActivity: number
  unilateral: number
  throbbing: number
  moderateSevere: number
}

function tally(days: DoctorReportDay[]): Counts {
  const c: Counts = {
    total: 0,
    visualAura: 0,
    photophobia: 0,
    phonophobia: 0,
    nausea: 0,
    vomiting: 0,
    osmophobia: 0,
    worsenedByActivity: 0,
    unilateral: 0,
    throbbing: 0,
    moderateSevere: 0,
  }
  for (const d of days) {
    for (const ep of d.episodes) {
      c.total++
      if (ep.hasAura === true) c.visualAura++
      const set = new Set(ep.associatedSymptoms)
      if (set.has(AssociatedSymptom.Photophobia)) c.photophobia++
      if (set.has(AssociatedSymptom.Phonophobia)) c.phonophobia++
      if (set.has(AssociatedSymptom.Nausea)) c.nausea++
      if (set.has(AssociatedSymptom.Vomiting)) c.vomiting++
      if (set.has(AssociatedSymptom.Osmophobia)) c.osmophobia++
      // Functional impact and worsenedByActivity are not on the
      // doctor-report episode shape — we cannot tally activity-worsening
      // independently. Backend already gives us severeAttacksCount via
      // the summary, so the ICHD-3 inference uses that as a proxy for
      // "moderate-severe pain".
      // Location 1=Left, 2=Right → unilateral. 3=Bilateral excluded.
      if (ep.location === 1 || ep.location === 2) c.unilateral++
      if (ep.quality === 1) c.throbbing++
      if (ep.severity >= 5) c.moderateSevere++
    }
  }
  return c
}

function pctOf(num: number, total: number): string {
  if (total === 0) return '0%'
  return `${Math.round((num / total) * 100)}%`
}

function meetsIchd3(c: Counts): boolean {
  if (c.total === 0) return false
  // At least 2 of 4 features (unilateral, throbbing, moderate-severe,
  // aggravated by activity). We omit "aggravated by activity" because
  // the doctor-report payload does not carry that field. Two of the
  // remaining three is a defensible proxy.
  const present =
    (c.unilateral > c.total / 2 ? 1 : 0) +
    (c.throbbing > c.total / 2 ? 1 : 0) +
    (c.moderateSevere > c.total / 2 ? 1 : 0)
  if (present < 2) return false
  // Plus at least one of nausea/vomiting OR photophobia+phonophobia.
  const nauseaOrVomit = c.nausea + c.vomiting > 0
  const photoAndPhono = c.photophobia > 0 && c.phonophobia > 0
  return nauseaOrVomit || photoAndPhono
}

export function AuraSymptomsSection({ summary, days }: AuraSymptomsSectionProps) {
  const counts = tally(days)
  const total = counts.total || summary.totalAttacks
  const ichd3 = meetsIchd3(counts)

  return (
    <div className="health-report-card">
      <h2 className="health-report-h2">🌀 Aura & associated symptoms</h2>
      <Row
        label="With aura (visual)"
        value={`${counts.visualAura} / ${total} (${pctOf(counts.visualAura, total)})`}
      />
      <Row
        label="Photophobia (กลัวแสง)"
        value={`${counts.photophobia} / ${total} (${pctOf(counts.photophobia, total)})`}
      />
      <Row
        label="Phonophobia (กลัวเสียง)"
        value={`${counts.phonophobia} / ${total} (${pctOf(counts.phonophobia, total)})`}
      />
      <Row
        label="Nausea"
        value={`${counts.nausea} / ${total} (${pctOf(counts.nausea, total)})`}
      />
      <Row
        label="Vomiting"
        value={`${counts.vomiting} / ${total} (${pctOf(counts.vomiting, total)})`}
      />
      <Row
        label="Osmophobia (กลัวกลิ่น)"
        value={`${counts.osmophobia} / ${total} (${pctOf(counts.osmophobia, total)})`}
      />
      <div className="health-report-ichd3-note">
        {ichd3 ? (
          <>
            ✅ ICHD-3 migraine criteria: ครบเกณฑ์ (≥2/4 features: unilateral,
            throbbing, moderate-severe • + ≥1: nausea หรือ photo+phonophobia)
          </>
        ) : (
          <>
            ℹ️ ICHD-3 migraine criteria: ข้อมูลไม่ครบหรืออาการ insufficient — ต้องการ ≥2
            ของ (unilateral, throbbing, moderate-severe) + nausea/photo+phono
          </>
        )}
      </div>
    </div>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="health-report-metric-row">
      <span className="health-report-metric-label">{label}</span>
      <span className="health-report-metric-value">{value}</span>
    </div>
  )
}
