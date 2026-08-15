import {useCallback, useEffect, useMemo, useRef, useState} from 'react'
import {useNavigate} from 'react-router-dom'
import './DiscoverPage.css'
import '../trips/trips-tokens.css'
import {useListMyPlacesQuery, useAddTripPlaceMutation, useCreateTripMutation} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store'
import {setAnchor, setScope, setCategoryFilter, toggleSignal, setSelectedKey} from './discoverSlice'
import {applyDiscover, computePlaceView, type DiscoverPlaceView} from './lib/discoverFilter'
import {addTripPlaceArgsFor} from './lib/originPassthrough'
import {addTripPlaceArgsForCapture, newTripNameFor, type CapturePayload} from '../trips/lib/capturePayload'
import type {CaptureTarget} from '../trips/components/AddPlaceMode'
import {rememberedAfterExit, resolveCommitRoute} from './lib/captureCommit'
import {DiscoverMap} from './components/DiscoverMap'
import {FilterBar} from './components/FilterBar'
import {PlaceBottomSheet} from './components/PlaceBottomSheet'
import {PlaceSheet} from './components/PlaceSheet'
import {AddToTripDialog} from './components/AddToTripDialog'
import {CloseIcon, LocateIcon, PlusIcon, SearchIcon} from './components/DiscoverIcons'

