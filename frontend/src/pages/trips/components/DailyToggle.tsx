// frontend/src/pages/trips/components/DailyToggle.tsx
import {useSetTripDailyMutation, type TripDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {RepeatIcon} from './TripFormIcons'

/**
 * The trip-level "โหมดประจำวัน" switch (issue #49). Commits immediately via
 * setTripDaily. When the trip has more than one day it cannot be enabled; the
 * switch is kept clickable (not `disabled`) so touch users — who have no hover
 * for the title — still get the reason in the shared error line on tap.
 */
export function DailyToggle({trip, onError}: {trip: TripDto; onError: (msg: string | null) => void}) {
  const [setDaily, {isLoading}] = useSetTripDailyMutation()
  const canEnable = trip.dayCount === 1
  const blocked = !trip.isDaily && !canEnable
  const blockedMsg = 'ทริปประจำวันต้องเป็นวันเดียว — ลบวันอื่นก่อนถึงจะเปิดได้'

  const toggle = async () => {
    onError(null)
    if (blocked) {
      onError(blockedMsg)
      return
    }
    try {
      await setDaily({id: trip.id, isDaily: !trip.isDaily}).unwrap()
    } catch (e) {
      onError(getErrorMessage(e))
    }
  }

  return (
    <button
      type="button"
      className={`daily-toggle${trip.isDaily ? ' on' : ''}${blocked ? ' blocked' : ''}`}
      role="switch"
      aria-checked={trip.isDaily}
      aria-disabled={blocked}
      aria-label="โหมดประจำวัน"
      disabled={isLoading}
      title={blocked ? blockedMsg : undefined}
      onClick={toggle}
    >
      <RepeatIcon className="daily-toggle-ic" />
      <span>ประจำวัน</span>
      <span className="daily-toggle-track"><span className="daily-toggle-knob" /></span>
    </button>
  )
}
