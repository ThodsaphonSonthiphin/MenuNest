import {useMemo, useState} from 'react'
import {useListTripsQuery, useAddTripPlaceMutation} from '../../../shared/api/api'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {addTripPlaceArgsFor} from '../lib/originPassthrough'
import {filterTrips, tripSubtitle, TRIP_PICKER_QUERY_ARGS} from '../lib/tripPicker'
import {CloseIcon, SearchIcon, TripIcon} from './DiscoverIcons'

interface Props {
  place: DiscoverPlaceView
  onClose: () => void
  onDone: (tripId: string) => void
}

export function AddToTripDialog({place, onClose, onDone}: Props) {
  // TRIP_PICKER_QUERY_ARGS, not a bare call: the server's own defaults are
  // Take=10 ordered by StartDate ascending, which hid the newest half of the
  // user's trips and made "เพิ่มเข้าทริป" impossible for them (spec R1.5).
  const {data, isLoading: loadingTrips} = useListTripsQuery(TRIP_PICKER_QUERY_ARGS)
  const [addTripPlace, {isLoading}] = useAddTripPlaceMutation()
  const [query, setQuery] = useState('')
  const [error, setError] = useState<string | null>(null)

  const trips = data?.result ?? []
  const total = data?.count ?? 0
  const shown = useMemo(() => filterTrips(trips, query), [trips, query])

  const add = async (tripId: string) => {
    setError(null)
    try {
      await addTripPlace(addTripPlaceArgsFor(tripId, place)).unwrap()
      onDone(tripId)
    } catch {
      // Without this the rejected unwrap() surfaced as an unhandled rejection and
      // the dialog just sat there — indistinguishable from "the button does nothing".
      setError('เพิ่มเข้าทริปไม่สำเร็จ — ลองใหม่อีกครั้ง')
    }
  }

  return (
    <div className="disc-modal" role="dialog" aria-label="เพิ่มเข้าทริป">
      <div className="disc-modal-card">
        <div className="disc-modal-head">
          <div className="disc-modal-title">
            <span className="disc-modal-name">เพิ่ม “{place.name}” เข้าทริป</span>
            <span className="disc-modal-sub">
              {loadingTrips ? 'กำลังโหลดทริป…' : `${trips.length} จาก ${total} ทริป · ล่าสุดก่อน`}
            </span>
          </div>
          <button type="button" className="disc-modal-close" onClick={onClose} aria-label="ปิด">
            <CloseIcon />
          </button>
        </div>

        <div className="disc-trip-search">
          <SearchIcon className="disc-trip-search-icon" />
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="ค้นหาทริป…"
            aria-label="ค้นหาทริป"
          />
        </div>

        {error && <p className="disc-modal-error" role="alert">{error}</p>}

        <ul className="disc-trip-list">
          {trips.length === 0 && !loadingTrips && (
            <li className="disc-empty">ยังไม่มีทริป — ใช้ “สร้างทริปใหม่” แทน</li>
          )}
          {trips.length > 0 && shown.length === 0 && (
            <li className="disc-empty">ไม่พบทริปที่ตรงกับ “{query.trim()}”</li>
          )}
          {shown.map((t) => {
            const sub = tripSubtitle(t)
            return (
              <li key={t.id}>
                <button type="button" className="disc-trip-item" disabled={isLoading} onClick={() => add(t.id)}>
                  <span className="disc-trip-ico"><TripIcon /></span>
                  <span className="disc-trip-text">
                    <span className="disc-trip-name">{t.name}</span>
                    {sub && <span className="disc-trip-sub">{sub}</span>}
                  </span>
                </button>
              </li>
            )
          })}
        </ul>
      </div>
    </div>
  )
}