export function DiscoverPage() {
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const {data: places = [], isLoading} = useListMyPlacesQuery()
  const {anchor, scope, categoryFilter, toggles, selectedKey} = useAppSelector((s) => s.discover)
  const [addForPlace, setAddForPlace] = useState<DiscoverPlaceView | null>(null)
  const [creatingTrip, setCreatingTrip] = useState(false)
  const [deletedNote, setDeletedNote] = useState(false)
  // Bumped by the anchor pill / locate FAB; MapCamera re-pans on every change.
  const [recenter, setRecenter] = useState(0)
  const [createTrip] = useCreateTripMutation()
  const [addTripPlace] = useAddTripPlaceMutation()

  // ── Capture mode (R8.1-R8.5) ───────────────────────────────────────────────
  const [armed, setArmed] = useState(false)
  // R8.5: the Trip chosen at the previous save in THIS run. Once set, the preview's
  // primary action names it and commits in one tap, so a run of captures picks a
  // Trip once. Deliberately not persisted — it is a property of the run, not of the
  // account, and a stale remembered Trip would silently misfile tomorrow's capture.
  const [remembered, setRemembered] = useState<{id: string; name: string} | null>(null)
  // A capture waiting on the trip picker. `resolve` is AddPlaceMode's continuation:
  // true clears its form and stays armed (R8.4), false leaves the preview untouched
  // so a cancelled pick does not throw away everything the user typed.
  // The picker's payload drives rendering; its continuation is a ref, so settling it
  // is never a side effect inside a state updater (StrictMode double-invokes those).
  const [pickerFor, setPickerFor] = useState<CapturePayload | null>(null)
  const pendingResolve = useRef<((saved: boolean) => void) | null>(null)
  const settlePending = useCallback((saved: boolean) => {
    const resolve = pendingResolve.current
    pendingResolve.current = null
    setPickerFor(null)
    resolve?.(saved)
  }, [])

  // Live location → anchor (ADR-027 pattern). Denied/unsupported → stays null (fit-all).
  useEffect(() => {
    if (!('geolocation' in navigator)) return
    navigator.geolocation.getCurrentPosition(
      (pos) => dispatch(setAnchor({lat: Math.round(pos.coords.latitude * 1e4) / 1e4, lng: Math.round(pos.coords.longitude * 1e4) / 1e4})),
      () => dispatch(setAnchor(null)),
      {timeout: 8000},
    )
  }, [dispatch])

  // Clears the delete toast so it does not stick past its moment.
  useEffect(() => {
    if (!deletedNote) return
    const id = window.setTimeout(() => setDeletedNote(false), 2500)
    return () => window.clearTimeout(id)
  }, [deletedNote])

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

  // ── The two commit actions a Discover capture offers (ADR-155) ─────────────
  // Neither navigates away: a capture run stays on Discover, and the saved place
  // reappears as one more dimmed own-pin, which is the confirmation (R8.4). That
  // refresh is free — addTripPlace invalidates the 'MyPlaces' tag this page reads.
  const commitTo = useCallback(async (tripId: string, tripName: string, payload: CapturePayload) => {
    await addTripPlace(addTripPlaceArgsForCapture(tripId, payload)).unwrap()
    setRemembered({id: tripId, name: tripName})
  }, [addTripPlace])

  const onPickTrip = useCallback(async (payload: CapturePayload, forcePicker = false) => {
    const route = resolveCommitRoute(remembered, forcePicker)
    if (route.route === 'direct') {
      await commitTo(route.trip.id, route.trip.name, payload)
      return true
    }
    // A real question. Hand it to the picker and suspend the commit until it answers
    // — AddPlaceMode is awaiting this promise and will only clear its form on true.
    return new Promise<boolean>((resolve) => {
      pendingResolve.current = resolve
      setPickerFor(payload)
    })
  }, [remembered, commitTo])

  const onCreateTripFromCapture = useCallback(async (payload: CapturePayload) => {
    const trip = await createTrip({
      name: newTripNameFor(payload),
      startDate: new Date().toISOString().slice(0, 10),
      dayCount: 1,
      defaultTravelMode: 'Drive',
    }).unwrap()
    await commitTo(trip.id, trip.name, payload)
    return true
  }, [createTrip, commitTo])

  const captureTarget = useMemo<CaptureTarget>(() => ({
    kind: 'choose',
    onPickTrip,
    onCreateTrip: onCreateTripFromCapture,
    rememberedTripName: remembered?.name ?? null,
  }), [onPickTrip, onCreateTripFromCapture, remembered])

  const exitArmed = useCallback(() => {
    setArmed(false)
    // A run ends with the mode. Keeping the remembered Trip past the exit would
    // silently misfile the next run's first capture into yesterday's Trip.
    setRemembered(rememberedAfterExit())
    // A pick left open when the mode is exited must not leave AddPlaceMode's promise
    // dangling — settle it as "not saved" so nothing downstream waits forever.
    settlePending(false)
  }, [settlePending])

  const arm = useCallback(() => {
    // Arming takes over every tap, so an open detail sheet would be a dead surface.
    dispatch(setSelectedKey(null))
    setArmed(true)
  }, [dispatch])

  return (
    <div className="discover-page">
      {/* R8.3: armed, the banner owns the top of the map, so the browse chrome steps
          aside. Leaving the filter row up would also be a lie — it filters the library,
          and while armed the library is not what the user is acting on. */}
      {!armed && (
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
      )}

      <DiscoverMap
        places={views}
        anchor={anchor}
        selectedKey={selectedKey}
        onSelect={onMapSelect}
        onScopeChange={onMapScopeChange}
        recenterNonce={recenter}
        armed={armed}
        captureTarget={armed ? captureTarget : null}
        onExitArmed={exitArmed}
      />

      {/* The dock owns the bottom anchoring and the height cap; the sheet inside it
          is normal-flow. That is what lets `.fab.locate` sit at `bottom: 100%` —
          exactly above the sheet's REAL height. Positioning the FAB against the
          cap instead left it floating 256px above a short list. */}
      <div className={armed ? 'disc-dock armed' : selected ? 'disc-dock detail' : 'disc-dock list'}>
        {/* One column so the capture FAB sits above the locate FAB without either
            knowing the other's height — and so it still lands correctly when the
            locate FAB is absent (no geolocation anchor yet). */}
        <div className="disc-fabs">
          <button
            type="button"
            className={`disc-fab capture${armed ? ' on' : ''}`}
            onClick={armed ? exitArmed : arm}
            aria-pressed={armed}
            aria-label={armed ? 'ออกจากโหมดเพิ่มสถานที่' : 'เพิ่มที่ใหม่'}
          >
            {/* The mock turns the FAB itself into the way out, so the control that
                armed the mode is the control that leaves it (frames 1 → 2). */}
            {!armed && <span className="disc-fab-label">เพิ่มที่ใหม่</span>}
            {armed ? <CloseIcon /> : <PlusIcon />}
          </button>
          {/* Hidden until geolocation has actually produced an anchor, so it is
              never a button that does nothing. */}
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
        </div>

        {/* R8.3: armed, PlaceBottomSheet is replaced by the capture surface, which
            AddPlaceMode renders over the map itself. */}
        {armed ? null : selected ? (
          <PlaceSheet
            // Keyed on the place so switching the selected marker remounts the sheet —
            // otherwise React reuses the instance across places and a pending delete
            // flow (chosen trip, confirm stage) survives onto the newly selected place.
            key={selected.key}
            place={selected}
            onClose={() => dispatch(setSelectedKey(null))}
            onAddToTrip={(p) => setAddForPlace(p)}
            onCreateTrip={handleCreateTrip}
            onDeleted={() => setDeletedNote(true)}
            creatingTrip={creatingTrip}
          />
        ) : (
          <PlaceBottomSheet places={views} onSelect={(k) => dispatch(setSelectedKey(k))} />
        )}

        {deletedNote && <div className="disc-armed-toast" role="status">ลบแล้ว</div>}
      </div>

      {addForPlace && (
        <AddToTripDialog
          place={addForPlace}
          onClose={() => setAddForPlace(null)}
          onDone={(tripId) => { setAddForPlace(null); navigate(`/trips/${tripId}`) }}
        />
      )}

      {pickerFor && (
        <AddToTripDialog
          placeName={pickerFor.name}
          onPick={(tripId, tripName) => commitTo(tripId, tripName, pickerFor)}
          onClose={() => settlePending(false)}
          onDone={() => settlePending(true)}
        />
      )}

      {isLoading && <div className="disc-loading">กำลังโหลด…</div>}
    </div>
  )
}
