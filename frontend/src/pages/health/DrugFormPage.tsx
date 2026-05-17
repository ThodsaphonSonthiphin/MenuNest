import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ChipGroup, ChipGroupMulti } from './components/ChipGroup'
import type { ChipOption } from './components/ChipGroup'
import { PhotoUploader } from './components/PhotoUploader'
import { SearchMultiSelect } from './components/SearchMultiSelect'
import {
  extractCustomText,
  isCustomValue,
} from './components/customValueSentinel'
import { useDrugForm } from './hooks/useDrugForm'
import {
  useAttachDrugPhotosMutation,
  useCreateCustomSymptomMutation,
  useListSymptomsQuery,
} from '../../shared/api/api'
import { DrugType } from '../../shared/api/healthTypes'
import './styles/health.css'

/**
 * Drug add/edit form.
 *
 *  - URL: `/health/drugs/new` (create) or `/health/drugs/:id/edit`.
 *  - State + submit live in `useDrugForm`. The local file only handles
 *    rendering chips + range inputs + the sticky save bar.
 *  - Photos: in create mode we hide the uploader and show a hint —
 *    once the user saves we redirect to the edit page so they can add
 *    photos against the real drug id. This avoids the "temp parentId"
 *    binding gymnastics for Phase 1 (see plan §14c trade-off note).
 *
 * Mocks:
 *  - docs/mocks/patient-drug-master-mock.html (right phone — form)
 *  - docs/mocks/patient-search-photo-mock.html (right phone — photo
 *    upload pattern)
 */
const DRUG_TYPE_OPTIONS: ChipOption<DrugType>[] = [
  { id: DrugType.Analgesic, label: 'Analgesic' },
  { id: DrugType.Nsaid, label: 'NSAID' },
  { id: DrugType.Triptan, label: 'Triptan' },
  { id: DrugType.Other, label: 'อื่นๆ' },
]

