import {useCallback, useEffect, useMemo, useState} from 'react'
import {useNavigate} from 'react-router-dom'
import './DiscoverPage.css'
import '../trips/trips-tokens.css'
import {useListMyPlacesQuery, useAddTripPlaceMutation, useCreateTripMutation} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store'
import {setAnchor, setScope, setCategoryFilter, toggleSignal, setSelectedKey} from './discoverSlice'
import {applyDiscover, computePlaceView, type DiscoverPlaceView} from './lib/discoverFilter'
import {addTripPlaceArgsFor} from './lib/originPassthrough'
import {DiscoverMap} from './components/DiscoverMap'
import {FilterBar} from './components/FilterBar'
import {PlaceBottomSheet} from './components/PlaceBottomSheet'
import {PlaceSheet} from './components/PlaceSheet'
import {AddToTripDialog} from './components/AddToTripDialog'
import {LocateIcon, SearchIcon} from './components/DiscoverIcons'

export function DiscoverPage() {
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const {data: places = [], isLoading} = useListMyPlacesQuery()
  const {anchor, scope, categoryFilter, toggles, selectedKey} = useAppSelector((s) => s.discover)
  const [addForPlace, setAddForPlace] = useState<DiscoverPlaceView | null>(null)
  const [creatingTrip, setCreatingTrip] = useState(false)
  // Bumped by the anchor pill / locate FAB; MapCamera re-pans on every change.
  const [recenter, setRecenter] = useState(0)
  const [createTrip] = useCreateTripMutation()
  const [addTripPlace] = useAddTripPlaceMutation()

  // Live location → anchor (ADR-027 pattern). Denied/unsupported → stays null (fit-all).
  useEffect(() => {
    if (!('geolocation' in navigator)) return
    navigator.geolocation.getCurrentPosition(
      (pos) => dispatch(setAnchor({lat: Math.round(pos.coords.latitude * 1e4) / 1e4, lng: Math.round(pos.coords.longitude * 1e4) / 1e4})),
      () => dispatch(setAnchor(null)),
      {timeout: 8000},
    )
  }, [dispatch])

  const views = useMemo(
    () => applyDiscover(places, {anchor, viewport: scope, category: categoryFilter, toggles, now: new Date()}),
    [places, anchor, scope, categoryFilter, toggles],
  )
  // Resolve the selected place from the FULL list (not the viewport-scoped `views`)
  // so panning the map or toggling a filter can't make the open detail sheet vanish.
  const selected = useMemo(() => {
    if (!selectedKey) return null
    const p = places.find((pl) => pl.key === selectedKey)
    return p ? computePlaceView(p, {anchor, viewport: scope, category: categoryFilter, toggles, now: new Date()}) : null
  }, [selectedKey, places, anchor, scope, categoryFilter, toggles])

  // Memoized so DiscoverMap's marker-building effect (keyed on this callback
  // identity) doesn't rebuild its markers/clusterer on every parent render.
  const onMapSelect = useCallback((k: string) => dispatch(setSelectedKey(k)), [dispatch])
  const onMapScopeChange = useCallback((b: {north: number; south: number; east: number; west: number}) => dispatch(setScope(b)), [dispatch])

  // ADR-098: creating a Trip from a discovered Place seeds it as the Trip's first
  // TripPlace (not just an empty Trip) — reuse the same addTripPlace payload shape
  // as AddToTripDialog so both paths stay in sync.
  const handleCreateTrip = async (place: DiscoverPlaceView) => {
    if (creatingTrip) return
    setCreatingTrip(true)
    try {
      const trip = await createTrip({
        name: place.name,
        startDate: new Date().toISOString().slice(0, 10),
        dayCount: 1,
        defaultTravelMode: 'Drive',
      }).unwrap()
      await addTripPlace(addTripPlaceArgsFor(trip.id, place)).unwrap()
      navigate(`/trips/${trip.id}`)
    } finally {
      setCreatingTrip(false)
    }
  }

  return (
    <div className="discover-page">
      <div className="disc-topbar">
        {/* `.searchbar` in the mock: magnifier + title + the scope "anchor" pill.
            The pill states where the ranking is measured from and re-centres the
            map on tap; it never invents a place name, because resolving the camera
            back to a city would need a paid reverse-geocode. */}
        <div className="disc-title-row">
          <SearchIcon className="disc-mag" />
          <span className="disc-title">ไปไหนดี</span>
          <button
            type="button"
            className="disc-anchor"
            disabled={!anchor}
            onClick={() => setRecenter((n) => n + 1)}
            aria-label={anchor ? 'กลับไปตำแหน่งของฉัน' : 'ยังไม่รู้ตำแหน่งของคุณ'}
          >
            <LocateIcon />
            {anchor ? 'ใกล้ฉัน' : 'ทั้งแผนที่'}
          </button>
        </div>
        <FilterBar
          category={categoryFilter}
          toggles={toggles}
          onCategory={(c) => dispatch(setCategoryFilter(c))}
          onToggle={(k) => dispatch(toggleSignal(k))}
        />
      </div>

      <DiscoverMap
        places={views}
        anchor={anchor}
        selectedKey={selectedKey}
        onSelect={onMapSelect}
        onScopeChange={onMapScopeChange}
        recenterNonce={recenter}
      />

      {/* `.fab.locate` — the mock's one map control. Hidden until geolocation has
          actually produced an anchor, so it is never a button that does nothing. */}
      {anchor && (
        <button
          type="button"
          className="disc-fab locate"
          onClick={() => setRecenter((n) => n + 1)}
          aria-label="กลับไปตำแหน่งของฉัน"
        >
          <LocateIcon />
        </button>
      )}

      {selected ? (
        <PlaceSheet
          place={selected}
          onClose={() => dispatch(setSelectedKey(null))}
          onAddToTrip={(p) => setAddForPlace(p)}
          onCreateTrip={handleCreateTrip}
          creatingTrip={creatingTrip}
        />
      ) : (
        <PlaceBottomSheet places={views} onSelect={(k) => dispatch(setSelectedKey(k))} />
      )}

      {addForPlace && (
        <AddToTripDialog
          place={addForPlace}
          onClose={() => setAddForPlace(null)}
          onDone={(tripId) => { setAddForPlace(null); navigate(`/trips/${tripId}`) }}
        />
      )}

      {isLoading && <div className="disc-loading">กำลังโหลด…</div>}
    </div>
  )
}
