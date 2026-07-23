// frontend/src/pages/trips/components/DailyToggle.tsx
import {useSetTripDailyMutation, type TripDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {RepeatIcon} from './TripFormIcons'

/**
 * The trip-level "โหมดประจำวัน" switch (issue #49). Commits immediately via
 * setTripDaily; enabling a multi-day trip is rejected by the backend and the
 * message is surfaced through onError. Disabled (with a hint) when the trip has
 * more than one day, since a daily trip must be single-day.
 */
export function DailyToggle({trip, onError}: {trip: TripDto; onError: (msg: string | null) => void}) {
  const [setDaily, {isLoading}] = useSetTripDailyMutation()
  const canEnable = trip.dayCount === 1
  const disabled = isLoading || (!trip.isDaily && !canEnable)

  const toggle = async () => {
    onError(null)
    try {
      await setDaily({id: trip.id, isDaily: !trip.isDaily}).unwrap()
    } catch (e) {
      onError(getErrorMessage(e))
    }
  }

  return (
    <button
      type="button"
      className={`daily-toggle${trip.isDaily ? ' on' : ''}`}
      role="switch"
      aria-checked={trip.isDaily}
      aria-label="โหมดประจำวัน"
      disabled={disabled}
      title={!trip.isDaily && !canEnable ? 'ทริปประจำวันต้องเป็นวันเดียว' : undefined}
      onClick={toggle}
    >
      <RepeatIcon className="daily-toggle-ic" />
      <span>ประจำวัน</span>
      <span className="daily-toggle-track"><span className="daily-toggle-knob" /></span>
    </button>
  )
}