export function DrugFormPage() {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const {
    mode,
    form,
    setField,
    errors,
    isLoading,
    isLoadingDetail,
    isReady,
    detail,
    submit,
  } = useDrugForm(id)
  const symptomsQuery = useListSymptomsQuery()
  const [attachPhotos] = useAttachDrugPhotosMutation()
  const [createCustomSymptom] = useCreateCustomSymptomMutation()
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [hasAttempted, setHasAttempted] = useState(false)

  const symptomOptions: ChipOption<string>[] = (symptomsQuery.data ?? []).map(
    (s) => ({ id: s.id, label: s.name, color: 'symptom' }),
  )

  // SearchMultiSelect data source — same set as the chips, but mapped to
  // the {id,name} shape the wrapper expects.
  const symptomItems = (symptomsQuery.data ?? []).map((s) => ({
    id: s.id,
    name: s.name,
  }))

  /**
   * Resolve any `__custom:<name>__` sentinels in `treatsSymptomIds` to real
   * server-side symptom ids before submit. Materialises one custom symptom
   * per sentinel via `useCreateCustomSymptomMutation` and folds the
   * returned ids back into the form state so retries are idempotent.
   */
  const resolveTreats = async (): Promise<string[]> => {
    const current = form.treatsSymptomIds
    const resolved: string[] = []
    let mutated = false
    for (const entry of current) {
      if (!isCustomValue(entry)) {
        resolved.push(entry)
        continue
      }
      const name = extractCustomText(entry).trim()
      if (!name) {
        mutated = true
        continue
      }
      const created = await createCustomSymptom({ name }).unwrap()
      resolved.push(created.id)
      mutated = true
    }
    if (mutated) {
      setField('treatsSymptomIds', resolved)
    }
    return resolved
  }

  const handleSubmit = async () => {
    setHasAttempted(true)
    setSubmitError(null)
    if (!isReady) {
      setSubmitError('กรอกข้อมูลให้ครบถ้วนก่อน')
      return
    }
    try {
      // Resolve custom symptoms before persisting the drug so the
      // backend never sees sentinel strings in `treatsSymptomIds`.
      await resolveTreats()
      const saved = await submit()
      if (mode === 'create') {
        // Bounce to the edit page so the user can attach photos now
        // that we have a real drug id.
        navigate(`/health/drugs/${saved.id}/edit`, { replace: true })
      } else {
        navigate('/health/drugs')
      }
    } catch (err) {
      const message =
        err && typeof err === 'object' && 'data' in err && err.data
          ? String((err as { data?: unknown }).data)
          : 'บันทึกไม่สำเร็จ'
      setSubmitError(message)
    }
  }

  const handleStockChange = (delta: number) => {
    const next = Math.max(0, form.stockCount + delta)
    setField('stockCount', next)
  }

  if (mode === 'edit' && isLoadingDetail && !detail) {
    return (
      <div className="health-page">
        <div className="health-page__container">
          <div style={{ padding: 32, color: 'var(--hl-text-muted)' }}>
            กำลังโหลด...
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="health-page">
      <div className="health-page__container">
        <header className="health-header">
          <div className="health-header__user" style={{ fontSize: 15 }}>
            <button
              type="button"
              className="health-icon-btn"
              aria-label="Back"
              onClick={() => navigate('/health/drugs')}
            >
              ←
            </button>
            <span>{mode === 'create' ? 'เพิ่มยาใหม่' : 'แก้ไขยา'}</span>
          </div>
        </header>

        {/* Drug name */}
        <div className="health-form-section">
          <div className="health-form-label">
            📝 ชื่อยา <span className="health-form-req">*</span>
          </div>
          <input
            type="text"
            className="health-form-input"
            placeholder="เช่น Paracetamol"
            value={form.name}
            onChange={(e) => setField('name', e.target.value)}
          />
          {hasAttempted && errors.name && (
            <div className="health-form-error">{errors.name}</div>
          )}
        </div>

        {/* Dose strength */}
        <div className="health-form-section">
          <div className="health-form-label">
            💊 Dose strength <span className="health-form-req">*</span>
          </div>
          <input
            type="text"
            className="health-form-input"
            placeholder="เช่น 500 mg"
            value={form.doseStrength}
            onChange={(e) => setField('doseStrength', e.target.value)}
          />
          {hasAttempted && errors.doseStrength && (
            <div className="health-form-error">{errors.doseStrength}</div>
          )}
        </div>

        {/* Drug type */}
        <div className="health-form-section">
          <div className="health-form-label">🏷 ประเภท</div>
          <ChipGroup
            options={DRUG_TYPE_OPTIONS}
            value={form.drugType}
            // Drug type is required so we never clear it back to null;
            // ignore the "tap active = clear" behavior.
            onChange={(next) => next != null && setField('drugType', next)}
          />
        </div>

        {/* Effect duration range */}
        <div className="health-form-section">
          <div className="health-form-label">⏳ ออกฤทธิ์ระยะเวลา</div>
          <div className="health-range-row">
            <input
              type="number"
              className="health-form-input"
              min={0}
              value={form.effectMin}
              onChange={(e) =>
                setField('effectMin', Number(e.target.value) || 0)
              }
            />
            <span className="health-range-sep">–</span>
            <input
              type="number"
              className="health-form-input"
              min={0}
              value={form.effectMax}
              onChange={(e) =>
                setField('effectMax', Number(e.target.value) || 0)
              }
            />
            <span className="health-range-unit">ชั่วโมง</span>
          </div>
          {hasAttempted && (errors.effectMin || errors.effectMax) && (
            <div className="health-form-error">
              {errors.effectMin ?? errors.effectMax}
            </div>
          )}
        </div>

        {/* Max daily dose */}
        <div className="health-form-section">
          <div className="health-form-label">⛔ Max ต่อวัน</div>
          <div className="health-range-row">
            <input
              type="number"
              className="health-form-input"
              min={0}
              style={{ flex: '0 0 100px' }}
              value={form.maxDaily}
              onChange={(e) =>
                setField('maxDaily', Number(e.target.value) || 0)
              }
            />
            <span className="health-range-unit">
              เม็ด / วัน
              {form.drugType === DrugType.Triptan
                ? ' (Triptan แนะนำ ≤ 2)'
                : ''}
            </span>
          </div>
          {hasAttempted && errors.maxDaily && (
            <div className="health-form-error">{errors.maxDaily}</div>
          )}
        </div>

        {/* Stock counter */}
        <div className="health-form-section">
          <div className="health-form-label">📦 Stock ปัจจุบัน</div>
          <div className="health-stock-counter">
            <button
              type="button"
              onClick={() => handleStockChange(-1)}
              aria-label="ลด stock"
            >
              −
            </button>
            <input
              type="number"
              className="health-form-input health-stock-input"
              min={0}
              value={form.stockCount}
              onChange={(e) =>
                setField('stockCount', Math.max(0, Number(e.target.value) || 0))
              }
            />
            <button
              type="button"
              onClick={() => handleStockChange(1)}
              aria-label="เพิ่ม stock"
            >
              +
            </button>
            <span className="health-range-unit">เม็ด</span>
          </div>
        </div>

        {/* Expiration */}
        <div className="health-form-section">
          <div className="health-form-label">📅 วันหมดอายุ (optional)</div>
          <input
            type="month"
            className="health-form-input"
            value={form.expirationDate ? form.expirationDate.slice(0, 7) : ''}
            onChange={(e) => {
              const v = e.target.value
              setField('expirationDate', v ? `${v}-01` : null)
            }}
          />
        </div>

        {/* Treats */}
        <div className="health-form-section">
          <div className="health-form-label">
            🎯 รักษาอาการอะไร? (เลือกหลายได้)
          </div>
          {symptomsQuery.isLoading ? (
            <div className="health-card" style={{ fontSize: 12, color: 'var(--hl-text-muted)' }}>
              กำลังโหลด...
            </div>
          ) : (
            <>
              <SearchMultiSelect
                id="drug-treats"
                dataSource={symptomItems}
                value={form.treatsSymptomIds}
                onChange={(ids) => setField('treatsSymptomIds', ids)}
                placeholder="พิมพ์ค้นหาอาการ หรือเพิ่มใหม่ (กด Enter)..."
                allowCustom
              />
              {symptomOptions.length > 0 && (
                <div style={{ marginTop: 8 }}>
                  <ChipGroupMulti
                    options={symptomOptions}
                    value={form.treatsSymptomIds.filter(
                      (id) => !isCustomValue(id),
                    )}
                    onChange={(next) => {
                      // Preserve any unresolved custom sentinels (added
                      // via the SearchMultiSelect above) while the user
                      // toggles seed/known chips on/off.
                      const custom = form.treatsSymptomIds.filter((id) =>
                        isCustomValue(id),
                      )
                      setField('treatsSymptomIds', [...next, ...custom])
                    }}
                  />
                </div>
              )}
              {form.treatsSymptomIds.some((id) => isCustomValue(id)) && (
                <div
                  style={{
                    marginTop: 6,
                    fontSize: 11,
                    color: 'var(--hl-text-muted)',
                  }}
                >
                  ✨ จะเพิ่มอาการใหม่ตอนบันทึก:{' '}
                  {form.treatsSymptomIds
                    .filter((id) => isCustomValue(id))
                    .map((id) => extractCustomText(id))
                    .join(', ')}
                </div>
              )}
            </>
          )}
        </div>

        {/* Usage note */}
        <div className="health-form-section">
          <div className="health-form-label">📝 วิธีใช้ (note)</div>
          <textarea
            className="health-form-input health-form-textarea"
            placeholder="เช่น กินทันทีเมื่อมีอาการ aura, ไม่ต้องรอ"
            value={form.usageNote}
            onChange={(e) => setField('usageNote', e.target.value)}
          />
        </div>

        {/* Photos — edit mode only */}
        <div className="health-form-section">
          <div className="health-form-label">📷 รูปซองยา (optional แต่แนะนำ)</div>
          {mode === 'edit' && detail ? (
            <PhotoUploader
              parentType="drug"
              parentId={detail.id}
              existing={detail.photos}
              onUploaded={async (photos) => {
                if (photos.length === 0) return
                await attachPhotos({ drugId: detail.id, photos }).unwrap()
              }}
            />
          ) : (
            <div
              className="health-card"
              style={{ fontSize: 12, color: 'var(--hl-text-muted)' }}
            >
              💾 บันทึกยาก่อน แล้วถึงเพิ่มรูปได้
            </div>
          )}
        </div>

        {submitError && (
          <div
            style={{
              marginTop: 12,
              color: 'var(--hl-danger)',
              fontSize: 13,
              background: 'var(--hl-danger-bg)',
              padding: 10,
              borderRadius: 8,
            }}
          >
            {submitError}
          </div>
        )}

        <div className="health-sticky-save health-sticky-save--row">
          <button
            type="button"
            className="health-cancel-btn"
            onClick={() => navigate('/health/drugs')}
          >
            ยกเลิก
          </button>
          <button
            type="button"
            className="health-save-btn"
            onClick={handleSubmit}
            disabled={isLoading}
          >
            💾 {isLoading ? 'กำลังบันทึก...' : 'บันทึก'}
          </button>
        </div>
      </div>
    </div>
  )
}
