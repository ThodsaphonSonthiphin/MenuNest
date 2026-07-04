// frontend/src/pages/trips/timingFlag.ts
// Single source of truth for a timing flag's Thai reason + suggested-fix wording.
// Data-only (no JSX) — mirrors placeCategory.ts. Copy is verbatim from the design
// spec §5. The dash between best-window times is EN DASH (–, U+2013).
import type {FlagSeverity, TimingFlag} from './hooks/useSchedule'

export function flagText(flag: TimingFlag): {reasonLine: string; fixLine: string} {
  switch (flag.reason) {
    case 'overflow':
      return {reasonLine: `แผนวันนี้ยาวข้ามเที่ยงคืน (ถึง ${flag.arrival})`, fixLine: 'ตัดจุดแวะออก หรือเริ่มวันให้เร็วขึ้น'}
    case 'off-window':
      return {
        reasonLine: `${flag.windowDir === 'before' ? 'ไปถึงก่อนช่วงแนะนำ' : 'ไปถึงหลังช่วงแนะนำ'} · ช่วงเหมาะ ${flag.bestStart}–${flag.bestEnd}`,
        fixLine: flag.windowDir === 'before' ? 'เลื่อนสตอปนี้ไปช่วงหลัง' : 'เลื่อนสตอปนี้ขึ้นก่อนหน้า',
      }
    case 'closed':
      switch (flag.closedKind) {
        case 'before-open': return {reasonLine: `ยังไม่เปิดตอนไปถึง · เปิด ${flag.reopenAt}`, fixLine: 'เลื่อนสตอปนี้ไปช่วงสาย'}
        case 'on-break':    return {reasonLine: `ปิดพักช่วงนี้ · เปิดอีกที ${flag.reopenAt}`, fixLine: 'เลี่ยงช่วงพักกลางวัน'}
        case 'after-close': return {reasonLine: 'ร้านปิดแล้วตอนไปถึง', fixLine: 'เลื่อนสตอปนี้ให้เร็วขึ้น'}
        default:            return {reasonLine: 'ร้านปิดทั้งวันนี้', fixLine: 'ย้ายไปวันอื่น หรือเอาออก'}
      }
  }
}

/** Short Thai severity word for accessible names / summaries. */
export function severityWord(severity: FlagSeverity): string {
  return severity === 'problem' ? 'ต้องแก้' : 'น่าปรับ'
}
