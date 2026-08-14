// frontend/src/pages/discover/components/DiscoverMap.tsx
// Google Maps: mirrors TripMap.tsx's env/key pattern and imperative-marker
// approach, but clusters category markers via @googlemaps/markerclusterer
// instead of rendering <AdvancedMarker> declaratively (TripMap does not
// cluster). Pure filter/sort/status logic lives in Tasks 4-5 (lib/distance,
// lib/discoverFilter) — this component only wires places -> map markers and
// reports viewport changes back up via onScopeChange.
//
// Capture mode (ADR-150, generalised by ADR-163): while armed this map reads a
// tap exactly the way the trip map does, and hosts the SAME AddPlaceMode
// component as a sibling of <Map> — which is what makes every capture input
// reach both surfaces without a second implementation.
import {useCallback, useEffect, useRef, useState} from 'react'
import {AdvancedMarker, APIProvider, Map, Pin, useMap, useMapsLibrary} from '@vis.gl/react-google-maps'
import {MarkerClusterer} from '@googlemaps/markerclusterer'
import {trackGoogleMapsError} from '../../../shared/telemetry/googleMapsTelemetry'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {categorySvgMarkup} from './DiscoverIcons'
import {catColor} from '../lib/categoryStyle'
import {AddPlaceMode, type CaptureTarget} from '../../trips/components/AddPlaceMode'

const KEY = import.meta.env.VITE_GOOGLE_MAPS_BROWSER_KEY as string | undefined
// `||` not `??`: an unset GitHub Actions secret renders as '' (not undefined),
// and '' ?? 'DEMO_MAP_ID' keeps the empty string → <Map mapId=""> → Google logs
// "initialized without a valid Map ID" and AdvancedMarkers silently break.
const MAP_ID = (import.meta.env.VITE_GOOGLE_MAPS_MAP_ID as string | undefined) || 'DEMO_MAP_ID'
// Bangkok city-centre fallback when there's no viewer anchor yet.
const BKK_CENTER = {lat: 13.7563, lng: 100.5018}

// The draft pin for the place currently being captured. Same values TripMap uses, in a
// named table for the same reason its CAT_COLOR is one: <Pin> takes plain colour strings
// (it renders into Google's own marker DOM, outside our stylesheet), so the tokens cannot
// come from CSS and belong in one place rather than inline at the call site.
const DRAFT_PIN = {bg: 'rgb(14, 143, 158)', border: 'rgb(255, 255, 255)', glyph: 'rgb(255, 255, 255)'}

interface Props {
  places: DiscoverPlaceView[]
  anchor: {lat: number; lng: number} | null
  selectedKey: string | null
  onSelect: (key: string) => void
  onScopeChange: (b: {north: number; south: number; east: number; west: number}) => void
  /** Incremented by the caller to re-centre the camera on `anchor`. */
  recenterNonce: number
  /** R8.1/R8.2: unarmed, this map behaves exactly as it did before capture existed. */
  armed: boolean
  /** Where a finished capture commits. Supplied only while armed. */
  captureTarget: CaptureTarget | null
  onExitArmed: () => void
}

// The teardrop is rotated 45°, so the glyph inside is counter-rotated −45° to sit
// upright — same trick as `.pin .dot svg` in docs/mocks/place-discovery-mock.html.
// Without the glyph the pins were flat colour blobs and the user's own saved places
// read as less important than Google's base-map POI icons, inverting the mock's
// "map คือพระเอก — หมุดสีตามหมวด" intent.
function pinElement(category: string, color: string, dimmed: boolean): HTMLElement {
  const el = document.createElement('div')
  el.className = 'disc-pin'
  el.style.cssText =
    `width:28px;height:28px;border-radius:50% 50% 50% 2px;transform:rotate(45deg);` +
    `border:2.5px solid #fff;box-shadow:0 3px 8px rgba(15,23,42,.3);background:${color};` +
    `opacity:${dimmed ? 0.45 : 1};display:flex;align-items:center;justify-content:center;color:#fff`
  // Static markup built from our own constant table — no user-supplied content.
  el.innerHTML = categorySvgMarkup(category, 14, -45)
  return el
}

