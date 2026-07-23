// frontend/src/pages/trips/components/CreateTripDialog.tsx
import {useMemo, useState, type ReactNode} from 'react'
import {Controller, useForm, useWatch} from 'react-hook-form'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox} from '@syncfusion/react-inputs'
import {DatePicker} from '@syncfusion/react-calendars'
import type {DatePickerChangeEvent} from '@syncfusion/react-calendars'
import {useCreateTripMutation, type TravelMode} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {
  ArrowRightIcon,
  CarIcon,
  MapPinIcon,
  MinusIcon,
  PlusIcon,
  RepeatIcon,
  SuitcaseIcon,
  TransitIcon,
  WalkIcon,
} from './TripFormIcons'

interface FormValues {
  name: string
  destination: string
  startDate: string        // stored as "yyyy-MM-dd" string for the API
  dayCount: number
  defaultTravelMode: TravelMode
  isDaily: boolean
}

// The backend TravelMode enum has exactly these three values (Drive/Walk/Transit,
// see the trip-planner design spec → Routes API travelMode). Rendered as tiles.
const MODES: {label: string; value: TravelMode; icon: ReactNode}[] = [
  {label: 'รถยนต์', value: 'Drive', icon: <CarIcon />},
  {label: 'ขนส่งสาธารณะ', value: 'Transit', icon: <TransitIcon />},
  {label: 'เดิน', value: 'Walk', icon: <WalkIcon />},
]

const MIN_DAYS = 1
const MAX_DAYS = 60

/** Convert a "yyyy-MM-dd" string to a Date object (local midnight). */
function strToDate(s: string): Date | null {
  if (!s) return null
  const [y, m, d] = s.split('-').map(Number)
  return new Date(y, m - 1, d)
}

