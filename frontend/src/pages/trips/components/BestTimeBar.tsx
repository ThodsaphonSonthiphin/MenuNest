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
    <div className="best-time-bar">
      <label>ช่วงเวลาที่ดีที่สุด (ใส่เอง)</label>
      <div className="best-time-row">
        <TimePicker
          value={hmsToDate(start)}
          onChange={handleStartChange}
          format="HH:mm"
          step={15}
          placeholder="เริ่ม"
        />
        <span>–</span>
        <TimePicker
          value={hmsToDate(end)}
          onChange={handleEndChange}
          format="HH:mm"
          step={15}
          placeholder="สิ้นสุด"
        />
      </div>
      <p className="best-time-hint">crowd-by-hour (popular times) ไม่มีใน Places API — v1 กรอกช่วงเองตามนี้</p>
    </div>
  )
}