function Markers({
  places, onSelect, armed, onOwnPin,
}: {
  places: DiscoverPlaceView[]
  onSelect: (k: string) => void
  armed: boolean
  onOwnPin: () => void
}) {
  const map = useMap()
  const markerLib = useMapsLibrary('marker')
  const clustererRef = useRef<MarkerClusterer | null>(null)

  useEffect(() => {
    if (!map || !markerLib) return
    const markers = places.map((p) => {
      const marker = new markerLib.AdvancedMarkerElement({
        position: {lat: p.lat, lng: p.lng},
        title: p.name,
        // R8.3: while armed EVERY own pin dims, which is one of the three signals
        // that the map is in capture mode. Unarmed, only Visited places dim.
        content: pinElement(p.category, catColor(p.category), p.visited || armed),
      })
      // R8.2: armed, a tap on one of the user's own pins is a warning and nothing
      // else — no PlaceSheet, and therefore no Hourly-forecast call for that place.
      marker.addListener('gmp-click', () => (armed ? onOwnPin() : onSelect(p.key)))
      return marker
    })
    clustererRef.current = new MarkerClusterer({map, markers})
    return () => {
      clustererRef.current?.setMap(null)
      clustererRef.current = null
    }
  }, [map, markerLib, places, onSelect, armed, onOwnPin])

  return null
}

// `defaultCenter`/`defaultZoom` on <Map> are read only once at mount, but
// `anchor` resolves asynchronously from navigator.geolocation AFTER the map
// has already mounted (DiscoverPage's effect) — so the uncontrolled default
// alone leaves the map stuck at the Bangkok/zoom-6 fallback forever. Drive
// the camera imperatively instead: pan/zoom once, the first time an anchor
// becomes available, and never again (so it doesn't fight later manual pans).
function MapCamera({anchor, recenterNonce}: {anchor: {lat: number; lng: number} | null; recenterNonce: number}) {
  const map = useMap()
  const done = useRef(false)
  useEffect(() => {
    if (!map || !anchor || done.current) return
    map.panTo(anchor)
    map.setZoom(13)
    done.current = true
  }, [map, anchor])

  // Explicit "take me back" from the anchor pill / locate FAB. Separate from the
  // once-only effect above so a manual pan is still never fought automatically.
  const lastNonce = useRef(recenterNonce)
  useEffect(() => {
    if (!map || !anchor || recenterNonce === lastNonce.current) return
    lastNonce.current = recenterNonce
    map.panTo(anchor)
    map.setZoom(14)
  }, [map, anchor, recenterNonce])
  return null
}

function ViewerPin({anchor}: {anchor: {lat: number; lng: number} | null}) {
  const map = useMap()
  const markerLib = useMapsLibrary('marker')
  useEffect(() => {
    if (!map || !markerLib || !anchor) return
    const dot = document.createElement('div')
    dot.className = 'viewer-pin'
    const marker = new markerLib.AdvancedMarkerElement({position: anchor, content: dot, zIndex: 0, title: 'คุณอยู่ที่นี่'})
    marker.map = map
    return () => { marker.map = null }
  }, [map, markerLib, anchor])
  return null
}

