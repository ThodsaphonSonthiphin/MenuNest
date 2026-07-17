import {useState} from 'react'
import type {SeasonPeriod} from '../../../shared/api/api'
import {THAI_MONTHS, rangeLabel} from '../lib/season'

type Draft = {kind: 'Good' | 'Bad'; months: number[]; note: string}
const NOW_MONTH = new Date().getMonth() // display-only "now" marker

export function PlaceSeasonEditor({
  periods,
  onChange,
}: {
  periods: SeasonPeriod[]
  onChange: (periods: SeasonPeriod[]) => void
}) {
  const [draft, setDraft] = useState<Draft | null>(null)

  const ribbonKind = (m: number): 'good' | 'bad' | 'none' => {
    if (draft?.months.includes(m)) return draft.kind === 'Bad' ? 'bad' : 'good'
    if (periods.some((p) => p.kind === 'Bad' && p.months.includes(m))) return 'bad'
    if (periods.some((p) => p.kind === 'Good' && p.months.includes(m))) return 'good'
    return 'none'
  }

  const toggleMonth = (m: number) => {
    if (!draft) return
    setDraft({...draft, months: draft.months.includes(m) ? draft.months.filter((x) => x !== m) : [...draft.months, m]})
  }

  const saveDraft = () => {
    if (!draft || draft.months.length === 0) return
    onChange([...periods, {kind: draft.kind, months: [...draft.months].sort((a, b) => a - b), note: draft.note.trim() || null}])
    setDraft(null)
  }

  return (
    <section className="se-sec season-editor">
      <div className="se-sec-head">ช่วงเดือน (ควรไป / ควรเลี่ยง)</div>

      <div className="season-ribbon" role="group" aria-label="ปฏิทินฤดูกาล">
        {THAI_MONTHS.map((label, m) => (
          <button
            type="button"
            key={m}
            className={`sr-cell ${ribbonKind(m)}${m === NOW_MONTH ? ' now' : ''}${draft?.months.includes(m) ? ' draft' : ''}`}
            aria-pressed={draft ? draft.months.includes(m) : undefined}
            onClick={() => (draft ? toggleMonth(m) : undefined)}
            disabled={!draft}
          >
            {label}
          </button>
        ))}
      </div>

      <ul className="season-rows">
        {periods.map((p, i) => (
          <li className={`sp-row ${p.kind === 'Bad' ? 'bad' : 'good'}`} key={i}>
            <span className="sp-pill">{p.kind === 'Bad' ? 'ควรเลี่ยง' : 'ควรไป'}</span>
            <span className="sp-range">{rangeLabel(p.months)}</span>
            {p.note && <span className="sp-note">{p.note}</span>}
            <button type="button" className="sp-del" aria-label="ลบช่วง" onClick={() => onChange(periods.filter((_, j) => j !== i))}>✕</button>
          </li>
        ))}
      </ul>

      {draft ? (
        <div className="sp-draft">
          <div className="sp-kind">
            <button type="button" className={`sp-kbtn good${draft.kind === 'Good' ? ' active' : ''}`} onClick={() => setDraft({...draft, kind: 'Good'})}>ควรไป</button>
            <button type="button" className={`sp-kbtn bad${draft.kind === 'Bad' ? ' active' : ''}`} onClick={() => setDraft({...draft, kind: 'Bad'})}>ควรเลี่ยง</button>
          </div>
          <input className="sp-note-input" placeholder="เหตุผล (ไม่บังคับ)" value={draft.note} onChange={(e) => setDraft({...draft, note: e.target.value})} />
          <div className="sp-draft-foot">
            <button type="button" className="sp-cancel" onClick={() => setDraft(null)}>ยกเลิก</button>
            <button type="button" className="sp-save" disabled={draft.months.length === 0} onClick={saveDraft}>เพิ่มช่วง</button>
          </div>
          <p className="sp-hint">แตะเดือนบนแถบด้านบนเพื่อเลือก</p>
        </div>
      ) : (
        periods.length < 12 && (
          <button type="button" className="sp-add" onClick={() => setDraft({kind: 'Bad', months: [], note: ''})}>+ เพิ่มช่วง</button>
        )
      )}
    </section>
  )
}