import {useState} from 'react'
import {useNavigate} from 'react-router-dom'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {buildStopNavUrl} from '../../trips/lib/navUrl'
import {ReviewIcon} from '../../trips/components/ReviewIcon'
import {reviewLabel, reviewHost} from '../../trips/lib/reviewLinks'
import {DiscoverHourly} from './DiscoverHourly'
import {catColor} from '../lib/categoryStyle'
import {distanceLabel} from '../lib/placeFormat'
import {useDeleteTripPlaceMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {startDelete, confirmCopy, type DeleteFlow} from '../lib/deleteFlow'
import {
  CalendarIcon,
  CategoryIcon,
  CheckIcon,
  CloseIcon,
  KeepIcon,
  NavArrowIcon,
  OpenIcon,
  PlusIcon,
  SunIcon,
  TrashIcon,
  TripIcon,
} from './DiscoverIcons'

interface Props {
  place: DiscoverPlaceView
  onClose: () => void
  onAddToTrip: (place: DiscoverPlaceView) => void
  onCreateTrip: (place: DiscoverPlaceView) => void
  onDeleted: () => void
  creatingTrip?: boolean
}

export function PlaceSheet({place, onClose, onAddToTrip, onCreateTrip, onDeleted, creatingTrip}: Props) {
  const navigate = useNavigate()
  const [choosing, setChoosing] = useState(false)
  const [flow, setFlow] = useState<DeleteFlow>({stage: 'idle'})
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [deletePlace, {isLoading: deleting}] = useDeleteTripPlaceMutation()
  const navUrl = buildStopNavUrl({lat: place.lat, lng: place.lng, googlePlaceId: place.googlePlaceId}, 'Drive')

  const runDelete = async (trip: {tripId: string; tripPlaceId: string}) => {
    setDeleteError(null)
    try {
      await deletePlace({tripId: trip.tripId, placeId: trip.tripPlaceId, cascade: true}).unwrap()
      setFlow({stage: 'idle'})
      onDeleted()
    } catch (err) {
      setDeleteError(getErrorMessage(err))
    }
  }

  // 1 trip → open directly. >1 (a place deduped across trips, ADR-100) → toggle an
  // inline chooser so the user picks which trip. 0 shouldn't happen (a discovered
  // place always comes from a TripPlace) but the button is guarded regardless.
  const openTrip = () => {
    if (place.trips.length === 1) navigate(`/trips/${place.trips[0].tripId}`)
    else if (place.trips.length > 1) setChoosing((v) => !v)
  }

  // Mock frame 2's `.d-addr` reads "<address> · 400 ม. จากคุณ" — the distance is
  // what makes the address actionable, and it was being dropped.
  const subtitle = [place.address, distanceLabel(place.distanceKm, 'จากคุณ')].filter(Boolean).join(' · ')

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
        {flow.stage === 'idle' && place.trips.length > 0 && (
          <button
            type="button"
            className="disc-abtn danger"
            onClick={() => setFlow(startDelete(place.trips))}
          >
            <TrashIcon />ลบจุดนี้
          </button>
        )}
        {flow.stage === 'choosing' && (
          <div className="disc-del-choose">
            <div className="disc-del-lab">เอาออกจากทริปไหน?</div>
            {place.trips.map((t) => (
              <button
                key={t.tripId}
                type="button"
                className="disc-abtn ghost"
                onClick={() => setFlow({stage: 'confirming', trip: t})}
              >
                <TripIcon />{t.tripName}
              </button>
            ))}
            <button type="button" className="disc-abtn ghost" onClick={() => setFlow({stage: 'idle'})}>
              ยกเลิก
            </button>
          </div>
        )}
        {flow.stage === 'confirming' && (() => {
          const copy = confirmCopy(place.name, flow.trip)
          return (
            <div className="disc-confirm">
              <div className="disc-cf-title">{copy.title}</div>
              {copy.warning && (
                <div className="disc-cf-line warn"><CalendarIcon />{copy.warning}</div>
              )}
              <div className="disc-cf-line keep"><KeepIcon />{copy.keep}</div>
              {deleteError && <p className="trips-field-error">{deleteError}</p>}
              <div className="disc-cf-row">
                <button type="button" className="disc-abtn ghost" disabled={deleting} onClick={() => setFlow({stage: 'idle'})}>
                  ยกเลิก
                </button>
                <button type="button" className="disc-abtn danger-solid" disabled={deleting} onClick={() => runDelete(flow.trip)}>
                  ลบ
                </button>
              </div>
            </div>
          )
        })()}
      </div>
    </div>
  )
}