export function DiscoverMap({
  places, anchor, selectedKey: _sel, onSelect, onScopeChange, recenterNonce,
  armed, captureTarget, onExitArmed,
}: Props) {
  // onCameraChanged fires on every animation frame during a pan/zoom; debounce the
  // scope dispatch so the marker/clusterer rebuild doesn't run on every frame.
  const scopeTimer = useRef<number | null>(null)
  useEffect(() => () => { if (scopeTimer.current != null) clearTimeout(scopeTimer.current) }, [])

  // Armed-tap plumbing, identical in shape to TripMap's: the tapped POI / point is
  // pushed DOWN to AddPlaceMode, which resolves it once and clears it back up. Stable
  // callbacks, because AddPlaceMode's resolve-once effect is keyed on their identity.
  const [tappedPlaceId, setTappedPlaceId] = useState<string | null>(null)
  const onTapConsumed = useCallback(() => setTappedPlaceId(null), [])
  const [tappedLatLng, setTappedLatLng] = useState<{lat: number; lng: number} | null>(null)
  const onLatLngConsumed = useCallback(() => setTappedLatLng(null), [])
  const [addPin, setAddPin] = useState<{lat: number; lng: number} | null>(null)

  // R8.2's third target. A transient strip rather than a blocking dialog: the tap was
  // almost certainly a mis-aim, and the user is mid-run capturing other places.
  const [ownPinWarned, setOwnPinWarned] = useState(false)
  const onOwnPin = useCallback(() => setOwnPinWarned(true), [])
  useEffect(() => {
    if (!ownPinWarned) return
    const t = setTimeout(() => setOwnPinWarned(false), 2200)
    return () => clearTimeout(t)
  }, [ownPinWarned])
  // Leaving capture mode must not leave the warning behind.
  useEffect(() => { if (!armed) setOwnPinWarned(false) }, [armed])

  if (!KEY) {
    return <div className="trip-map-fallback">ตั้งค่า VITE_GOOGLE_MAPS_BROWSER_KEY เพื่อแสดงแผนที่</div>
  }
  return (
    <APIProvider apiKey={KEY} onError={trackGoogleMapsError}>
      <div className="discover-map">
        <Map
          mapId={MAP_ID}
          defaultCenter={anchor ?? BKK_CENTER}
          defaultZoom={anchor ? 13 : 6}
          gestureHandling="greedy"
          disableDefaultUI
          internalUsageAttributionIds={['gmp_git_agentskills_v1']}
          onClick={(ev) => {
            // R8.1: unarmed, a POI or ground tap does nothing at all — the map is
            // exactly what it was before capture existed.
            if (!armed) return
            // POI clicks carry a placeId; empty-ground clicks do not (ADR-016).
            const placeId = ev.detail.placeId
            if (placeId) {
              ev.stop() // suppress the default Google info window
              setTappedPlaceId(placeId)
              return
            }
            // ADR-164 §2: an empty-ground tap is not dead — it becomes a coordinate
            // capture prefilled with the tapped point. No Geocoding call is made, which
            // is what keeps this path at $0 (spec R8.2, R7.3).
            const ll = ev.detail.latLng
            if (ll) setTappedLatLng({lat: ll.lat, lng: ll.lng})
          }}
          onCameraChanged={(ev) => {
            const b = ev.map.getBounds()
            if (!b) return
            const ne = b.getNorthEast()
            const sw = b.getSouthWest()
            const bounds = {north: ne.lat(), south: sw.lat(), east: ne.lng(), west: sw.lng()}
            if (scopeTimer.current != null) clearTimeout(scopeTimer.current)
            scopeTimer.current = window.setTimeout(() => onScopeChange(bounds), 200)
          }}
        >
          <Markers places={places} onSelect={onSelect} armed={armed} onOwnPin={onOwnPin} />
          <ViewerPin anchor={anchor} />
          <MapCamera anchor={anchor} recenterNonce={recenterNonce} />
          {armed && addPin && (
            <AdvancedMarker position={addPin} zIndex={999}>
              <Pin background={DRAFT_PIN.bg} borderColor={DRAFT_PIN.border} glyphColor={DRAFT_PIN.glyph} scale={1.3} />
            </AdvancedMarker>
          )}
        </Map>

        {/* A sibling of <Map>, exactly as on the trip map — which is why every
            .add-* overlay lands correctly here: .discover-map is position:relative
            and .trip-map is too, so the shared CSS needs no Discover variant. */}
        {armed && captureTarget && (
          <AddPlaceMode
            target={captureTarget}
            onExit={onExitArmed}
            tappedPlaceId={tappedPlaceId}
            onTapConsumed={onTapConsumed}
            tappedLatLng={tappedLatLng}
            onLatLngConsumed={onLatLngConsumed}
            onSelectedChange={setAddPin}
            bannerText={{
              text: 'แตะจุดบนแผนที่เพื่อเพิ่ม',
              sub: 'หรือค้นหาชื่อ / วางลิงก์ Google Maps · แตะที่ว่าง = พิกัด',
            }}
          />
        )}

        {ownPinWarned && (
          <div className="disc-armed-toast" role="status">มีอยู่ในคลังแล้ว</div>
        )}
      </div>
    </APIProvider>
  )
}
