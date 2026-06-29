// frontend/src/pages/trips/components/DwellStepper.tsx
import {Button, Color, Variant} from '@syncfusion/react-buttons'

const CHIPS = [30, 60, 90, 120]

export function DwellStepper({value, onChange}: {value: number; onChange: (v: number) => void}) {
  return (
    <div className="dwell-stepper">
      <div className="dwell-row">
        <Button onClick={() => onChange(Math.max(15, value - 15))} aria-label="ลด">−</Button>
        <div className="dwell-value">{value} <span>นาที</span></div>
        <Button color={Color.Primary} onClick={() => onChange(value + 15)} aria-label="เพิ่ม">+</Button>
      </div>
      <div className="dwell-chips">
        {CHIPS.map(c => (
          <Button
            key={c}
            className={`chip${c === value ? ' active' : ''}`}
            color={c === value ? Color.Primary : undefined}
            variant={c === value ? Variant.Filled : Variant.Outlined}
            onClick={() => onChange(c)}
          >
            {c}
          </Button>
        ))}
      </div>
    </div>
  )
}
