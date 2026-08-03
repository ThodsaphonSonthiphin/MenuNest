// frontend/src/pages/trips/components/EditTripDialog.tsx
import {useMemo, useState, type ReactNode} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox} from '@syncfusion/react-inputs'
import {DatePicker} from '@syncfusion/react-calendars'
import type {DatePickerChangeEvent} from '@syncfusion/react-calendars'
import {useUpdateTripMutation, type TravelMode, type TripDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {draftFromTrip, isDraftDirty, normalizeDraft, type TripEditDraft} from '../lib/tripEdit'
import {dateToYmd, endDate, thaiDate, ymdToDate} from '../utils/date'
import {
  AlertIcon,
  ArrowRightIcon,
  CarIcon,
  CheckIcon,
  MapPinIcon,
  MinusIcon,
  PencilIcon,
  PlusIcon,
  TransitIcon,
  WalkIcon,
} from './TripFormIcons'

// Same three values and labels as CreateTripDialog — the backend TravelMode enum.
const MODES: {label: string; value: TravelMode; icon: ReactNode}[] = [
  {label: 'รถยนต์', value: 'Drive', icon: <CarIcon />},
  {label: 'ขนส่งสาธารณะ', value: 'Transit', icon: <TransitIcon />},
  {label: 'เดิน', value: 'Walk', icon: <WalkIcon />},
]

const MIN_DAYS = 1
const MAX_DAYS = 60

/**
 * Edit an existing trip's five fields (issue #50, ADR-141).
 *
 * A dedicated SIBLING of CreateTripDialog, not a mode on it: create and edit diverge in
 * title, submit label, defaults, mutation, success behaviour, the day-count guard — and
 * edit drops the isDaily switch entirely, because ADR-137 forbids IsDaily on the
 * full-replace UpdateTrip and DailyToggle stays on the header. The two share the
 * `.create-trip-dialog` CSS class so they cannot drift visually; the Syncfusion Dialog is
 * portaled to document.body and cannot see the page-scoped .trip-detail tokens, so each
 * dialog family declares its own palette there.
 *
 * Every field is STAGED behind an explicit save (ADR-138 requires it for day count, and a
 * form where one field behaves differently from its neighbours is worse than either
 * consistent option). The save is dirty-diffed, errors stay local with the dialog open,
 * and cancel closes with no warning — what is lost is typed text, not data.
 */
export function EditTripDialog({trip, onClose}: {trip: TripDto; onClose: () => void}) {
  const [draft, setDraft] = useState<TripEditDraft>(() => draftFromTrip(trip))
  const [nameError, setNameError] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [updateTrip, {isLoading}] = useUpdateTripMutation()

  const set = <K extends keyof TripEditDraft>(k: K, v: TripEditDraft[K]) =>
    setDraft((d) => ({...d, [k]: v}))

  // Live end-date summary — most useful precisely when changing the day count.
  const endLabel = useMemo(() => {
    const e = endDate(ymdToDate(draft.startDate), draft.dayCount)
    return e ? thaiDate(e) : null
  }, [draft.startDate, draft.dayCount])

  const save = async () => {
    setSaveError(null)
    const d = normalizeDraft(draft)
    if (!d.name) {
      setNameError('กรุณากรอกชื่อทริป')
      return
    }
    setNameError(null)
    // Dirty-diff (ADR-141): an unchanged save issues NO PUT. updateTrip invalidates
    // TripItinerary on every call, and that refetch re-bills Google Routes + Weather.
    if (!isDraftDirty(d, trip)) {
      onClose()
      return
    }
    try {
      await updateTrip({
        id: trip.id,
        name: d.name,
        destination: d.destination || null,
        startDate: d.startDate,
        dayCount: d.dayCount,
        defaultTravelMode: d.defaultTravelMode,
      }).unwrap()
      onClose()
    } catch (e) {
      // The dialog STAYS OPEN on failure and shows the message inside itself. Backend
      // messages are English and are rendered verbatim (ADR-145).
      setSaveError(getErrorMessage(e))
    }
  }

  const header = (
    <div className="ctd-head">
      <span className="ctd-head-badge">
        <PencilIcon />
      </span>
      <div className="ctd-head-text">
        <span className="ctd-head-title">แก้ไขทริป</span>
        <span className="ctd-head-sub">เปลี่ยนรายละเอียดของทริปนี้</span>
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
      <form
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
        noValidate
        className="ctd-form"
      >
        {/* Trip name */}
        <div className="ctd-field">
          <label className="ctd-label">
            ชื่อทริป <span className="ctd-req">*</span>
          </label>
          <TextBox
            value={draft.name}
            placeholder="เช่น เชียงใหม่ 3 วัน"
            onChange={(e) => set('name', e.value ?? '')}
          />
          {nameError && <p className="ctd-error">{nameError}</p>}
        </div>

        {/* Destination — pin lead icon */}
        <div className="ctd-field">
          <label className="ctd-label">ปลายทาง</label>
          <div className="ctd-pin">
            <span className="ctd-pin-ico">
              <MapPinIcon />
            </span>
            <TextBox
              value={draft.destination}
              placeholder="Chiang Mai"
              onChange={(e) => set('destination', e.value ?? '')}
            />
          </div>
        </div>

        {/* Start date + day count — two columns. No daily switch: ADR-137/141. */}
        <div className="ctd-row2">
          <div className="ctd-field">
            <label className="ctd-label">
              วันเริ่ม <span className="ctd-req">*</span>
            </label>
            <DatePicker
              value={ymdToDate(draft.startDate)}
              format="dd MMM yyyy"
              onChange={(e: DatePickerChangeEvent) => {
                const v = dateToYmd(e.value)
                if (v) set('startDate', v)
              }}
            />
          </div>

          <div className="ctd-field">
            <label className="ctd-label">
              จำนวนวัน <span className="ctd-req">*</span>
            </label>
            <div className="ctd-stepper">
              <button
                type="button"
                className="ctd-step"
                aria-label="ลดจำนวนวัน"
                disabled={draft.dayCount <= MIN_DAYS}
                onClick={() => set('dayCount', Math.max(MIN_DAYS, draft.dayCount - 1))}
              >
                <MinusIcon />
              </button>
              <span className="ctd-step-val" aria-live="polite">
                {draft.dayCount}
              </span>
              <button
                type="button"
                className="ctd-step"
                aria-label="เพิ่มจำนวนวัน"
                disabled={draft.dayCount >= MAX_DAYS}
                onClick={() => set('dayCount', Math.min(MAX_DAYS, draft.dayCount + 1))}
              >
                <PlusIcon />
              </button>
            </div>
          </div>
        </div>

        {/* Live end-date summary */}
        {endLabel && (
          <div className="ctd-summary">
            <span className="ctd-summary-ico">
              <ArrowRightIcon />
            </span>
            <span>
              สิ้นสุด <b>{endLabel}</b> · รวม <b>{draft.dayCount} วัน</b>
            </span>
          </div>
        )}

        {/* Primary travel mode — tiles */}
        <div className="ctd-field">
          <label className="ctd-label">การเดินทางหลัก</label>
          <div className="ctd-modes" role="radiogroup" aria-label="การเดินทางหลัก">
            {MODES.map((m) => (
              <button
                type="button"
                key={m.value}
                role="radio"
                aria-checked={draft.defaultTravelMode === m.value}
                className={`ctd-mode${draft.defaultTravelMode === m.value ? ' active' : ''}`}
                onClick={() => set('defaultTravelMode', m.value)}
              >
                <span className="ctd-mode-ico">{m.icon}</span>
                <span className="ctd-mode-lab">{m.label}</span>
              </button>
            ))}
          </div>
        </div>

        {saveError && (
          <div className="ctd-errbox">
            <AlertIcon />
            <span>{saveError}</span>
          </div>
        )}

        <div className="ctd-actions">
          <button type="button" className="ctd-btn ctd-btn-ghost" onClick={onClose}>
            ยกเลิก
          </button>
          <button type="submit" className="ctd-btn ctd-btn-primary" disabled={isLoading}>
            {isLoading ? (
              '…'
            ) : (
              <>
                <CheckIcon /> บันทึก
              </>
            )}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
