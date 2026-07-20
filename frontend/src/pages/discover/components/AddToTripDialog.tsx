import {useListTripsQuery, useAddTripPlaceMutation} from '../../../shared/api/api'
import type {DiscoverPlaceView} from '../lib/discoverFilter'

interface Props {
  place: DiscoverPlaceView
  onClose: () => void
  onDone: (tripId: string) => void
}

export function AddToTripDialog({place, onClose, onDone}: Props) {
  const {data: trips = []} = useListTripsQuery()
  const [addTripPlace, {isLoading}] = useAddTripPlaceMutation()

  const add = async (tripId: string) => {
    await addTripPlace({
      tripId,
      googlePlaceId: place.googlePlaceId,
      name: place.name,
      lat: place.lat,
      lng: place.lng,
      address: place.address,
      category: place.category,
      priceLevel: place.priceLevel,
      photoUrl: place.photoUrl,
      openingHoursJson: place.openingHoursJson,
      reviewLinks: [],
      checklist: [],
    }).unwrap()
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
