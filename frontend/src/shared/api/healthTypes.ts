/**
 * Mirror of the backend Health DTOs (`backend/src/MenuNest.Application/UseCases/Health/*.cs`).
 *
 * Type-mapping conventions:
 *  - C# `DateOnly` → TS `string` (ISO date `YYYY-MM-DD`)
 *  - C# `DateTime` → TS `string` (ISO 8601 with offset / `Z`)
 *  - C# `Guid`     → TS `string`
 *
 * Enums use explicit numeric values matching the C# enum values so the
 * wire payload (which serializes enums by number under our default JSON
 * settings) round-trips cleanly.
 */

// ---------------------------------------------------------------------------
// Enums — written as `const` objects + value-derived type aliases because
// the app's tsconfig enables `erasableSyntaxOnly`, which forbids classic
// numeric `enum` declarations. The shape is identical at the call site:
// `DrugType.Triptan` is a numeric literal that matches the C# enum value.
// ---------------------------------------------------------------------------

export const DrugType = {
  Analgesic: 1,
  Nsaid: 2,
  Triptan: 3,
  Other: 9,
} as const
export type DrugType = (typeof DrugType)[keyof typeof DrugType]

export const SymptomLocation = {
  Left: 1,
  Right: 2,
  Bilateral: 3,
  Frontal: 4,
  Temporal: 5,
  Occipital: 6,
} as const
export type SymptomLocation = (typeof SymptomLocation)[keyof typeof SymptomLocation]

export const SymptomQuality = {
  Throbbing: 1,
  Pressure: 2,
  Stabbing: 3,
  Burning: 4,
} as const
export type SymptomQuality = (typeof SymptomQuality)[keyof typeof SymptomQuality]

export const AssociatedSymptom = {
  Nausea: 1,
  Vomiting: 2,
  Photophobia: 3,
  Phonophobia: 4,
  Osmophobia: 5,
} as const
export type AssociatedSymptom =
  (typeof AssociatedSymptom)[keyof typeof AssociatedSymptom]

export const AuraType = {
  Visual: 1,
  Sensory: 2,
  Speech: 3,
  Motor: 4,
} as const
export type AuraType = (typeof AuraType)[keyof typeof AuraType]

export const FunctionalImpact = {
  None: 1,
  Mild: 2,
  Moderate: 3,
  SevereBedrest: 4,
} as const
export type FunctionalImpact =
  (typeof FunctionalImpact)[keyof typeof FunctionalImpact]

export const NoDrugReason = {
  MaxDoseReached: 1,
  AllDrugsActive: 2,
  OutOfStock: 3,
  NoDrugTreatsThis: 4,
  UserSkip: 9,
} as const
export type NoDrugReason = (typeof NoDrugReason)[keyof typeof NoDrugReason]

export const PingStatus = {
  Pending: 1,
  Asked: 2,
  Answered: 3,
  Missed: 4,
} as const
export type PingStatus = (typeof PingStatus)[keyof typeof PingStatus]

export const PingResponse = {
  Resolved: 1,
  Improved: 2,
  Same: 3,
  Worse: 4,
  RetroResolved: 5,
  RetroUnknown: 6,
} as const
export type PingResponse = (typeof PingResponse)[keyof typeof PingResponse]

export const BlockedReason = {
  MaxDoseReached: 1,
  StillActive: 2,
  OutOfStock: 3,
} as const
export type BlockedReason = (typeof BlockedReason)[keyof typeof BlockedReason]

// ---------------------------------------------------------------------------
// Drugs
// ---------------------------------------------------------------------------

export interface PhotoRefDto {
  id: string
  url: string
  fileSize: number
  contentType: string
}

export interface AttachedPhotoInfo {
  blobUrl: string
  fileSize: number
  contentType: string
}

export interface DrugDto {
  id: string
  name: string
  activeIngredient: string | null
  drugType: DrugType
  doseStrength: string
  effectDurationMinHours: number
  effectDurationMaxHours: number
  maxDailyDose: number
  stockCount: number
  expirationDate: string | null
  treatsSymptomIds: string[]
  hasPhoto: boolean
  firstPhotoUrl: string | null
}

export interface DrugDetailDto {
  id: string
  name: string
  activeIngredient: string | null
  drugType: DrugType
  doseStrength: string
  effectDurationMinHours: number
  effectDurationMaxHours: number
  maxDailyDose: number
  stockCount: number
  expirationDate: string | null
  usageNote: string | null
  treatsSymptomIds: string[]
  photos: PhotoRefDto[]
  createdAt: string
  updatedAt: string | null
}

export interface CreateDrugRequest {
  name: string
  drugType: DrugType
  doseStrength: string
  effectDurationMinHours: number
  effectDurationMaxHours: number
  maxDailyDose: number
  stockCount?: number
  activeIngredient?: string | null
  expirationDate?: string | null
  usageNote?: string | null
  treatsSymptomIds?: string[] | null
}

