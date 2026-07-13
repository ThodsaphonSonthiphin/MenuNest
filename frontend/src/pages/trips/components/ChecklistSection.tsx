import {useState} from 'react'
import {
  useListChecklistItemsQuery,
  useAttachChecklistItemMutation,
  useDetachChecklistItemMutation,
  useSetChecklistEntryCheckedMutation,
  type PlaceChecklistEntry,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {ChecklistIcon} from './ChecklistIcon'
import {
  MAX_CHECKLIST_ITEMS_PER_PLACE,
  isValidChecklistName,
  normalizeChecklistName,
  matchLibrary,
  exactMatch,
  checklistProgress,
} from '../lib/checklist'

export function ChecklistSection({
  tripId,
  placeId,
  checklist,
}: {
  tripId: string
  placeId: string
  checklist: PlaceChecklistEntry[]
}) {
  const {data: library} = useListChecklistItemsQuery()
  const [attachChecklist] = useAttachChecklistItemMutation()
  const [detachChecklist] = useDetachChecklistItemMutation()
  const [setChecklistChecked] = useSetChecklistEntryCheckedMutation()
  const [ckDraft, setCkDraft] = useState('')
  const [ckError, setCkError] = useState<string | null>(null)

  const progress = checklistProgress(checklist)
  const attachedItemIds = new Set(checklist.map((e) => e.checklistItemId))
  const suggestions = matchLibrary(ckDraft, library ?? []).filter((i) => !attachedItemIds.has(i.id))
  const showCreate = isValidChecklistName(ckDraft) && !exactMatch(ckDraft, library ?? [])

  const addChecklist = async (name: string) => {
    setCkError(null)
    if (!isValidChecklistName(name)) { setCkError('ชื่อไม่ถูกต้อง หรือยาวเกิน 100 ตัวอักษร'); return }
    if (checklist.length >= MAX_CHECKLIST_ITEMS_PER_PLACE) { setCkError(`เพิ่มได้สูงสุด ${MAX_CHECKLIST_ITEMS_PER_PLACE} รายการ`); return }
    try { await attachChecklist({tripId, placeId, name: normalizeChecklistName(name)}).unwrap(); setCkDraft('') }
    catch (err) { setCkError(getErrorMessage(err)) }
  }
  const toggleChecklist = async (entryId: string, next: boolean) => {
    setCkError(null)
    try { await setChecklistChecked({tripId, placeId, entryId, isChecked: next}).unwrap() }
    catch (err) { setCkError(getErrorMessage(err)) }
  }
  const removeChecklist = async (entryId: string) => {
    setCkError(null)
    try { await detachChecklist({tripId, placeId, entryId}).unwrap() }
    catch (err) { setCkError(getErrorMessage(err)) }
  }

  return (
    <section className="se-sec">
      <div className="se-sec-head">
        <ChecklistIcon />สิ่งที่ต้องเตรียม
        {checklist.length > 0 && (<span className="se-ck-pill">เตรียมแล้ว {progress.done}/{progress.total}</span>)}
      </div>
      {checklist.length > 0 && (
        <div className="ck-card">
          {checklist.map((e) => (
            <label className={e.isChecked ? 'ck-row done' : 'ck-row'} key={e.id}>
              <input type="checkbox" checked={e.isChecked} onChange={(ev) => toggleChecklist(e.id, ev.target.checked)} />
              <span className="ck-name">{e.name}</span>
              <button type="button" className="ck-del" aria-label="เอาออก" onClick={(ev) => { ev.preventDefault(); removeChecklist(e.id) }}>
                <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2.2} strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
              </button>
            </label>
          ))}
        </div>
      )}
      <div className="ck-add-wrap">
        <div className="ck-add-in">
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
          <input value={ckDraft} placeholder="พิมพ์ของที่ต้องเตรียม…" onChange={(ev) => setCkDraft(ev.target.value)}
            onKeyDown={(ev) => { if (ev.key === 'Enter') { ev.preventDefault(); if (ckDraft.trim()) addChecklist(ckDraft) } }} />
        </div>
        {ckDraft.trim().length > 0 && (suggestions.length > 0 || showCreate) && (
          <div className="ck-ac">
            {suggestions.length > 0 && <div className="ac-h">จากคลังของคุณ</div>}
            {suggestions.map((i) => (
              <button type="button" key={i.id} onClick={() => addChecklist(i.name)}>
                <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true"><path d="M3 12h18M3 6h18M3 18h18" /></svg>
                {i.name}<span className="lib">ในคลัง</span>
              </button>
            ))}
            {showCreate && (
              <button type="button" className="create" onClick={() => addChecklist(ckDraft)}>
                <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
                สร้าง “{normalizeChecklistName(ckDraft)}” ใหม่
              </button>
            )}
          </div>
        )}
      </div>
      {ckError && <p className="trips-field-error">{ckError}</p>}
    </section>
  )
}