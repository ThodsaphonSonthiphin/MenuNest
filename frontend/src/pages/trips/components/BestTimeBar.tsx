// frontend/src/pages/trips/components/BestTimeBar.tsx
import {TimePicker} from '@syncfusion/react-calendars'
import type {TimePickerChangeEvent} from '@syncfusion/react-calendars'
import {hmsToDate, dateToHms} from '../utils/time'

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
