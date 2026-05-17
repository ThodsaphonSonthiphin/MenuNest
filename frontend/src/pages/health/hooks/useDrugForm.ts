import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  useCreateDrugMutation,
  useGetDrugQuery,
  useUpdateDrugMutation,
} from '../../../shared/api/api'
import {
  DrugType,
  type CreateDrugRequest,
  type DrugDetailDto,
  type UpdateDrugRequest,
} from '../../../shared/api/healthTypes'

/**
 * Drives the Drug create/edit form (`DrugFormPage`).
 *
 *  - `mode` is derived from `id` so the page doesn't need to pass it
 *    explicitly. In edit mode the hook fetches the existing drug and
 *    seeds the state once the data arrives.
 *  - The hook stores the dose-strength as a single text field
 *    (`"500 mg"`) to match the DTO. Keeping this string-free-form
 *    matches the seed data in the mock.
 *  - `submit()` resolves to the saved `DrugDetailDto` so the caller can
 *    attach photos afterwards (the photo upload flow needs a real
 *    drug id; see `DrugFormPage.tsx`).
 */
export type DrugFormMode = 'create' | 'edit'

export interface DrugFormState {
  name: string
  doseStrength: string
  drugType: DrugType
  effectMin: number
  effectMax: number
  maxDaily: number
  stockCount: number
  expirationDate: string | null
  treatsSymptomIds: string[]
  usageNote: string
  activeIngredient: string
}

export interface DrugFormErrors {
  name?: string
  drugType?: string
  doseStrength?: string
  effectMin?: string
  effectMax?: string
  maxDaily?: string
  stockCount?: string
}

const EMPTY: DrugFormState = {
  name: '',
  doseStrength: '',
  drugType: DrugType.Analgesic,
  effectMin: 4,
  effectMax: 6,
  maxDaily: 4,
  stockCount: 0,
  expirationDate: null,
  treatsSymptomIds: [],
  usageNote: '',
  activeIngredient: '',
}

function seedFromDetail(d: DrugDetailDto): DrugFormState {
  return {
    name: d.name,
    doseStrength: d.doseStrength,
    drugType: d.drugType,
    effectMin: d.effectDurationMinHours,
    effectMax: d.effectDurationMaxHours,
    maxDaily: d.maxDailyDose,
    stockCount: d.stockCount,
    expirationDate: d.expirationDate,
    treatsSymptomIds: [...d.treatsSymptomIds],
    usageNote: d.usageNote ?? '',
    activeIngredient: d.activeIngredient ?? '',
  }
}

export interface UseDrugFormResult {
  mode: DrugFormMode
  form: DrugFormState
  setField: <K extends keyof DrugFormState>(key: K, value: DrugFormState[K]) => void
  errors: DrugFormErrors
  isLoading: boolean
  isLoadingDetail: boolean
  isReady: boolean
  detail: DrugDetailDto | undefined
  submit: () => Promise<DrugDetailDto>
}

function validate(form: DrugFormState): DrugFormErrors {
  const errs: DrugFormErrors = {}
  if (!form.name.trim()) errs.name = 'ใส่ชื่อยา'
  if (!form.doseStrength.trim()) errs.doseStrength = 'ใส่ dose เช่น 500 mg'
  if (form.effectMin <= 0) errs.effectMin = 'ต้อง > 0'
  if (form.effectMax <= 0 || form.effectMax < form.effectMin)
    errs.effectMax = 'ต้อง ≥ min'
  if (form.maxDaily <= 0) errs.maxDaily = 'ต้อง > 0'
  if (form.stockCount < 0) errs.stockCount = 'ต้อง ≥ 0'
  return errs
}

export function useDrugForm(id?: string): UseDrugFormResult {
  const mode: DrugFormMode = id ? 'edit' : 'create'
  // `skip` keeps RTK Query from issuing a fetch in create mode.
  const detailQuery = useGetDrugQuery(id ?? '', { skip: !id })
  const [createDrug, createState] = useCreateDrugMutation()
  const [updateDrug, updateState] = useUpdateDrugMutation()

  const [form, setForm] = useState<DrugFormState>(EMPTY)
  const [seeded, setSeeded] = useState(false)

  // One-shot seed once the detail arrives.
  useEffect(() => {
    if (mode === 'edit' && detailQuery.data && !seeded) {
      setForm(seedFromDetail(detailQuery.data))
      setSeeded(true)
    }
  }, [mode, detailQuery.data, seeded])

  const setField = useCallback(
    <K extends keyof DrugFormState>(key: K, value: DrugFormState[K]) => {
      setForm((prev) => ({ ...prev, [key]: value }))
    },
    [],
  )

  const errors = useMemo(() => validate(form), [form])
  const isReady = Object.keys(errors).length === 0

  const submit = useCallback(async (): Promise<DrugDetailDto> => {
    if (!isReady) {
      throw new Error('Form is not ready')
    }
    if (mode === 'edit' && id) {
      const req: UpdateDrugRequest = {
        name: form.name.trim(),
        drugType: form.drugType,
        doseStrength: form.doseStrength.trim(),
        effectDurationMinHours: form.effectMin,
        effectDurationMaxHours: form.effectMax,
        maxDailyDose: form.maxDaily,
        stockCount: form.stockCount,
        activeIngredient: form.activeIngredient.trim() || null,
        expirationDate: form.expirationDate,
        usageNote: form.usageNote.trim() || null,
        treatsSymptomIds: form.treatsSymptomIds.length ? form.treatsSymptomIds : null,
      }
      return await updateDrug({ id, ...req }).unwrap()
    }
    const req: CreateDrugRequest = {
      name: form.name.trim(),
      drugType: form.drugType,
      doseStrength: form.doseStrength.trim(),
      effectDurationMinHours: form.effectMin,
      effectDurationMaxHours: form.effectMax,
      maxDailyDose: form.maxDaily,
      stockCount: form.stockCount,
      activeIngredient: form.activeIngredient.trim() || null,
      expirationDate: form.expirationDate,
      usageNote: form.usageNote.trim() || null,
      treatsSymptomIds: form.treatsSymptomIds.length ? form.treatsSymptomIds : null,
    }
    return await createDrug(req).unwrap()
  }, [mode, id, form, isReady, createDrug, updateDrug])

  return {
    mode,
    form,
    setField,
    errors,
    isLoading: createState.isLoading || updateState.isLoading,
    isLoadingDetail: detailQuery.isLoading,
    isReady,
    detail: detailQuery.data,
    submit,
  }
}
