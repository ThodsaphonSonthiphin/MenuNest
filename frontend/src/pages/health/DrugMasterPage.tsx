import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  useDeleteDrugMutation,
  useListDrugsQuery,
} from '../../shared/api/api'
import { DrugType, type DrugDto } from '../../shared/api/healthTypes'
import './styles/health.css'

/**
 * Drug Master — list of every drug the patient has registered. Each row
 * is a photo-forward "elderly-friendly" card (90×90 placeholder when a
 * photo hasn't been uploaded yet) so the user can recognize a drug
 * even when they can't read the name.
 *
 *  - Tap a card → edit form (`/health/drugs/:id/edit`).
 *  - Action icons on the right open the edit form or trigger delete.
 *  - "📷 ถ่ายซองยาใหม่" CTA links to `/health/drugs/new`; OCR auto-fill
 *    is Phase 2 (we keep the visual hook in the UI so it ships now).
 *  - Phase 1 search is client-side; the input filters by name +
 *    dose-strength. Replacing this with Syncfusion AutoComplete is
 *    Phase 2.
 *
 * Mocks:
 *  - docs/mocks/patient-drug-master-mock.html (left phone — basic list)
 *  - docs/mocks/patient-search-photo-mock.html (left phone — photo
 *    cards + section headers)
 */
const DRUG_TYPE_LABEL: Record<DrugType, string> = {
  [DrugType.Analgesic]: 'Analgesic',
  [DrugType.Nsaid]: 'NSAID',
  [DrugType.Triptan]: 'Triptan',
  [DrugType.Other]: 'Other',
}

const DRUG_TYPE_CLASS: Record<DrugType, string> = {
  [DrugType.Analgesic]: 'analgesic',
  [DrugType.Nsaid]: 'nsaid',
  [DrugType.Triptan]: 'triptan',
  [DrugType.Other]: 'other',
}

function effectDurationLabel(d: DrugDto): string {
  if (d.effectDurationMinHours === d.effectDurationMaxHours)
    return `${d.effectDurationMinHours} ชม.`
  return `${d.effectDurationMinHours}-${d.effectDurationMaxHours} ชม.`
}

function stockText(d: DrugDto): { text: string; severity: 'ok' | 'low' | 'out' } {
  if (d.stockCount <= 0)
    return { text: '❌ หมด — ซื้อใหม่', severity: 'out' }
  if (d.stockCount <= 2)
    return { text: `⚠ เหลือ ${d.stockCount} เม็ด — ใกล้หมด`, severity: 'low' }
  return {
    text: `เหลือ ${d.stockCount} เม็ด • ${effectDurationLabel(d)}`,
    severity: 'ok',
  }
}

