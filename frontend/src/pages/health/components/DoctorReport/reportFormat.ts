import type {
  AssociatedSymptom,
  DoctorReportEpisode,
  FunctionalImpact,
  NoDrugReason,
  SymptomLocation,
  SymptomQuality,
} from '../../../../shared/api/healthTypes'
import {
  AssociatedSymptom as AssociatedSymptomEnum,
  FunctionalImpact as FunctionalImpactEnum,
  NoDrugReason as NoDrugReasonEnum,
  SymptomLocation as SymptomLocationEnum,
  SymptomQuality as SymptomQualityEnum,
} from '../../../../shared/api/healthTypes'

/**
 * Shared formatting helpers for the doctor report sub-components. The
 * report is presented to a clinician (often in Thai) so most labels are
 * Thai; ICHD-3 / drug names stay in English so they round-trip back to
 * pharmacy / EHR systems.
 *
 * Kept in a single module to avoid duplicating tiny i18n maps across
 * twelve sub-components.
 */

const THAI_MONTHS_SHORT = [
  'ม.ค.',
  'ก.พ.',
  'มี.ค.',
  'เม.ย.',
  'พ.ค.',
  'มิ.ย.',
  'ก.ค.',
  'ส.ค.',
  'ก.ย.',
  'ต.ค.',
  'พ.ย.',
  'ธ.ค.',
]

const THAI_DAYS_FULL = ['อาทิตย์', 'จันทร์', 'อังคาร', 'พุธ', 'พฤหัสบดี', 'ศุกร์', 'เสาร์']
const THAI_DAYS_SHORT = ['อา', 'จ', 'อ', 'พ', 'พฤ', 'ศ', 'ส']

/** Convert ISO date (`YYYY-MM-DD`) into "17 พ.ค." (Buddhist-era year omitted in compact form). */
export function formatDateShort(isoDate: string): string {
  const [yyyy, mm, dd] = isoDate.split('-').map((s) => parseInt(s, 10))
  if (Number.isNaN(yyyy) || Number.isNaN(mm) || Number.isNaN(dd)) return isoDate
  return `${dd} ${THAI_MONTHS_SHORT[mm - 1]}`
}

/** "17 เม.ย. — 17 พ.ค. 2569 (30 วัน)" range label. */
export function formatDateRange(fromIso: string, toIso: string, durationDays: number): string {
  const [, , dd1] = fromIso.split('-').map((s) => parseInt(s, 10))
  const m1 = parseInt(fromIso.split('-')[1], 10)
  const [yyyy2, mm2, dd2] = toIso.split('-').map((s) => parseInt(s, 10))
  // Thai Buddhist Era — add 543 to Gregorian year.
  const beYear = yyyy2 + 543
  return `${dd1} ${THAI_MONTHS_SHORT[m1 - 1]} — ${dd2} ${THAI_MONTHS_SHORT[mm2 - 1]} ${beYear} (${durationDays} วัน)`
}

/** Full date for daily timeline headers: "📅 17 พ.ค. พุธ". */
export function formatDayHeader(isoDate: string): string {
  const [yyyy, mm, dd] = isoDate.split('-').map((s) => parseInt(s, 10))
  if (Number.isNaN(yyyy) || Number.isNaN(mm) || Number.isNaN(dd)) return isoDate
  // Date(year, monthIdx, day) — local time. UTC vs local does not matter
  // here because we only read the day-of-week, not the wall-clock.
  const dow = new Date(yyyy, mm - 1, dd).getDay()
  return `${dd} ${THAI_MONTHS_SHORT[mm - 1]} ${THAI_DAYS_FULL[dow]}`
}

/** "HH:mm" — clock time only, in the viewer's local zone. */
export function formatClockTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  return d.toLocaleTimeString('th-TH', { hour: '2-digit', minute: '2-digit', hour12: false })
}

