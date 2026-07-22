import {useState} from 'react'
import {TimePicker} from '@syncfusion/react-calendars'
import type {TimePickerChangeEvent} from '@syncfusion/react-calendars'
import type {BestTimeWindow} from '../../../shared/api/api'
import {hmsToDate, dateToHms} from '../utils/time'
import {ClockIcon} from './ClockIcon'

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
      <p className="se-sub">กำหนดช่วงเวลาในวันที่เหมาะจะไปสถานที่นี้ — ใส่ได้หลายช่วง แต่ละช่วงมีเหตุผลได้</p>

      <ul className="bt-rows">
        {windows.map((w, i) => (
          <li className="bt-row" key={i}>
            <span className="bt-clock"><ClockIcon /></span>
            <span className="bt-range">{fmt(w.start)}–{fmt(w.end)}</span>
            {w.note && <span className="bt-note">{w.note}</span>}
            <button type="button" className="bt-del" aria-label="ลบช่วง" onClick={() => onChange(windows.filter((_, j) => j !== i))}>✕</button>
          </li>
        ))}
      </ul>

      {draft && (
        <div className="bt-draft">
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
          <input className="bt-note-input" placeholder="เหตุผล (ไม่บังคับ)" value={draft.note} onChange={(e) => setDraft({...draft, note: e.target.value})} />
          <div className="bt-draft-foot">
            <button type="button" className="bt-cancel" onClick={() => setDraft(null)}>ยกเลิก</button>
            <button type="button" className="bt-save" disabled={!draftValid} onClick={saveDraft}>เพิ่มช่วง</button>
          </div>
        </div>
      )}

      {!draft && windows.length < MAX_WINDOWS && (
        <button type="button" className="bt-add" onClick={() => setDraft({start: null, end: null, note: ''})}>+ เพิ่มช่วง</button>
      )}

      <p className="bt-cap">สูงสุด {MAX_WINDOWS} ช่วง · เวลาสิ้นสุดต้องหลังเวลาเริ่ม</p>
    </section>
  )
}
