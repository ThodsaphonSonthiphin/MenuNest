// frontend/src/pages/trips/components/CreateTripDialog.tsx
import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox, NumericTextBox} from '@syncfusion/react-inputs'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {DatePicker} from '@syncfusion/react-calendars'
import type {DatePickerChangeEvent} from '@syncfusion/react-calendars'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {useCreateTripMutation, type TravelMode} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

interface FormValues {
  name: string
  destination: string
  startDate: string        // stored as "yyyy-MM-dd" string for the API
  dayCount: number
  defaultTravelMode: TravelMode
}

const MODES: {label: string; value: TravelMode}[] = [
  {label: 'รถยนต์', value: 'Drive'},
  {label: 'เดิน', value: 'Walk'},
  {label: 'ขนส่งสาธารณะ', value: 'Transit'},
]

/** Convert a "yyyy-MM-dd" string to a Date object (noon UTC to avoid TZ shift). */
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
    },
  })
  const [createTrip, {isLoading}] = useCreateTripMutation()
  const [serverError, setServerError] = useState<string | null>(null)

  const submit = handleSubmit(async (v) => {
    setServerError(null)
    try {
      const t = await createTrip({
        name: v.name.trim(),
        destination: v.destination.trim() || null,
        startDate: v.startDate,
        dayCount: v.dayCount,
        defaultTravelMode: v.defaultTravelMode,
      }).unwrap()
      onCreated(t.id)
    } catch (e) {
      setServerError(getErrorMessage(e))
    }
  })

  return (
    <Dialog
      open
      onClose={onClose}
      modal
      header="สร้างทริปใหม่"
      style={{width: '440px'}}
    >
      <form onSubmit={submit} noValidate className="trip-form">

        <div className="trip-form-field">
          <label className="trip-form-label">
            ชื่อทริป <span className="trip-field-required">*</span>
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
                  <p className="trips-field-error">{fieldState.error.message}</p>
                )}
              </>
            )}
          />
        </div>

        <div className="trip-form-field">
          <label className="trip-form-label">ปลายทาง</label>
          <Controller
            control={control}
            name="destination"
            render={({field}) => (
              <TextBox
                value={field.value}
                placeholder="Chiang Mai"
                onChange={e => field.onChange(e.value ?? '')}
              />
            )}
          />
        </div>

        <div className="trip-form-field">
          <label className="trip-form-label">
            วันเริ่ม <span className="trip-field-required">*</span>
          </label>
          <Controller
            control={control}
            name="startDate"
            rules={{required: 'เลือกวันเริ่ม'}}
            render={({field, fieldState}) => (
              <>
                {/* DatePicker.value is Date|null; onChange fires DatePickerChangeEvent with value: Date|null */}
                <DatePicker
                  value={strToDate(field.value)}
                  format="yyyy-MM-dd"
                  onChange={(e: DatePickerChangeEvent) =>
                    field.onChange(dateToStr(e.value))
                  }
                />
                {fieldState.error && (
                  <p className="trips-field-error">{fieldState.error.message}</p>
                )}
              </>
            )}
          />
        </div>

        <div className="trip-form-field">
          <label className="trip-form-label">
            จำนวนวัน <span className="trip-field-required">*</span>
          </label>
          <Controller
            control={control}
            name="dayCount"
            rules={{required: 'กรุณากรอกจำนวนวัน', min: {value: 1, message: 'ต้องมากกว่า 0'}, max: {value: 60, message: 'ไม่เกิน 60 วัน'}}}
            render={({field, fieldState}) => (
              <>
                <NumericTextBox
                  value={field.value}
                  min={1}
                  max={60}
                  onChange={e => field.onChange((e.value as number | null) ?? 1)}
                />
                {fieldState.error && (
                  <p className="trips-field-error">{fieldState.error.message}</p>
                )}
              </>
            )}
          />
        </div>

        <div className="trip-form-field">
          <label className="trip-form-label">การเดินทางหลัก</label>
          <Controller
            control={control}
            name="defaultTravelMode"
            render={({field}) => (
              <DropDownList
                dataSource={MODES}
                fields={{text: 'label', value: 'value'}}
                value={field.value}
                onChange={(e: {value: unknown}) =>
                  field.onChange((e.value as TravelMode) ?? 'Drive')
                }
              />
            )}
          />
        </div>

        {serverError && <p className="trips-field-error">{serverError}</p>}

        <div className="trip-form-actions">
          <Button
            type="button"
            variant={Variant.Outlined}
            color={Color.Secondary}
            onClick={onClose}
          >
            ยกเลิก
          </Button>
          <Button
            type="submit"
            variant={Variant.Filled}
            color={Color.Primary}
            disabled={isLoading}
          >
            {isLoading ? '…' : 'สร้าง'}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}
