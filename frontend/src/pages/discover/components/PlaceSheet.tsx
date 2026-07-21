import {useState} from 'react'
import {useNavigate} from 'react-router-dom'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {buildStopNavUrl} from '../../trips/lib/navUrl'
import {ReviewIcon} from '../../trips/components/ReviewIcon'
import {reviewLabel, reviewHost} from '../../trips/lib/reviewLinks'
import {DiscoverHourly} from './DiscoverHourly'

interface Props {
  place: DiscoverPlaceView
  onClose: () => void
  onAddToTrip: (place: DiscoverPlaceView) => void
  onCreateTrip: (place: DiscoverPlaceView) => void
  creatingTrip?: boolean
}

export function PlaceSheet({place, onClose, onAddToTrip, onCreateTrip, creatingTrip}: Props) {
  const navigate = useNavigate()
  const [choosing, setChoosing] = useState(false)
  const navUrl = buildStopNavUrl({lat: place.lat, lng: place.lng, googlePlaceId: place.googlePlaceId}, 'Drive')

  // 1 trip → open directly. >1 (a place deduped across trips, ADR-100) → toggle an
  // inline chooser so the user picks which trip. 0 shouldn't happen (a discovered
  // place always comes from a TripPlace) but the button is guarded regardless.
  const openTrip = () => {
    if (place.trips.length === 1) navigate(`/trips/${place.trips[0].tripId}`)
    else if (place.trips.length > 1) setChoosing((v) => !v)
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
      <DiscoverHourly place={place} />
      {place.reviewLinks.length > 0 && (
        <div className="disc-reviews">
          <div className="disc-sec-lab">รีวิว</div>
          {place.reviewLinks.map((l, i) => (
            <a key={l.url + i} className="disc-review" href={l.url} target="_blank" rel="noopener noreferrer">
              <ReviewIcon />
              <span className="disc-review-label">{reviewLabel(l, i)}</span>
              <span className="disc-review-host">{reviewHost(l.url)}</span>
            </a>
          ))}
        </div>
      )}
      {place.notes && (
        <div className="disc-note">
          <div className="disc-sec-lab">โน้ต</div>
          <p className="disc-note-body">{place.notes}</p>
        </div>
      )}
      <div className="disc-actions">
        {navUrl && <a className="disc-abtn primary" href={navUrl} target="_blank" rel="noopener noreferrer">นำทางด้วย Google Maps</a>}
        <div className="disc-arow">
          <button type="button" className="disc-abtn ghost" onClick={openTrip} disabled={place.trips.length === 0}>
            {place.trips.length > 1 ? `เปิดทริป (${place.trips.length})` : 'เปิดทริป'}
          </button>
          <button type="button" className="disc-abtn ghost" onClick={() => onAddToTrip(place)}>เพิ่มเข้าทริป</button>
        </div>
        {choosing && place.trips.length > 1 && (
          <div className="disc-trip-choose">
            {place.trips.map((t) => (
              <button key={t.tripId} type="button" className="disc-abtn ghost" onClick={() => navigate(`/trips/${t.tripId}`)}>{t.tripName}</button>
            ))}
          </div>
        )}
        <div className="disc-arow">
          <button type="button" className="disc-abtn ghost" disabled={creatingTrip} onClick={() => onCreateTrip(place)}>สร้างทริปใหม่</button>
        </div>
      </div>
    </div>
  )
}
