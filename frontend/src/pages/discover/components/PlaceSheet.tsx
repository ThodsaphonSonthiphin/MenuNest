import {useState} from 'react'
import {useNavigate} from 'react-router-dom'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {buildStopNavUrl} from '../../trips/lib/navUrl'
import {ReviewIcon} from '../../trips/components/ReviewIcon'
import {reviewLabel, reviewHost} from '../../trips/lib/reviewLinks'
import {DiscoverHourly} from './DiscoverHourly'
import {catColor} from '../lib/categoryStyle'
import {CategoryIcon, CheckIcon, CloseIcon, NavArrowIcon, OpenIcon, PlusIcon, SunIcon, TripIcon} from './DiscoverIcons'

function distanceLabel(km: number | null): string {
  if (km == null) return ''
  return km < 1 ? `${Math.round(km * 1000)} ม. จากคุณ` : `${km.toFixed(1)} กม. จากคุณ`
}

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

  // Mock frame 2's `.d-addr` reads "<address> · 400 ม. จากคุณ" — the distance is
  // what makes the address actionable, and it was being dropped.
  const subtitle = [place.address, distanceLabel(place.distanceKm)].filter(Boolean).join(' · ')

  return (
    <div className="disc-detail">
      <div className="disc-grip" />
      <div className="disc-detail-head">
        <span className="disc-d-cat" style={{background: catColor(place.category)}}>
          <CategoryIcon category={place.category} />
        </span>
        <div className="disc-detail-title">
          <div className="disc-detail-name">{place.name}</div>
          {subtitle && <div className="disc-detail-addr">{subtitle}</div>}
        </div>
        <button type="button" className="disc-detail-close" onClick={onClose} aria-label="ปิด">
          <CloseIcon />
        </button>
      </div>
      <div className="disc-detail-badges">
        {place.openNow === true && <span className="disc-badge open"><CheckIcon />เปิดอยู่</span>}
        {place.seasonStatus === 'good' && <span className="disc-badge season"><SunIcon />เดือนนี้ควรไป</span>}
        {place.seasonStatus === 'bad' && <span className="disc-badge closed"><SunIcon />เดือนนี้ควรเลี่ยง</span>}
        {place.trips.map((t) => <span key={t.tripId} className="disc-badge trip"><TripIcon />{t.tripName}</span>)}
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
      {/* Every action carries its glyph, per `.abtn svg` in the mock. The mock's
          fourth action, "ดูรายละเอียด", is deliberately not rendered: this sheet IS
          the place detail — there is no deeper screen to route to, and a button
          that goes nowhere is worse than an absent one. */}
      <div className="disc-actions">
        {navUrl && (
          <a className="disc-abtn primary" href={navUrl} target="_blank" rel="noopener noreferrer">
            <NavArrowIcon />นำทางด้วย Google Maps
          </a>
        )}
        <div className="disc-arow">
          <button type="button" className="disc-abtn ghost" onClick={openTrip} disabled={place.trips.length === 0}>
            <TripIcon />{place.trips.length > 1 ? `เปิดทริป (${place.trips.length})` : 'เปิดทริป'}
          </button>
          <button type="button" className="disc-abtn ghost" onClick={() => onAddToTrip(place)}>
            <PlusIcon />เพิ่มเข้าทริป
          </button>
        </div>
        {choosing && place.trips.length > 1 && (
          <div className="disc-trip-choose">
            {place.trips.map((t) => (
              <button key={t.tripId} type="button" className="disc-abtn ghost" onClick={() => navigate(`/trips/${t.tripId}`)}>
                <OpenIcon />{t.tripName}
              </button>
            ))}
          </div>
        )}
        <div className="disc-arow">
          <button type="button" className="disc-abtn ghost" disabled={creatingTrip} onClick={() => onCreateTrip(place)}>
            <PlusIcon />สร้างทริปใหม่
          </button>
        </div>
      </div>
    </div>
  )
}
