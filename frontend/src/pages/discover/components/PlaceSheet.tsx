import {useNavigate} from 'react-router-dom'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {buildStopNavUrl} from '../../trips/lib/navUrl'

interface Props {
  place: DiscoverPlaceView
  onClose: () => void
  onAddToTrip: (place: DiscoverPlaceView) => void
  onCreateTrip: (place: DiscoverPlaceView) => void
  creatingTrip?: boolean
}

export function PlaceSheet({place, onClose, onAddToTrip, onCreateTrip, creatingTrip}: Props) {
  const navigate = useNavigate()
  const navUrl = buildStopNavUrl({lat: place.lat, lng: place.lng, googlePlaceId: place.googlePlaceId}, 'Drive')

  const openTrip = () => {
    if (place.trips.length === 1) navigate(`/trips/${place.trips[0].tripId}`)
    // >1 trip: the caller renders a small chooser; here we no-op unless single.
  }

  return (
    <div className="disc-detail">
      <div className="disc-grip" />
      <div className="disc-detail-head">
        <div className="disc-detail-title">
          <div className="disc-detail-name">{place.name}</div>
          {place.address && <div className="disc-detail-addr">{place.address}</div>}
        </div>
        <button type="button" className="disc-detail-close" onClick={onClose} aria-label="ปิด">✕</button>
      </div>
      <div className="disc-detail-badges">
        {place.openNow === true && <span className="disc-badge open">เปิดอยู่</span>}
        {place.seasonStatus === 'good' && <span className="disc-badge season">เดือนนี้ควรไป</span>}
        {place.seasonStatus === 'bad' && <span className="disc-badge closed">เดือนนี้ควรเลี่ยง</span>}
        {place.trips.map((t) => <span key={t.tripId} className="disc-badge trip">{t.tripName}</span>)}
      </div>
      <div className="disc-actions">
        {navUrl && <a className="disc-abtn primary" href={navUrl} target="_blank" rel="noopener noreferrer">นำทางด้วย Google Maps</a>}
        <div className="disc-arow">
          <button type="button" className="disc-abtn ghost" onClick={openTrip} disabled={place.trips.length !== 1}>เปิดทริป</button>
          <button type="button" className="disc-abtn ghost" onClick={() => onAddToTrip(place)}>เพิ่มเข้าทริป</button>
        </div>
        <div className="disc-arow">
          <button type="button" className="disc-abtn ghost" disabled={creatingTrip} onClick={() => onCreateTrip(place)}>สร้างทริปใหม่</button>
        </div>
      </div>
    </div>
  )
}