export interface UpdateDrugRequest {
  name: string
  drugType: DrugType
  doseStrength: string
  effectDurationMinHours: number
  effectDurationMaxHours: number
  maxDailyDose: number
  stockCount: number
  activeIngredient: string | null
  expirationDate: string | null
  usageNote: string | null
  treatsSymptomIds: string[] | null
}

// ---------------------------------------------------------------------------
// Symptoms + Triggers
// ---------------------------------------------------------------------------

export interface SymptomDto {
  id: string
  name: string
  isSeed: boolean
}

export interface TriggerDto {
  id: string
  name: string
  isSeed: boolean
}

export interface CreateCustomSymptomRequest {
  name: string
}

export interface CreateCustomTriggerRequest {
  name: string
}

// ---------------------------------------------------------------------------
// Episodes
// ---------------------------------------------------------------------------

export interface EpisodeDto {
  id: string
  symptomId: string
  symptomName: string
  startedAt: string
  endedAt: string | null
  severity: number
  severityAfter: number | null
  isOnPeriod: boolean
  noDrugTaken: boolean
  noDrugReasonCode: NoDrugReason | null
  retroClosed: boolean
  intakeCount: number
  firstDrugName: string | null
}

export interface EpisodeIntakeDto {
  id: string
  drugId: string
  drugName: string
  doseStrength: string
  takenAt: string
  doseAmount: number
}

export interface EpisodeFollowUpDto {
  id: string
  scheduledAt: string
  askedAt: string | null
  respondedAt: string | null
  response: PingResponse | null
  severityAtCheck: number | null
  status: PingStatus
}

export interface EpisodeDetailDto {
  id: string
  symptomId: string
  symptomName: string
  startedAt: string
  endedAt: string | null
  severity: number
  severityAfter: number | null
  isOnPeriod: boolean
  noDrugTaken: boolean
  noDrugReasonCode: NoDrugReason | null
  notes: string | null
  retroClosed: boolean
  retroEstimatedDuration: string | null
  hasAura: boolean | null
  auraDurationMin: number | null
  auraTypes: AuraType[]
  location: SymptomLocation | null
  quality: SymptomQuality | null
  associatedSymptoms: AssociatedSymptom[]
  worsenedByActivity: boolean | null
  functionalImpact: FunctionalImpact | null
  triggerIds: string[]
  intakes: EpisodeIntakeDto[]
  followUps: EpisodeFollowUpDto[]
  photos: PhotoRefDto[]
  createdAt: string
  updatedAt: string | null
}

export interface StartEpisodeRequest {
  symptomId: string
  severity: number
  isOnPeriod?: boolean
  startedAt?: string | null
  triggerIds?: string[] | null
  notes?: string | null
  hasAura?: boolean | null
  auraTypes?: AuraType[] | null
  auraDurationMin?: number | null
  location?: SymptomLocation | null
  quality?: SymptomQuality | null
  associatedSymptoms?: AssociatedSymptom[] | null
  worsenedByActivity?: boolean | null
  functionalImpact?: FunctionalImpact | null
}

export interface UpdateEpisodeRequest {
  severity?: number | null
  notes?: string | null
  isOnPeriod?: boolean | null
  triggerIds?: string[] | null
  hasAura?: boolean | null
  auraTypes?: AuraType[] | null
  auraDurationMin?: number | null
  location?: SymptomLocation | null
  quality?: SymptomQuality | null
  associatedSymptoms?: AssociatedSymptom[] | null
  worsenedByActivity?: boolean | null
  functionalImpact?: FunctionalImpact | null
  migraineAttributesProvided?: boolean
}

export interface ResolveEpisodeRequest {
  severityAfter?: number
  endedAt?: string | null
}

export interface ListEpisodesQueryArgs {
  from?: string | null
  to?: string | null
  symptomId?: string | null
  onlyResolved?: boolean | null
  onlyFailed?: boolean | null
}

// ---------------------------------------------------------------------------
// Intakes + Take Medication context
// ---------------------------------------------------------------------------

export interface IntakeDto {
  id: string
  drugId: string
  drugName: string
  symptomEpisodeId: string | null
  takenAt: string
  doseAmount: number
}

export interface ActiveDrugDto {
  drugId: string
  drugName: string
  doseStrength: string
  lastTakenAt: string
  effectEndsAt: string
  remainingMinutes: number
  progressPct: number
}

export interface TakeableDrugDto {
  drugId: string
  drugName: string
  doseStrength: string
  drugType: DrugType
  stockCount: number
  effectDurationMinHours: number
  effectDurationMaxHours: number
}

export interface BlockedDrugDto {
  drugId: string
  drugName: string
  doseStrength: string
  reason: BlockedReason
  availableAt: string | null
}

export interface TakeMedicationContextDto {
  symptomEpisodeId: string | null
  symptomId: string | null
  symptomName: string | null
  currentSeverity: number | null
  activeDrugs: ActiveDrugDto[]
  takeableDrugs: TakeableDrugDto[]
  blockedDrugs: BlockedDrugDto[]
}

export interface LogIntakeRequest {
  drugId: string
  doseAmount: number
  symptomEpisodeId?: string | null
  takenAt?: string | null
  notes?: string | null
}