/** Full UTC → local for the "Generated 17 May 2026 14:30" line. */
export function formatGeneratedAt(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleString('en-GB', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

/** Compact duration: "4h 30m" or "50m". */
export function formatDuration(startIso: string, endIso: string | null): string {
  if (!endIso) return '—'
  const start = new Date(startIso).getTime()
  const end = new Date(endIso).getTime()
  const mins = Math.max(0, Math.round((end - start) / 60_000))
  const h = Math.floor(mins / 60)
  const m = mins % 60
  if (h === 0) return `${m}m`
  return m === 0 ? `${h}h` : `${h}h ${m}m`
}

export function symptomLocationLabel(loc: SymptomLocation | null): string {
  switch (loc) {
    case SymptomLocationEnum.Left:
      return 'left-sided'
    case SymptomLocationEnum.Right:
      return 'right-sided'
    case SymptomLocationEnum.Bilateral:
      return 'bilateral'
    case SymptomLocationEnum.Frontal:
      return 'frontal'
    case SymptomLocationEnum.Temporal:
      return 'temporal'
    case SymptomLocationEnum.Occipital:
      return 'occipital'
    default:
      return ''
  }
}

export function symptomQualityLabel(q: SymptomQuality | null): string {
  switch (q) {
    case SymptomQualityEnum.Throbbing:
      return 'throbbing'
    case SymptomQualityEnum.Pressure:
      return 'pressure'
    case SymptomQualityEnum.Stabbing:
      return 'stabbing'
    case SymptomQualityEnum.Burning:
      return 'burning'
    default:
      return ''
  }
}

export function associatedSymptomLabel(s: AssociatedSymptom): string {
  switch (s) {
    case AssociatedSymptomEnum.Nausea:
      return 'nausea'
    case AssociatedSymptomEnum.Vomiting:
      return 'vomiting'
    case AssociatedSymptomEnum.Photophobia:
      return 'photophobia'
    case AssociatedSymptomEnum.Phonophobia:
      return 'phonophobia'
    case AssociatedSymptomEnum.Osmophobia:
      return 'osmophobia'
    default:
      return ''
  }
}

export function associatedSymptomIcon(s: AssociatedSymptom): string {
  switch (s) {
    case AssociatedSymptomEnum.Nausea:
      return '🤢'
    case AssociatedSymptomEnum.Vomiting:
      return '🤮'
    case AssociatedSymptomEnum.Photophobia:
      return '💡'
    case AssociatedSymptomEnum.Phonophobia:
      return '🔊'
    case AssociatedSymptomEnum.Osmophobia:
      return '👃'
    default:
      return ''
  }
}

export function functionalImpactLabel(f: FunctionalImpact | null): string {
  switch (f) {
    case FunctionalImpactEnum.None:
      return ''
    case FunctionalImpactEnum.Mild:
      return 'mild'
    case FunctionalImpactEnum.Moderate:
      return 'moderate'
    case FunctionalImpactEnum.SevereBedrest:
      return 'bedrest'
    default:
      return ''
  }
}

export function noDrugReasonLabel(r: NoDrugReason | null): string {
  switch (r) {
    case NoDrugReasonEnum.MaxDoseReached:
      return 'เกิน max daily dose'
    case NoDrugReasonEnum.AllDrugsActive:
      return 'ยาตัวอื่นยังออกฤทธิ์'
    case NoDrugReasonEnum.OutOfStock:
      return 'ยาหมด'
    case NoDrugReasonEnum.NoDrugTreatsThis:
      return 'ไม่มียารักษาอาการนี้'
    case NoDrugReasonEnum.UserSkip:
      return 'ผู้ป่วยเลือกไม่กิน'
    default:
      return ''
  }
}

/** Outcome bucket used by EpisodeReportCard chrome color. */
export type EpisodeOutcome = 'resolved' | 'no-drug-warning' | 'ongoing'

export function episodeOutcome(ep: DoctorReportEpisode): EpisodeOutcome {
  if (ep.noDrugTaken) return 'no-drug-warning'
  if (!ep.endedAt) return 'ongoing'
  return 'resolved'
}

/** Day-of-week thai short label, index 0=Sunday … 6=Saturday (.NET DayOfWeek order). */
export function dayOfWeekLabel(dotnetDow: number): string {
  return THAI_DAYS_SHORT[dotnetDow] ?? ''
}

/**
 * Re-key the .NET DayOfWeek-indexed dictionary into Monday-first array
 * because clinicians read Mon→Sun in Thai context.
 */
export function dayOfWeekToMondayFirst(counts: Record<string, number>): Array<{
  label: string
  count: number
}> {
  // .NET DayOfWeek: 0=Sun, 1=Mon, … 6=Sat. We want Mon-first.
  const ORDER = [1, 2, 3, 4, 5, 6, 0]
  return ORDER.map((dow) => ({
    label: THAI_DAYS_SHORT[dow] ?? '',
    count: counts[dow.toString()] ?? 0,
  }))
}

/** Onset bucket label + sort key. Keys come from backend as 'morning'|'afternoon'|'evening'|'night'. */
export function onsetBucketLabel(key: string): string {
  switch (key.toLowerCase()) {
    case 'morning':
      return 'เช้า 06-12'
    case 'afternoon':
      return 'บ่าย 12-18'
    case 'evening':
      return 'เย็น 18-24'
    case 'night':
      return 'กลางคืน 0-6'
    default:
      return key
  }
}

export const ONSET_BUCKET_ORDER = ['morning', 'afternoon', 'evening', 'night'] as const

/** "12 / 30 วัน" plus optional warning glyph. */
export function ratio(num: number, denom: number): string {
  return `${num} / ${denom}`
}

export function percent(value: number): string {
  return `${Math.round(value)}%`
}
