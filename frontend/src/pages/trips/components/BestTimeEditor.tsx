import {useState} from 'react'
import {TimePicker} from '@syncfusion/react-calendars'
import type {TimePickerChangeEvent} from '@syncfusion/react-calendars'
import type {BestTimeWindow} from '../../../shared/api/api'
import {hmsToDate, dateToHms} from '../utils/time'

const MAX_WINDOWS = 6
type Draft = {start: string | null; end: string | null; note: string}

export function BestTimeEditor({
  windows,
  onChange,
}: {
  windows: BestTimeWindow[]
  onChange: (windows: BestTimeWindow[]) => void
}) {
  const [draft, setDraft] = useState<Draft | null>(null)
  const fmt = (hms: string) => hms.slice(0, 5)
  const draftValid = !!draft && !!draft.start && !!draft.end && draft.end > draft.start

  const saveDraft = () => {
    if (!draft || !draft.start || !draft.end || draft.end <= draft.start) return
    onChange([...windows, {start: draft.start, end: draft.end, note: draft.note.trim() || null}])
    setDraft(null)
  }

  return (
    <section className="se-sec se-best">
      <div className="se-sec-head">
        <span className="se-ico">🕐</span>ช่วงเวลาที่ดี
        <span className="se-pill">หลายช่วงได้</span>
      </div>
      <p className="se-sub">ใส่ช่วงเวลาในวันที่เหมาะจะไป — ได้หลายช่วง แต่ละช่วงใส่เหตุผลได้</p>

      <ul className="season-rows">
        {windows.map((w, i) => (
          <li className="sp-row good" key={i}>
            <span className="sp-range">{fmt(w.start)}–{fmt(w.end)}</span>
            {w.note && <span className="sp-note">{w.note}</span>}
            <button type="button" className="sp-del" aria-label="ลบช่วง" onClick={() => onChange(windows.filter((_, j) => j !== i))}>✕</button>
          </li>
        ))}
      </ul>

      {draft ? (
        <div className="sp-draft">
          <div className="se-time-grid">
            <div className="se-time-card">
              <span className="se-time-lab">เริ่ม</span>
              <TimePicker value={hmsToDate(draft.start)} onChange={(e: TimePickerChangeEvent) => setDraft({...draft, start: dateToHms(e.value)})} format="HH:mm" step={15} placeholder="--:--" />
            </div>
            <span className="se-time-dash">–</span>
            <div className="se-time-card">
              <span className="se-time-lab">สิ้นสุด</span>
              <TimePicker value={hmsToDate(draft.end)} onChange={(e: TimePickerChangeEvent) => setDraft({...draft, end: dateToHms(e.value)})} format="HH:mm" step={15} placeholder="--:--" />
            </div>
          </div>
          <input className="sp-note-input" placeholder="เหตุผล (ไม่บังคับ)" value={draft.note} onChange={(e) => setDraft({...draft, note: e.target.value})} />
          <div className="sp-draft-foot">
            <button type="button" className="sp-cancel" onClick={() => setDraft(null)}>ยกเลิก</button>
            <button type="button" className="sp-save" disabled={!draftValid} onClick={saveDraft}>เพิ่มช่วง</button>
          </div>
          <p className="sp-hint">เวลาสิ้นสุดต้องหลังเวลาเริ่ม</p>
        </div>
      ) : (
        windows.length < MAX_WINDOWS && (
          <button type="button" className="sp-add" onClick={() => setDraft({start: null, end: null, note: ''})}>+ เพิ่มช่วง</button>
        )
      )}
    </section>
  )
}