/** Convert a Date to "yyyy-MM-dd" string. */
function dateToStr(d: Date | null): string {
  if (!d) return ''
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/** Thai Buddhist-era long date, e.g. "5 ก.ค. 2569" (matches the rest of the app). */
function thaiDate(d: Date): string {
  return d.toLocaleDateString('th-TH', {day: 'numeric', month: 'short', year: 'numeric'})
}

export function CreateTripDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void
  onCreated: (id: string) => void
}) {
  const today = dateToStr(new Date())
  const {control, handleSubmit} = useForm<FormValues>({
    defaultValues: {
      name: '',
      destination: '',
      startDate: today,
      dayCount: 3,
      defaultTravelMode: 'Drive',
      isDaily: false,
    },
  })
  const [createTrip, {isLoading}] = useCreateTripMutation()
  const [serverError, setServerError] = useState<string | null>(null)

  // Live end-date summary — start + (dayCount − 1) days, inclusive.
  const [startDate, dayCount, isDaily] = useWatch({control, name: ['startDate', 'dayCount', 'isDaily']})
  const endLabel = useMemo(() => {
    const s = strToDate(startDate)
    if (!s || !dayCount || dayCount < 1) return null
    const end = new Date(s)
    end.setDate(end.getDate() + (dayCount - 1))
    return thaiDate(end)
  }, [startDate, dayCount])

  const submit = handleSubmit(async (v) => {
    setServerError(null)
    try {
      const t = await createTrip({
        name: v.name.trim(),
        destination: v.destination.trim() || null,
        startDate: v.startDate,
        dayCount: v.isDaily ? 1 : v.dayCount,
        defaultTravelMode: v.defaultTravelMode,
        isDaily: v.isDaily,
      }).unwrap()
      onCreated(t.id)
    } catch (e) {
      setServerError(getErrorMessage(e))
    }
  })

  const header = (
    <div className="ctd-head">
      <span className="ctd-head-badge">
        <SuitcaseIcon />
      </span>
      <div className="ctd-head-text">
        <span className="ctd-head-title">สร้างทริปใหม่</span>
        <span className="ctd-head-sub">วางแผนการเดินทางครั้งใหม่ของคุณ</span>
      </div>
    </div>
  )

  return (
    <Dialog
      open
      onClose={onClose}
      modal
      className="create-trip-dialog"
      header={header}
      style={{width: 'min(460px, calc(100vw - 24px))'}}
    >
      <form onSubmit={submit} noValidate className="ctd-form">
        {/* Trip name */}
        <div className="ctd-field">
          <label className="ctd-label">
            ชื่อทริป <span className="ctd-req">*</span>
          </label>
          <Controller
            control={control}
            name="name"
            rules={{
              required: 'กรุณากรอกชื่อทริป',
              validate: v => v.trim().length > 0 || 'กรุณากรอกชื่อทริป',
            }}
            render={({field, fieldState}) => (
              <>
                <TextBox
                  value={field.value}
                  placeholder="เช่น เชียงใหม่ 3 วัน"
                  onChange={e => field.onChange(e.value ?? '')}
                />
                {fieldState.error && (
                  <p className="ctd-error">{fieldState.error.message}</p>
                )}
              </>
            )}
          />
        </div>

        {/* Destination — pin lead icon */}
        <div className="ctd-field">
          <label className="ctd-label">ปลายทาง</label>
          <Controller
            control={control}
            name="destination"
            render={({field}) => (
              <div className="ctd-pin">
                <span className="ctd-pin-ico">
                  <MapPinIcon />
                </span>
                <TextBox
                  value={field.value}
                  placeholder="Chiang Mai"
                  onChange={e => field.onChange(e.value ?? '')}
                />
              </div>
            )}
          />
        </div>

        {/* Daily mode */}
        <div className="ctd-field">
          <Controller
            control={control}
            name="isDaily"
            render={({field}) => (
              <button
                type="button"
                className={`ctd-daily${field.value ? ' on' : ''}`}
                role="switch"
                aria-checked={field.value}
                onClick={() => field.onChange(!field.value)}
              >
                <span className="ctd-daily-ic"><RepeatIcon /></span>
                <span className="ctd-daily-txt">
                  <b>ทริปประจำวัน</b>
                  <small>เดินทางเส้นเดิมซ้ำทุกวัน — บังคับ 1 วัน, เริ่มเป็น "วันนี้" อัตโนมัติ</small>
                </span>
                <span className="ctd-daily-track"><span className="ctd-daily-knob" /></span>
              </button>
            )}
          />
        </div>

        {/* Start date + day count — two columns */}
        <div className="ctd-row2">
          <div className="ctd-field">
            <label className="ctd-label">
              วันเริ่ม <span className="ctd-req">*</span>
            </label>
            <Controller
              control={control}
              name="startDate"
              rules={{required: 'เลือกวันเริ่ม'}}
              render={({field, fieldState}) => (
                <>
                  <DatePicker
                    value={strToDate(field.value)}
                    format="dd MMM yyyy"
                    onChange={(e: DatePickerChangeEvent) =>
                      field.onChange(dateToStr(e.value))
                    }
                  />
                  {fieldState.error && (
                    <p className="ctd-error">{fieldState.error.message}</p>
                  )}
                </>
              )}
            />
          </div>

          <div className="ctd-field">
            <label className="ctd-label">
              จำนวนวัน <span className="ctd-req">*</span>
            </label>
            <Controller
              control={control}
              name="dayCount"
              rules={{required: true, min: MIN_DAYS, max: MAX_DAYS}}
              render={({field}) => (
                <div className="ctd-stepper">
                  <button
                    type="button"
                    className="ctd-step"
                    aria-label="ลดจำนวนวัน"
                    disabled={isDaily || field.value <= MIN_DAYS}
                    onClick={() => field.onChange(Math.max(MIN_DAYS, field.value - 1))}
                  >
                    <MinusIcon />
                  </button>
                  <span className="ctd-step-val" aria-live="polite">{isDaily ? 1 : field.value}</span>
                  <button
                    type="button"
                    className="ctd-step"
                    aria-label="เพิ่มจำนวนวัน"
                    disabled={isDaily || field.value >= MAX_DAYS}
                    onClick={() => field.onChange(Math.min(MAX_DAYS, field.value + 1))}
                  >
                    <PlusIcon />
                  </button>
                </div>
              )}
            />
          </div>
        </div>

        {/* Live end-date summary */}
        {endLabel && (
          <div className="ctd-summary">
            <span className="ctd-summary-ico">
              <ArrowRightIcon />
            </span>
            <span>
              สิ้นสุด <b>{endLabel}</b> · รวม <b>{dayCount} วัน</b>
            </span>
          </div>
        )}

        {/* Primary travel mode — tiles */}
        <div className="ctd-field">
          <label className="ctd-label">การเดินทางหลัก</label>
          <Controller
            control={control}
            name="defaultTravelMode"
            render={({field}) => (
              <div className="ctd-modes" role="radiogroup" aria-label="การเดินทางหลัก">
                {MODES.map(m => (
                  <button
                    type="button"
                    key={m.value}
                    role="radio"
                    aria-checked={field.value === m.value}
                    className={`ctd-mode${field.value === m.value ? ' active' : ''}`}
                    onClick={() => field.onChange(m.value)}
                  >
                    <span className="ctd-mode-ico">{m.icon}</span>
                    <span className="ctd-mode-lab">{m.label}</span>
                  </button>
                ))}
              </div>
            )}
          />
        </div>

        {serverError && <p className="ctd-error">{serverError}</p>}

        <div className="ctd-actions">
          <button type="button" className="ctd-btn ctd-btn-ghost" onClick={onClose}>
            ยกเลิก
          </button>
          <button type="submit" className="ctd-btn ctd-btn-primary" disabled={isLoading}>
            {isLoading ? (
              '…'
            ) : (
              <>
                <PlusIcon /> สร้างทริป
              </>
            )}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
