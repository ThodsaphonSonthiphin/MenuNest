import { useEffect, useMemo, useState } from 'react'
import {
  useListSymptomsQuery,
  useListTriggersQuery,
} from '../../../shared/api/api'
import type {
  AssociatedSymptom,
  AuraType,
  FunctionalImpact,
  StartEpisodeRequest,
  SymptomDto,
  SymptomLocation,
  SymptomQuality,
  TriggerDto,
} from '../../../shared/api/healthTypes'
import { useLastEpisodeAttributes } from './useLastEpisodeAttributes'

/**
 * Local form state + reference-data plumbing for QuickLogAttackPage.
 *
 * The hook intentionally keeps state plain (`useState`s) rather than
 * pulling in react-hook-form: the form is short, every field is
 * optional except severity, and chip-style controls don't benefit from
 * RHF's validation chain. Pre-fill is wired through
 * `useLastEpisodeAttributes` so adding richer hints later is a one-line
 * change.
 */
export interface QuickLogFormState {
  // reference data
  symptoms: SymptomDto[] | undefined
  triggers: TriggerDto[] | undefined
  isLoadingRefs: boolean

  // form values
  symptomId: string
  setSymptomId: (v: string) => void
  severity: number
  setSeverity: (v: number) => void
  location: SymptomLocation | null
  setLocation: (v: SymptomLocation | null) => void
  quality: SymptomQuality | null
  setQuality: (v: SymptomQuality | null) => void
  triggerIds: string[]
  setTriggerIds: (v: string[]) => void
  hasAura: boolean
  auraTypes: AuraType[]
  toggleAura: (t: AuraType | 'none') => void
  isOnPeriod: boolean
  setIsOnPeriod: (v: boolean) => void
  associatedSymptoms: AssociatedSymptom[]
  setAssociatedSymptoms: (v: AssociatedSymptom[]) => void
  functionalImpact: FunctionalImpact | null
  setFunctionalImpact: (v: FunctionalImpact | null) => void
  notes: string
  setNotes: (v: string) => void

  // derived
  buildRequest: () => StartEpisodeRequest | null
  isReady: boolean
}

export function useQuickLogForm(): QuickLogFormState {
  const symptomsQuery = useListSymptomsQuery()
  const triggersQuery = useListTriggersQuery()
  const symptoms = symptomsQuery.data
  const triggers = triggersQuery.data
  const isLoadingRefs =
    (symptomsQuery.isLoading && !symptoms) || (triggersQuery.isLoading && !triggers)

  // Default to "Migraine" if present, else first symptom. The backend
  // seeds a known set so this should almost always resolve.
  const defaultSymptomId = useMemo(() => {
    if (!symptoms || symptoms.length === 0) return ''
    const migraine = symptoms.find((s) => /migraine|ไมเกรน/i.test(s.name))
    return (migraine ?? symptoms[0]).id
  }, [symptoms])

  const [symptomId, setSymptomId] = useState<string>('')
  useEffect(() => {
    if (!symptomId && defaultSymptomId) setSymptomId(defaultSymptomId)
  }, [defaultSymptomId, symptomId])

  // Smart pre-fill. For Phase 1 this resolves to `null` but we keep the
  // wiring so the chip groups can read from it once richer data lands.
  const prefill = useLastEpisodeAttributes(symptomId || null)

  const [severity, setSeverity] = useState<number>(7)
  const [location, setLocation] = useState<SymptomLocation | null>(prefill?.location ?? null)
  const [quality, setQuality] = useState<SymptomQuality | null>(prefill?.quality ?? null)
  const [triggerIds, setTriggerIds] = useState<string[]>(prefill?.triggerIds ?? [])
  const [hasAura, setHasAura] = useState<boolean>(false)
  const [auraTypes, setAuraTypes] = useState<AuraType[]>([])
  const [isOnPeriod, setIsOnPeriod] = useState<boolean>(false)
  const [associatedSymptoms, setAssociatedSymptoms] = useState<AssociatedSymptom[]>(
    prefill?.associatedSymptoms ?? [],
  )
  const [functionalImpact, setFunctionalImpact] = useState<FunctionalImpact | null>(
    prefill?.functionalImpact ?? null,
  )
  const [notes, setNotes] = useState<string>('')

  const toggleAura = (t: AuraType | 'none') => {
    if (t === 'none') {
      setHasAura(false)
      setAuraTypes([])
      return
    }
    setHasAura(true)
    setAuraTypes((curr) => (curr.includes(t) ? curr.filter((x) => x !== t) : [...curr, t]))
  }

  const buildRequest = (): StartEpisodeRequest | null => {
    if (!symptomId) return null
    return {
      symptomId,
      severity,
      isOnPeriod,
      triggerIds: triggerIds.length ? triggerIds : null,
      notes: notes.trim() ? notes.trim() : null,
      hasAura: hasAura || auraTypes.length > 0,
      auraTypes: auraTypes.length ? auraTypes : null,
      location,
      quality,
      associatedSymptoms: associatedSymptoms.length ? associatedSymptoms : null,
      functionalImpact,
    }
  }

  return {
    symptoms,
    triggers,
    isLoadingRefs,
    symptomId,
    setSymptomId,
    severity,
    setSeverity,
    location,
    setLocation,
    quality,
    setQuality,
    triggerIds,
    setTriggerIds,
    hasAura,
    auraTypes,
    toggleAura,
    isOnPeriod,
    setIsOnPeriod,
    associatedSymptoms,
    setAssociatedSymptoms,
    functionalImpact,
    setFunctionalImpact,
    notes,
    setNotes,
    buildRequest,
    isReady: !!symptomId,
  }
}
