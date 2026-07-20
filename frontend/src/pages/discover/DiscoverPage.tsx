import {useCallback, useEffect, useMemo, useState} from 'react'
import {useNavigate} from 'react-router-dom'
import './DiscoverPage.css'
import '../trips/trips-tokens.css'
import {useListMyPlacesQuery, useAddTripPlaceMutation, useCreateTripMutation} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store'
import {setAnchor, setScope, setCategoryFilter, toggleSignal, setSelectedKey} from './discoverSlice'
import {applyDiscover, type DiscoverPlaceView} from './lib/discoverFilter'
import {DiscoverMap} from './components/DiscoverMap'
import {FilterBar} from './components/FilterBar'
import {PlaceBottomSheet} from './components/PlaceBottomSheet'
import {PlaceSheet} from './components/PlaceSheet'
import {AddToTripDialog} from './components/AddToTripDialog'

export function DiscoverPage() {
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const {data: places = [], isLoading} = useListMyPlacesQuery()
  const {anchor, scope, categoryFilter, toggles, selectedKey} = useAppSelector((s) => s.discover)
  const [addForPlace, setAddForPlace] = useState<DiscoverPlaceView | null>(null)
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
  const selected = views.find((v) => v.key === selectedKey) ?? null

  // Memoized so DiscoverMap's marker-building effect (keyed on this callback
  // identity) doesn't rebuild its markers/clusterer on every parent render.
  const onMapSelect = useCallback((k: string) => dispatch(setSelectedKey(k)), [dispatch])
  const onMapScopeChange = useCallback((b: {north: number; south: number; east: number; west: number}) => dispatch(setScope(b)), [dispatch])

  // ADR-098: creating a Trip from a discovered Place seeds it as the Trip's first
  // TripPlace (not just an empty Trip) — reuse the same addTripPlace payload shape
  // as AddToTripDialog so both paths stay in sync.
  const handleCreateTrip = async (place: DiscoverPlaceView) => {
    const trip = await createTrip({
      name: place.name,
      startDate: new Date().toISOString().slice(0, 10),
      dayCount: 1,
      defaultTravelMode: 'Drive',
    }).unwrap()
    await addTripPlace({
      tripId: trip.id,
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
    navigate(`/trips/${trip.id}`)
  }

  return (
    <div className="discover-page">
      <div className="disc-topbar">
        <div className="disc-title-row"><span className="disc-title">ไปไหนดี</span></div>
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
      />

      {selected ? (
        <PlaceSheet
          place={selected}
          onClose={() => dispatch(setSelectedKey(null))}
          onAddToTrip={(p) => setAddForPlace(p)}
          onCreateTrip={handleCreateTrip}
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
