// frontend/src/pages/trips/components/BestTimeBar.tsx
import {TimePicker} from '@syncfusion/react-calendars'
import type {TimePickerChangeEvent} from '@syncfusion/react-calendars'

/**
 * Convert a stored "HH:mm:ss" string to a Date (local-time, today's date as base).
 * Avoids TZ-shift issues by using setHours/setMinutes/setSeconds.
 */
function hmsToDate(hms: string | null): Date | null {
  if (!hms) return null
  const [h, m, s] = hms.slice(0, 8).split(':').map(Number)
  const d = new Date()
  d.setHours(h ?? 0, m ?? 0, s ?? 0, 0)
  return d
}

/**
 * Convert a Date back to "HH:mm:ss" using local-time getters.
 */
function dateToHms(date: Date | null): string | null {
  if (!date) return null
  const hh = String(date.getHours()).padStart(2, '0')
  const mm = String(date.getMinutes()).padStart(2, '0')
  const ss = String(date.getSeconds()).padStart(2, '0')
  return `${hh}:${mm}:${ss}`
}

export function BestTimeBar({start, end, onChange}: {
  start: string | null
  end: string | null
  onChange: (start: string | null, end: string | null) => void
}) {
  const handleStartChange = (e: TimePickerChangeEvent) => {
    onChange(dateToHms(e.value), end)
  }

  const handleEndChange = (e: TimePickerChangeEvent) => {
    onChange(start, dateToHms(e.value))
  }

  return (
    <section className="se-sec se-best">
      <div className="se-sec-head">
        <span className="se-ico">🕐</span>ช่วงเวลาที่ดีที่สุด
        <span className="se-pill">ใส่เอง</span>
      </div>
      <p className="se-sub">
        ยังไม่มีข้อมูลความหนาแน่นของคนจาก Places API — กำหนดช่วงที่อยากมาเองได้
      </p>
      <div className="se-time-grid">
        <div className="se-time-card">
          <span className="se-time-lab">เริ่ม</span>
          <TimePicker
            value={hmsToDate(start)}
            onChange={handleStartChange}
            format="HH:mm"
            step={15}
            placeholder="--:--"
          />
        </div>
        <span className="se-time-dash">–</span>
        <div className="se-time-card">
          <span className="se-time-lab">สิ้นสุด</span>
          <TimePicker
            value={hmsToDate(end)}
            onChange={handleEndChange}
            format="HH:mm"
            step={15}
            placeholder="--:--"
          />
        </div>
      </div>
    </section>
  )
}
