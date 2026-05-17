import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChipGroup, ChipGroupMulti } from './components/ChipGroup'
import type { ChipOption } from './components/ChipGroup'
import { SeveritySlider } from './components/SeveritySlider'
import { SearchAutoComplete } from './components/SearchAutoComplete'
import {
  extractCustomText,
  isCustomValue,
} from './components/customValueSentinel'
import { useQuickLogForm } from './hooks/useQuickLogForm'
import { useStartEpisode } from './hooks/useStartEpisode'
import {
  useCreateCustomSymptomMutation,
  useCreateCustomTriggerMutation,
} from '../../shared/api/api'
import {
  AssociatedSymptom,
  AuraType,
  FunctionalImpact,
  SymptomLocation,
  SymptomQuality,
} from '../../shared/api/healthTypes'
import './styles/health.css'

/**
 * Quick Log Attack — the "I'm hurting right now" entry point.
 *
 * Severity is the only hard requirement; everything else is optional so
 * the user can save in two taps when they need to crash on the couch.
 * After save we navigate to the new Active Episode page so the user
 * lands somewhere actionable, never back to a blank Home.
 *
 * Mock: docs/mocks/patient-quick-log-mock.html.
 */
const LOCATION_OPTIONS: ChipOption<SymptomLocation>[] = [
  { id: SymptomLocation.Left, label: 'ซ้าย' },
  { id: SymptomLocation.Right, label: 'ขวา' },
  { id: SymptomLocation.Bilateral, label: 'ทั้งสอง' },
  { id: SymptomLocation.Frontal, label: 'Frontal' },
  { id: SymptomLocation.Temporal, label: 'Temporal' },
  { id: SymptomLocation.Occipital, label: 'Occipital' },
]

const QUALITY_OPTIONS: ChipOption<SymptomQuality>[] = [
  { id: SymptomQuality.Throbbing, label: 'ตุบๆ' },
  { id: SymptomQuality.Pressure, label: 'บีบ' },
  { id: SymptomQuality.Stabbing, label: 'แทง' },
  { id: SymptomQuality.Burning, label: 'แสบ' },
]

// Aura is single-select in the mock (one of: none / visual / sensory /
// speech / motor). Our `hasAura/auraTypes` shape stores it as a boolean
// + array; we model "none" as `null`, anything else as a single-type
// array, so the chip group can drive both fields.
type AuraChoice = 'none' | AuraType
const AURA_OPTIONS: ChipOption<AuraChoice>[] = [
  { id: 'none', label: 'ไม่มี', color: 'aura' },
  { id: AuraType.Visual, label: 'Visual', color: 'aura' },
  { id: AuraType.Sensory, label: 'Sensory', color: 'aura' },
  { id: AuraType.Speech, label: 'Speech', color: 'aura' },
  { id: AuraType.Motor, label: 'Motor', color: 'aura' },
]

const ASSOC_OPTIONS: ChipOption<AssociatedSymptom>[] = [
  { id: AssociatedSymptom.Nausea, label: 'คลื่นไส้', color: 'symptom' },
  { id: AssociatedSymptom.Vomiting, label: 'อาเจียน', color: 'symptom' },
  { id: AssociatedSymptom.Photophobia, label: 'กลัวแสง', color: 'symptom' },
  { id: AssociatedSymptom.Phonophobia, label: 'กลัวเสียง', color: 'symptom' },
  { id: AssociatedSymptom.Osmophobia, label: 'กลัวกลิ่น', color: 'symptom' },
]

const IMPACT_OPTIONS: ChipOption<FunctionalImpact>[] = [
  { id: FunctionalImpact.None, label: 'ทำงานต่อได้' },
  { id: FunctionalImpact.Mild, label: 'ฝืน' },
  { id: FunctionalImpact.Moderate, label: 'ลำบาก' },
  { id: FunctionalImpact.SevereBedrest, label: 'ต้องนอน' },
]