export interface LogNoDrugRequest {
  reason: NoDrugReason
}

// ---------------------------------------------------------------------------
// Follow-ups
// ---------------------------------------------------------------------------

export interface RecordPingResponseRequest {
  response: PingResponse
  severityAtCheck?: number | null
}

export interface RetroCloseEpisodeRequest {
  estimatedDuration: string | null
  outcome: PingResponse
}

// ---------------------------------------------------------------------------
// Photos (SAS upload)
// ---------------------------------------------------------------------------

export type PhotoContainerKey = 'drug' | 'episode'

export interface RequestUploadSasRequest {
  containerKey: PhotoContainerKey
  parentId: string
  contentType: string
}

export interface UploadSasResponse {
  uploadUrl: string
  blobUrl: string
  expiresAt: string
}

// ---------------------------------------------------------------------------
// Push Subscriptions
// ---------------------------------------------------------------------------

export interface SubscribeWebPushRequest {
  endpoint: string
  p256dh: string
  auth: string
  expiresAt: string | null
}

export interface SubscribeWebPushResultDto {
  id: string
}

export interface UnsubscribeWebPushRequest {
  endpoint: string
}

export interface VapidPublicKeyDto {
  publicKey: string
}

// ---------------------------------------------------------------------------
// Share links
// ---------------------------------------------------------------------------

export interface CreateShareLinkRequest {
  dateFrom: string
  dateTo: string
  validForDays?: number
}

export interface CreateShareLinkResultDto {
  token: string
  shareUrl: string
  shareId: string
  expiresAt: string
  dateFrom: string
  dateTo: string
}

export interface ShareLinkSummaryDto {
  id: string
  dateFrom: string
  dateTo: string
  createdAt: string
  expiresAt: string
  revokedAt: string | null
  accessCount: number
  lastAccessedAt: string | null
}

// ---------------------------------------------------------------------------
// Doctor Report (public)
// ---------------------------------------------------------------------------

export interface DoctorReportSummary {
  totalAttacks: number
  daysAffected: number
  acuteMedDays: number
  averageDurationHours: number
  averagePeakSeverity: number
  severeAttacksCount: number
  daysFullyDisabled: number
  attacksWithAura: number
  auraPercentage: number
}

export interface DoctorReportFlag {
  code: string
  severity: 'danger' | 'warning' | string
  title: string
  detail: string
}

export interface TriggerCorrelationDto {
  triggerId: string
  triggerName: string
  attackCount: number
  percentage: number
}

export interface TreatmentEfficacyDto {
  drugId: string
  drugName: string
  drugType: DrugType
  doseCount: number
  reliefCount: number
  reliefPercentage: number
  averageOnsetMinutes: number
}

/**
 * Days-of-week are keyed by the .NET `DayOfWeek` enum (0=Sunday … 6=Saturday)
 * but JSON serialization yields integer keys as strings. Use the helper
 * elsewhere to normalize.
 */
export interface DoctorReportPatterns {
  onsetTimeBuckets: Record<string, number>
  dayOfWeekCounts: Record<string, number>
  attacksDuringPeriod: number
  attacksOutsidePeriod: number
  attackRateDuringPeriod: number
  attackRateOutsidePeriod: number
}

export interface NoDrugEventDto {
  episodeId: string
  startedAt: string
  symptomName: string
  severity: number
  reason: NoDrugReason | null
}

export interface DoctorReportEpisodeIntake {
  takenAt: string
  drugName: string
  doseAmount: number
}

export interface DoctorReportEpisodeFollowUp {
  scheduledAt: string
  respondedAt: string | null
  response: PingResponse | null
  severityAtCheck: number | null
}

export interface DoctorReportEpisode {
  id: string
  symptomId: string
  symptomName: string
  startedAt: string
  endedAt: string | null
  severity: number
  severityAfter: number | null
  hasAura: boolean | null
  location: SymptomLocation | null
  quality: SymptomQuality | null
  associatedSymptoms: AssociatedSymptom[]
  functionalImpact: FunctionalImpact | null
  isOnPeriod: boolean
  noDrugTaken: boolean
  noDrugReasonCode: NoDrugReason | null
  intakes: DoctorReportEpisodeIntake[]
  followUps: DoctorReportEpisodeFollowUp[]
  triggerIds: string[]
}

export interface DoctorReportDay {
  date: string
  isPeriodDay: boolean
  attackCount: number
  peakSeverity: number
  doseCount: number
  noDrugEvents: number
  episodes: DoctorReportEpisode[]
}

export interface DoctorReportDto {
  patientName: string
  dateFrom: string
  dateTo: string
  durationDays: number
  generatedAtUtc: string
  summary: DoctorReportSummary
  clinicalFlags: DoctorReportFlag[]
  triggerCorrelations: TriggerCorrelationDto[]
  treatmentEfficacy: TreatmentEfficacyDto[]
  patterns: DoctorReportPatterns
  noDrugEvents: NoDrugEventDto[]
  days: DoctorReportDay[]
}
