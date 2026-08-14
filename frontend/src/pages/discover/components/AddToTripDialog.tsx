import {useEffect, useMemo, useState} from 'react'
import {useListTripsQuery, useAddTripPlaceMutation} from '../../../shared/api/api'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {addTripPlaceArgsFor} from '../lib/originPassthrough'
import {filterTrips, tripSubtitle, tripPickerArgs, TRIP_PICKER_PAGE_SIZE} from '../lib/tripPicker'
import {CloseIcon, SearchIcon, TripIcon} from './DiscoverIcons'

interface Props {
  place: DiscoverPlaceView
  onClose: () => void
  onDone: (tripId: string) => void
}

export function AddToTripDialog({place, onClose, onDone}: Props) {
  const [query, setQuery] = useState('')
  const [error, setError] = useState<string | null>(null)
  // The typed value drives the list instantly; a debounced copy drives the request,
  // so a long name is one round-trip rather than one per keystroke.
  const [debounced, setDebounced] = useState('')
  useEffect(() => {
    const t = setTimeout(() => setDebounced(query), 250)
    return () => clearTimeout(t)
  }, [query])

  // Explicit args, never a bare call: the server's own defaults are Take=10 ordered
  // by StartDate ascending, which hid the newest half of the user's trips and made
  // "เพิ่มเข้าทริป" impossible for them (spec R1.5). Past TRIP_PICKER_PAGE_SIZE the
  // search term goes to the server too, so trips beyond the first page stay reachable.
  const {data, isLoading: loadingTrips, isFetching} = useListTripsQuery(tripPickerArgs(debounced))
  const [addTripPlace, {isLoading}] = useAddTripPlaceMutation()

  const trips = useMemo(() => data?.result ?? [], [data])
  const total = data?.count ?? 0
  const shown = useMemo(() => filterTrips(trips, query), [trips, query])
  const capped = !query.trim() && total > TRIP_PICKER_PAGE_SIZE

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
              {loadingTrips
                ? 'กำลังโหลดทริป…'
                : capped
                  ? `${trips.length} จาก ${total} ทริปล่าสุด · พิมพ์ค้นหาเพื่อดูที่เหลือ`
                  : `${trips.length} จาก ${total} ทริป · ล่าสุดก่อน`}
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
          {/* "No trips at all" only when nothing is being searched for — with a
              query active an empty result means no match, not an empty library. */}
          {trips.length === 0 && !loadingTrips && query.trim() === '' && (
            <li className="disc-empty">ยังไม่มีทริป — ใช้ “สร้างทริปใหม่” แทน</li>
          )}
          {shown.length === 0 && query.trim() !== '' && !isFetching && (
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