function formatNowTime(): string {
  const d = new Date()
  return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

export function QuickLogAttackPage() {
  const navigate = useNavigate()
  const form = useQuickLogForm()
  const { startEpisode, isLoading } = useStartEpisode()
  const [createCustomSymptom] = useCreateCustomSymptomMutation()
  const [createCustomTrigger, createCustomTriggerState] =
    useCreateCustomTriggerMutation()
  const [submitError, setSubmitError] = useState<string | null>(null)

  // "Add new trigger" modal state — small inline AutoComplete that
  // resolves to either an existing trigger id or a brand new server-side
  // custom trigger via `useCreateCustomTriggerMutation`. Keeping it in
  // page state (rather than a separate component) is fine while it's a
  // one-off and we want to stay close to the existing chip state below.
  const [triggerModalOpen, setTriggerModalOpen] = useState(false)
  const [triggerModalValue, setTriggerModalValue] = useState<string | null>(null)
  const [triggerModalError, setTriggerModalError] = useState<string | null>(null)

  // Build trigger chips from server reference data. Multi-select. Color
  // accent stays default (accent purple) per mock.
  const triggerOptions: ChipOption<string>[] = (form.triggers ?? []).map((t) => ({
    id: t.id,
    label: t.name,
  }))

  // SearchAutoComplete data source mirrors the chip-group list.
  const symptomItems = (form.symptoms ?? []).map((s) => ({
    id: s.id,
    name: s.name,
  }))
  const triggerItems = (form.triggers ?? []).map((t) => ({
    id: t.id,
    name: t.name,
  }))

  // Map aura state back to the chip-group's single-select model.
  const auraSelection: AuraChoice | null =
    !form.hasAura && form.auraTypes.length === 0
      ? 'none'
      : (form.auraTypes[0] ?? null)

  /**
   * If the user typed a brand-new symptom (sentinel value from
   * SearchAutoComplete with `allowCustom`), materialise it first via the
   * custom-symptom mutation so the StartEpisode payload always carries a
   * real Guid. On success we also pin the resolved id on the form state
   * so retries don't re-create the row.
   */
  const resolveSymptomId = async (): Promise<string | null> => {
    const current = form.symptomId
    if (!current) return null
    if (!isCustomValue(current)) return current
    const name = extractCustomText(current).trim()
    if (!name) return null
    const created = await createCustomSymptom({ name }).unwrap()
    form.setSymptomId(created.id)
    return created.id
  }

  const handleSubmit = async () => {
    setSubmitError(null)
    try {
      const resolvedId = await resolveSymptomId()
      if (!resolvedId) {
        setSubmitError('โปรดเลือก symptom ก่อน')
        return
      }
      const req = form.buildRequest()
      if (!req) {
        setSubmitError('โปรดเลือก symptom ก่อน')
        return
      }
      await startEpisode({ ...req, symptomId: resolvedId })
      // useStartEpisode handles navigation on success.
    } catch (err) {
      const message =
        (err && typeof err === 'object' && 'data' in err && err.data
          ? String((err as { data?: unknown }).data)
          : null) ?? 'เกิดข้อผิดพลาด กรุณาลองอีกครั้ง'
      setSubmitError(message)
    }
  }

  const handleConfirmTrigger = async () => {
    setTriggerModalError(null)
    const picked = triggerModalValue
    if (!picked) {
      setTriggerModalError('โปรดเลือกหรือพิมพ์ trigger ก่อน')
      return
    }
    try {
      let triggerId = picked
      if (isCustomValue(picked)) {
        const name = extractCustomText(picked).trim()
        if (!name) {
          setTriggerModalError('โปรดพิมพ์ชื่อ trigger')
          return
        }
        const created = await createCustomTrigger({ name }).unwrap()
        triggerId = created.id
      }
      if (!form.triggerIds.includes(triggerId)) {
        form.setTriggerIds([...form.triggerIds, triggerId])
      }
      setTriggerModalOpen(false)
      setTriggerModalValue(null)
    } catch (err) {
      const message =
        err && typeof err === 'object' && 'data' in err && err.data
          ? String((err as { data?: unknown }).data)
          : 'บันทึก trigger ไม่สำเร็จ'
      setTriggerModalError(message)
    }
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
              onClick={() => navigate('/health')}
            >
              ←
            </button>
            <span>มี Migraine Attack</span>
          </div>
        </header>

        <div className="health-time-row">
          <div>
            <div className="health-time-row__info">เริ่มปวด</div>
            <div className="health-time-row__value">ตอนนี้ • {formatNowTime()}</div>
          </div>
        </div>

        <SeveritySlider
          question="รุนแรงแค่ไหน?"
          value={form.severity}
          onChange={form.setSeverity}
          min={1}
          max={10}
        />

        <div className="health-section-title">🩺 อาการอะไร?</div>
        {form.isLoadingRefs ? (
          <div className="health-card">กำลังโหลด...</div>
        ) : (
          <SearchAutoComplete
            id="quick-log-symptom"
            dataSource={symptomItems}
            value={form.symptomId || null}
            onChange={(id) => form.setSymptomId(id ?? '')}
            placeholder="เลือกหรือพิมพ์ symptom..."
            allowCustom
          />
        )}

        <div className="health-section-title">📍 ด้านไหน?</div>
        <ChipGroup
          options={LOCATION_OPTIONS}
          value={form.location}
          onChange={form.setLocation}
        />

        <div className="health-section-title">💢 ลักษณะ</div>
        <ChipGroup
          options={QUALITY_OPTIONS}
          value={form.quality}
          onChange={form.setQuality}
        />

        <div className="health-section-title">⚡ Trigger (เลือกได้หลาย)</div>
        {triggerOptions.length === 0 ? (
          <div className="health-card" style={{ color: 'var(--hl-text-muted)', fontSize: 12 }}>
            ยังไม่มี triggers — จะมี seeds พื้นฐานหลัง backend ครั้งแรก
          </div>
        ) : (
          <ChipGroupMulti
            options={triggerOptions}
            value={form.triggerIds}
            onChange={form.setTriggerIds}
          />
        )}
        <button
          type="button"
          className="health-add-trigger-link"
          onClick={() => {
            setTriggerModalValue(null)
            setTriggerModalError(null)
            setTriggerModalOpen(true)
          }}
        >
          ✏️ เพิ่ม trigger ใหม่
        </button>

        <div className="health-section-title">🌀 มี aura ก่อนหน้านี้?</div>
        <ChipGroup
          options={AURA_OPTIONS}
          value={auraSelection}
          onChange={(next) => form.toggleAura(next ?? 'none')}
        />

        <div className="health-section-title">⚭ รอบเดือน</div>
        <button
          type="button"
          className={`health-check-row${form.isOnPeriod ? ' health-check-row--checked' : ''}`}
          onClick={() => form.setIsOnPeriod(!form.isOnPeriod)}
          aria-pressed={form.isOnPeriod}
        >
          <div className="health-check-row__box">{form.isOnPeriod ? '✓' : ''}</div>
          <div>
            <div style={{ fontWeight: 600, fontSize: 14 }}>กำลังมีประจำเดือนอยู่</div>
            <div style={{ fontSize: 11, color: 'var(--hl-text-muted)' }}>
              สำหรับการวิเคราะห์ pattern
            </div>
          </div>
        </button>

        <div className="health-section-title">🤢 อาการอื่นๆ (เลือกได้หลาย)</div>
        <ChipGroupMulti
          options={ASSOC_OPTIONS}
          value={form.associatedSymptoms}
          onChange={form.setAssociatedSymptoms}
        />

        <div className="health-section-title">📊 ผลกระทบ</div>
        <ChipGroup
          options={IMPACT_OPTIONS}
          value={form.functionalImpact}
          onChange={form.setFunctionalImpact}
        />

        <div className="health-section-title">📝 บันทึก (optional)</div>
        <textarea
          className="health-note-input"
          placeholder="เช่น: นั่งหน้าจอนานเกินไป, ลืมกินข้าวเช้า"
          value={form.notes}
          onChange={(e) => form.setNotes(e.target.value)}
        />

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

        <div className="health-sticky-save">
          <button
            type="button"
            className="health-save-btn"
            onClick={handleSubmit}
            disabled={isLoading || !form.isReady}
          >
            💾 {isLoading ? 'กำลังบันทึก...' : 'บันทึก attack'}
          </button>
          <div className="health-save-btn-sub">
            2 taps จาก home ก็บันทึกได้ (ที่เหลือ optional)
          </div>
        </div>

        {triggerModalOpen && (
          <div
            className="health-modal-backdrop"
            onClick={() => setTriggerModalOpen(false)}
            role="presentation"
          >
            <div
              className="health-modal"
              onClick={(e) => e.stopPropagation()}
              role="dialog"
              aria-label="เพิ่ม trigger ใหม่"
            >
              <div className="health-modal__title">เพิ่ม trigger ใหม่</div>
              <div style={{ fontSize: 12, color: 'var(--hl-text-muted)', marginBottom: 8 }}>
                เลือกจากที่มีอยู่ หรือพิมพ์ชื่อใหม่แล้วกด Enter
              </div>
              <SearchAutoComplete
                id="quick-log-add-trigger"
                dataSource={triggerItems}
                value={triggerModalValue}
                onChange={setTriggerModalValue}
                placeholder="เช่น แดดจัด, ไม่ได้ดื่มน้ำ..."
                allowCustom
              />
              {triggerModalError && (
                <div
                  style={{
                    marginTop: 8,
                    color: 'var(--hl-danger)',
                    fontSize: 12,
                  }}
                >
                  {triggerModalError}
                </div>
              )}
              <div className="health-modal__actions">
                <button
                  type="button"
                  className="health-action-btn"
                  onClick={() => setTriggerModalOpen(false)}
                >
                  ยกเลิก
                </button>
                <button
                  type="button"
                  className="health-action-btn health-action-btn--primary"
                  onClick={handleConfirmTrigger}
                  disabled={createCustomTriggerState.isLoading || !triggerModalValue}
                >
                  {createCustomTriggerState.isLoading ? 'กำลังบันทึก...' : 'เพิ่ม'}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
