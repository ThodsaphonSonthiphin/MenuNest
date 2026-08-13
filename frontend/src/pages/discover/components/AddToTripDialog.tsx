import {useListTripsQuery, useAddTripPlaceMutation} from '../../../shared/api/api'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {addTripPlaceArgsFor} from '../lib/originPassthrough'

interface Props {
  place: DiscoverPlaceView
  onClose: () => void
  onDone: (tripId: string) => void
}

export function AddToTripDialog({place, onClose, onDone}: Props) {
  const {data} = useListTripsQuery()
  const trips = data?.result || []
  const [addTripPlace, {isLoading}] = useAddTripPlaceMutation()

  const add = async (tripId: string) => {
    await addTripPlace(addTripPlaceArgsFor(tripId, place)).unwrap()
    onDone(tripId)
  }

  return (
    <div className="disc-modal" role="dialog" aria-label="เพิ่มเข้าทริป">
      <div className="disc-modal-card">
        <div className="disc-modal-head">
          <span>เพิ่ม “{place.name}” เข้าทริป</span>
          <button type="button" onClick={onClose} aria-label="ปิด">✕</button>
        </div>
        <ul className="disc-trip-list">
          {trips.length === 0 && <li className="disc-empty">ยังไม่มีทริป — ใช้ “สร้างทริปใหม่” แทน</li>}
          {trips.map((t) => (
            <li key={t.id}>
              <button type="button" className="disc-trip-item" disabled={isLoading} onClick={() => add(t.id)}>{t.name}</button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}
