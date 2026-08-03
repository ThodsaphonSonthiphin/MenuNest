// frontend/src/pages/trips/components/EditTripDialog.tsx
import {useMemo, useState, type ReactNode} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox} from '@syncfusion/react-inputs'
import {DatePicker} from '@syncfusion/react-calendars'
import type {DatePickerChangeEvent} from '@syncfusion/react-calendars'
import {useNavigate} from 'react-router-dom'
import {useDeleteTripMutation, useUpdateTripMutation, type ItineraryDayDto, type TravelMode, type TripDto, type TripPlaceDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {useConfirm} from '../../../shared/hooks/useConfirm'
import {capNames, draftFromTrip, isDraftDirty, normalizeDraft, shrinkLoss, totalStops, type ShrinkLoss, type TripEditDraft} from '../lib/tripEdit'
import {dateToYmd, endDate, thaiDate, ymdToDate} from '../utils/date'
import {
  AlertIcon,
  ArrowRightIcon,
  CarIcon,
  CheckIcon,
  ClockIcon,
  InfoIcon,
  MapPinIcon,
  MinusIcon,
  PencilIcon,
  PlusIcon,
  TransitIcon,
  TrashIcon,
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

/** "yyyy-MM-dd" -> Thai BE label, falling back to the raw value if it will not parse. */
function th(ymd: string): string {
  const d = ymdToDate(ymd)
  return d ? thaiDate(d) : ymd
}

/**
 * What the confirm says before a Shrink destroys stops (ADR-138): the day range, the stop
 * count, the place NAMES (capped for the 420px dialog), and a distinct tag on any stop
 * already marked มาแล้ว — that is recorded history, and a bare number hides it.
 */
function ShrinkLossMessage({loss}: {loss: ShrinkLoss}) {
  const {shown, moreCount} = capNames(loss.stops, 5)
  const range = loss.dayFrom === loss.dayTo ? `วันที่ ${loss.dayFrom}` : `วันที่ ${loss.dayFrom}–${loss.dayTo}`
  const dates = loss.dateFrom === loss.dateTo ? th(loss.dateFrom) : `${th(loss.dateFrom)} – ${th(loss.dateTo)}`
  return (
    <>
      {range} ({dates}) จะถูกลบ พร้อม <b>จุดแวะ {loss.stops.length} จุด</b> บนวันนั้น
      <div className="trip-confirm-loss">
        จุดแวะที่จะหายไป
        <ul>
          {shown.map((s, i) => (
            <li key={i}>
              {s.name}
              {s.isVisited && <span className="trip-confirm-tag">มาแล้ว</span>}
            </li>
          ))}
          {moreCount > 0 && <li>…และอีก {moreCount} แห่ง</li>}
        </ul>
        <span className="trip-confirm-final">ลบแล้วกู้คืนไม่ได้</span>
      </div>
    </>
  )
}

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
export function EditTripDialog({
  trip,
  days,
  places,
  overrideDate,
  locked,
  onClose,
}: {
  trip: TripDto
  /** The itinerary already in the RTK cache. `[]` means "not loaded" — a trip always has >=1 day. */
  days: ItineraryDayDto[]
  places: TripPlaceDto[]
  /** The server-projected "today" for a daily trip's single current-time-start day (ADR-144). */
  overrideDate?: string
  /**
   * Mirrors TripDetailPage's own `currentDay` predicate: true when this trip's single day has
   * useCurrentTimeAsStart set, REGARDLESS of isDaily — that flag is not exclusive to daily trips
   * (SetDayUseCurrentTimeHandler only refuses turning it off on a daily trip). Without this the
   * header locks the date and shows today while this dialog, one tap away, showed the persisted
   * date and offered to change it — two surfaces disagreeing about the same field.
   */
  locked?: boolean
  onClose: () => void
}) {
  const [draft, setDraft] = useState<TripEditDraft>(() => draftFromTrip(trip))
  const [nameError, setNameError] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [updateTrip, {isLoading}] = useUpdateTripMutation()
  const [deleteTrip, {isLoading: isDeleting}] = useDeleteTripMutation()
  const navigate = useNavigate()
  const {confirm} = useConfirm()

  // ADR-139: the day-count control is live ONLY where the itinerary is already cached, and
  // is DISABLED — with its reason shown — while the count cannot be priced. This covers the
  // in-flight window, the refire when geolocation resolves, and an outright fetch failure.
  // Never default the unknown count to zero: that is the failure mode this whole guard exists
  // to prevent. The other four fields stay editable throughout.
  const daysKnown = days.length > 0

  // ── Daily trips (ADR-144) ────────────────────────────────────────────────────
  // Two fields cannot mean anything here, for DIFFERENT reasons, so each carries its own
  // copy. Both stay VISIBLE and disabled — the dialog has one shape for every trip, and
  // hiding them would delete the only place the constraint is ever explained.
  const isDaily = trip.isDaily
  const todayYmd = dateToYmd(new Date()) ?? draft.startDate
  // ADR-146: a Backdate is refused server-side, so never offer one. Memoised because a fresh
  // Date object every render would churn the picker's prop identity.
  const minDate = useMemo(() => {
    const d = new Date()
    d.setHours(0, 0, 0, 0)
    return d
  }, [])
  // The persisted start date of a daily trip is displayed NOWHERE in the app (dailyCard has
  // no date row, TripDateEditor is always locked, GetItinerary projects the date to today),
  // so it is a fallback, not a value. DISPLAY today — but keep `draft.startDate` on the
  // persisted value so the save never moves it and the dirty-diff never trips on it.
  // ADR-144's fallback also covers the non-daily "current-time-start" single day (`locked`,
  // mirroring TripDetailPage's `currentDay`): its persisted date is likewise shown nowhere in
  // the app while the flag is on, so the same display/draft split applies.
  const dateLocked = isDaily || !!locked
  const displayStartYmd = dateLocked ? (overrideDate?.slice(0, 10) ?? todayYmd) : draft.startDate
  const dayCountDisabled = isDaily || !daysKnown
  const dayCountValue = isDaily ? 1 : draft.dayCount

  const placeNameById = useMemo(
    () => Object.fromEntries(places.map((p) => [p.id, p.name])) as Record<string, string>,
    [places],
  )

  const set = <K extends keyof TripEditDraft>(k: K, v: TripEditDraft[K]) =>
    setDraft((d) => ({...d, [k]: v}))

  // Live end-date summary — most useful precisely when changing the day count.
  // Follows the COERCED values (displayStartYmd, dayCountValue), not the raw draft, or the
  // pill would misrepresent a daily trip — same reason CreateTripDialog coerces its own.
  const endLabel = useMemo(() => {
    const e = endDate(ymdToDate(displayStartYmd), dayCountValue)
    return e ? thaiDate(e) : null
  }, [displayStartYmd, dayCountValue])

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

    // ADR-138: exactly ONE confirm against the NET change, fired on save rather than on each
    // tap of the minus button — 5 -> 3 is one decision, not two. It fires only when the dropped
    // days really hold stops; a shrink over empty days is an ordinary edit. Priced entirely
    // from the cache this dialog was already handed (ADR-139) — nothing is fetched for it.
    let allowStopLoss = false
    const loss = shrinkLoss(days, placeNameById, d.dayCount)
    if (loss) {
      const ok = await confirm({
        title: `ลดจำนวนวันจาก ${days.length} เหลือ ${d.dayCount}?`,
        message: <ShrinkLossMessage loss={loss} />,
        confirmText: 'ลบวันและจุดแวะ',
        destructive: true,
      })
      if (!ok) return
      allowStopLoss = true
    }

    try {
      await updateTrip({
        id: trip.id,
        name: d.name,
        destination: d.destination || null,
        startDate: d.startDate,
        dayCount: d.dayCount,
        defaultTravelMode: d.defaultTravelMode,
        // Only ever true immediately after the user confirmed the loss above (ADR-140).
        allowStopLoss,
      }).unwrap()
      onClose()
    } catch (e) {
      // The dialog STAYS OPEN on failure and shows the message inside itself. Backend
      // messages are English and are rendered verbatim (ADR-145).
      setSaveError(getErrorMessage(e))
    }
  }

  const handleDelete = async () => {
    setSaveError(null)
    // "N วัน · M จุดแวะ" identifies the trip at a glance and is free: ADR-139 already
    // requires this dialog to open where the itinerary is cached. Omitted while unknown
    // rather than guessed — the name alone still identifies it.
    const ok = await confirm({
      title: 'ลบทริปนี้?',
      message: (
        <>
          <b>“{trip.name}”</b>
          {daysKnown && (
            <>
              {' '}
              · {days.length} วัน · {totalStops(days)} จุดแวะ
            </>
          )}
          <div className="trip-confirm-loss">
            {/* DeleteTripHandler is a pure soft delete — the stops are NOT erased, so the copy
                must not say they are. What is true, and unguessable from "ลบทริป", is that
                this trip's places also leave Discover. */}
            สถานที่ในทริปนี้จะหายจาก <b>ไปไหนดี</b> ด้วย
            <span className="trip-confirm-final">ลบแล้วกู้คืนไม่ได้</span>
          </div>
        </>
      ),
      confirmText: 'ลบทริป',
      destructive: true,
    })
    if (!ok) return
    try {
      await deleteTrip(trip.id).unwrap()
      // Leave immediately: staying put hits TripDetailPage's not-found guard, which reads as
      // an error for something the user just asked for. No toast — the app has no shared toast
      // system, and the trip's absence from /trips is the feedback (ADR-143).
      navigate('/trips')
    } catch (e) {
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
              วันเริ่ม {!dateLocked && <span className="ctd-req">*</span>}
            </label>
            <DatePicker
              value={ymdToDate(displayStartYmd)}
              format="dd MMM yyyy"
              disabled={dateLocked}
              minDate={minDate}
              onChange={(e: DatePickerChangeEvent) => {
                const v = dateToYmd(e.value)
                if (v) set('startDate', v)
              }}
            />
            {isDaily && (
              <span className="ctd-why">
                <InfoIcon />
                ทริปประจำวันเริ่ม “วันนี้” เสมอ
              </span>
            )}
            {!isDaily && locked && (
              <span className="ctd-why">
                <InfoIcon />
                วันนี้เริ่มจากเวลาปัจจุบัน
              </span>
            )}
          </div>

          <div className="ctd-field">
            <label className="ctd-label">
              จำนวนวัน {!dayCountDisabled && <span className="ctd-req">*</span>}
            </label>
            <div className={`ctd-stepper${dayCountDisabled ? ' is-disabled' : ''}`}>
              <button
                type="button"
                className="ctd-step"
                aria-label="ลดจำนวนวัน"
                disabled={dayCountDisabled || draft.dayCount <= MIN_DAYS}
                onClick={() => set('dayCount', Math.max(MIN_DAYS, draft.dayCount - 1))}
              >
                <MinusIcon />
              </button>
              <span className="ctd-step-val" aria-live="polite">
                {dayCountValue}
              </span>
              <button
                type="button"
                className="ctd-step"
                aria-label="เพิ่มจำนวนวัน"
                disabled={dayCountDisabled || draft.dayCount >= MAX_DAYS}
                onClick={() => set('dayCount', Math.min(MAX_DAYS, draft.dayCount + 1))}
              >
                <PlusIcon />
              </button>
            </div>
            {isDaily ? (
              <span className="ctd-why">
                <InfoIcon />
                ทริปประจำวันเป็นวันเดียวเสมอ
              </span>
            ) : !daysKnown ? (
              <span className="ctd-why">
                <ClockIcon />
                กำลังโหลดแผนเที่ยว — ยังนับจุดแวะที่จะหายไม่ได้
              </span>
            ) : null}
          </div>
        </div>

        {/* Live end-date summary */}
        {endLabel && (
          <div className="ctd-summary">
            <span className="ctd-summary-ico">
              <ArrowRightIcon />
            </span>
            <span>
              สิ้นสุด <b>{endLabel}</b> · รวม <b>{dayCountValue} วัน</b>
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

        <div className="ctd-actions ctd-actions-split">
          <button
            type="button"
            className="ctd-btn ctd-btn-danger"
            disabled={isLoading || isDeleting}
            onClick={() => void handleDelete()}
          >
            <TrashIcon /> ลบทริป
          </button>
          <div className="ctd-actions-r">
            <button type="button" className="ctd-btn ctd-btn-ghost" onClick={onClose}>
              ยกเลิก
            </button>
            <button type="submit" className="ctd-btn ctd-btn-primary" disabled={isLoading || isDeleting}>
              {isLoading ? (
                '…'
              ) : (
                <>
                  <CheckIcon /> บันทึก
                </>
              )}
            </button>
          </div>
        </div>
      </form>
    </Dialog>
  )
}