export function DrugMasterPage() {
  const navigate = useNavigate()
  const { data: drugs, isLoading, error } = useListDrugsQuery()
  const [deleteDrug, deleteState] = useDeleteDrugMutation()
  const [search, setSearch] = useState('')
  const [pendingDelete, setPendingDelete] = useState<DrugDto | null>(null)

  const filtered = useMemo(() => {
    const list = drugs ?? []
    if (!search.trim()) return list
    const q = search.trim().toLowerCase()
    return list.filter(
      (d) =>
        d.name.toLowerCase().includes(q) ||
        d.doseStrength.toLowerCase().includes(q) ||
        (d.activeIngredient ?? '').toLowerCase().includes(q),
    )
  }, [drugs, search])

  const sorted = useMemo(
    () => [...filtered].sort((a, b) => a.name.localeCompare(b.name, 'th')),
    [filtered],
  )

  const handleDelete = async () => {
    if (!pendingDelete) return
    const id = pendingDelete.id
    setPendingDelete(null)
    await deleteDrug(id).unwrap()
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
            <span>ยาในระบบ</span>
          </div>
        </header>

        {/* Add new drug — primary CTA */}
        <button
          type="button"
          className="health-add-drug-btn health-add-drug-btn--primary"
          onClick={() => navigate('/health/drugs/new')}
        >
          ➕ เพิ่มยาใหม่
        </button>

        {/* Photo CTA — Phase 2 will do OCR auto-fill */}
        <button
          type="button"
          className="health-photo-cta-card"
          onClick={() => navigate('/health/drugs/new')}
        >
          <div className="health-photo-cta-card__icon">📷</div>
          <div className="health-photo-cta-card__text">ถ่ายซองยาใหม่</div>
          <div className="health-photo-cta-card__sub">
            ระบบจะ scan ชื่อยา + dose อัตโนมัติ (OCR — Phase 2)
          </div>
        </button>

        {/* Search */}
        <input
          type="search"
          className="health-search-box"
          placeholder="ค้นหาชื่อยา หรือ dose..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />

        {isLoading && (
          <div style={{ padding: 32, color: 'var(--hl-text-muted)', fontSize: 13 }}>
            กำลังโหลด...
          </div>
        )}

        {error && (
          <div className="health-card" style={{ color: 'var(--hl-danger)' }}>
            ⚠ โหลดยาไม่สำเร็จ
          </div>
        )}

        {!isLoading && sorted.length === 0 && (
          <div className="health-empty-state">
            {search.trim()
              ? `ไม่พบยาที่ตรงกับ "${search}"`
              : 'ยังไม่มียาในระบบ — เพิ่มยาแรกของคุณ'}
          </div>
        )}

        {sorted.length > 0 && (
          <div className="health-section-title">
            <span>ยาทั้งหมด</span>
            <span className="health-section-title__count">{sorted.length}</span>
          </div>
        )}

        {sorted.map((drug) => {
          const stock = stockText(drug)
          return (
            <div key={drug.id} className="health-drug-photo-card">
              <button
                type="button"
                className={`health-drug-photo ${
                  drug.hasPhoto ? '' : 'health-drug-photo--placeholder'
                }`}
                onClick={() => navigate(`/health/drugs/${drug.id}/edit`)}
                aria-label={`Edit ${drug.name}`}
              >
                {drug.firstPhotoUrl ? (
                  <img
                    src={drug.firstPhotoUrl}
                    alt={drug.name}
                    className="health-drug-photo__img"
                  />
                ) : (
                  <span aria-hidden>📷</span>
                )}
              </button>
              <div className="health-drug-info">
                <div className="health-drug-name-big">{drug.name}</div>
                <div className="health-drug-meta-row">
                  <span
                    className={`health-drug-type-tag health-drug-type-tag--${
                      DRUG_TYPE_CLASS[drug.drugType]
                    }`}
                  >
                    {DRUG_TYPE_LABEL[drug.drugType]}
                  </span>
                  <span className="health-drug-meta-row__dose">
                    {drug.doseStrength}
                  </span>
                </div>
                <div
                  className={`health-drug-stock-line health-drug-stock-line--${stock.severity}`}
                >
                  {stock.text}
                </div>
              </div>
              <div className="health-drug-photo-actions">
                <button
                  type="button"
                  className="health-icon-btn-sm"
                  onClick={() => navigate(`/health/drugs/${drug.id}/edit`)}
                  aria-label={`Edit ${drug.name}`}
                  title="แก้ไข"
                >
                  ✏️
                </button>
                <button
                  type="button"
                  className="health-icon-btn-sm health-icon-btn-sm--danger"
                  onClick={() => setPendingDelete(drug)}
                  aria-label={`Delete ${drug.name}`}
                  title="ลบ"
                >
                  🗑
                </button>
              </div>
            </div>
          )
        })}

        {pendingDelete && (
          <div
            className="health-modal-backdrop"
            onClick={() => setPendingDelete(null)}
            role="presentation"
          >
            <div
              className="health-modal"
              onClick={(e) => e.stopPropagation()}
              role="dialog"
              aria-label="ลบยา"
            >
              <div className="health-modal__title">
                ลบ {pendingDelete.name}?
              </div>
              <div style={{ fontSize: 13, color: 'var(--hl-text-muted)' }}>
                ลบยานี้ออกจากระบบ — ประวัติ intake ที่ใช้ยานี้จะยังคงอยู่
                แต่ไม่สามารถใช้ยานี้กับ episode ใหม่ได้
              </div>
              <div className="health-modal__actions">
                <button
                  type="button"
                  className="health-action-btn"
                  onClick={() => setPendingDelete(null)}
                >
                  ยกเลิก
                </button>
                <button
                  type="button"
                  className="health-action-btn health-action-btn--danger"
                  onClick={handleDelete}
                  disabled={deleteState.isLoading}
                >
                  {deleteState.isLoading ? 'กำลังลบ...' : 'ลบ'}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
