// frontend/src/pages/discover/components/DiscoverMap.tsx
// Google Maps: mirrors TripMap.tsx's env/key pattern and imperative-marker
// approach, but clusters category markers via @googlemaps/markerclusterer
// instead of rendering <AdvancedMarker> declaratively (TripMap does not
// cluster). Pure filter/sort/status logic lives in Tasks 4-5 (lib/distance,
// lib/discoverFilter) — this component only wires places -> map markers and
// reports viewport changes back up via onScopeChange.
import {useEffect, useRef} from 'react'
import {APIProvider, Map, useMap, useMapsLibrary} from '@vis.gl/react-google-maps'
import {MarkerClusterer} from '@googlemaps/markerclusterer'
import {trackGoogleMapsError} from '../../../shared/telemetry/googleMapsTelemetry'
import type {DiscoverPlaceView} from '../lib/discoverFilter'
import {categorySvgMarkup} from './DiscoverIcons'
import {catColor} from '../lib/categoryStyle'

const KEY = import.meta.env.VITE_GOOGLE_MAPS_BROWSER_KEY as string | undefined
// `||` not `??`: an unset GitHub Actions secret renders as '' (not undefined),
// and '' ?? 'DEMO_MAP_ID' keeps the empty string → <Map mapId=""> → Google logs
// "initialized without a valid Map ID" and AdvancedMarkers silently break.
const MAP_ID = (import.meta.env.VITE_GOOGLE_MAPS_MAP_ID as string | undefined) || 'DEMO_MAP_ID'
// Bangkok city-centre fallback when there's no viewer anchor yet.
const BKK_CENTER = {lat: 13.7563, lng: 100.5018}

interface Props {
  places: DiscoverPlaceView[]
  anchor: {lat: number; lng: number} | null
  selectedKey: string | null
  onSelect: (key: string) => void
  onScopeChange: (b: {north: number; south: number; east: number; west: number}) => void
  /** Incremented by the caller to re-centre the camera on `anchor`. */
  recenterNonce: number
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

function Markers({places, onSelect}: {places: DiscoverPlaceView[]; onSelect: (k: string) => void}) {
  const map = useMap()
  const markerLib = useMapsLibrary('marker')
  const clustererRef = useRef<MarkerClusterer | null>(null)

  useEffect(() => {
    if (!map || !markerLib) return
    const markers = places.map((p) => {
      const marker = new markerLib.AdvancedMarkerElement({
        position: {lat: p.lat, lng: p.lng},
        title: p.name,
        content: pinElement(p.category, catColor(p.category), p.visited),
      })
      marker.addListener('gmp-click', () => onSelect(p.key))
      return marker
    })
    clustererRef.current = new MarkerClusterer({map, markers})
    return () => {
      clustererRef.current?.setMap(null)
      clustererRef.current = null
    }
  }, [map, markerLib, places, onSelect])

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

export function DiscoverMap({places, anchor, selectedKey: _sel, onSelect, onScopeChange, recenterNonce}: Props) {
  // onCameraChanged fires on every animation frame during a pan/zoom; debounce the
  // scope dispatch so the marker/clusterer rebuild doesn't run on every frame.
  const scopeTimer = useRef<number | null>(null)
  useEffect(() => () => { if (scopeTimer.current != null) clearTimeout(scopeTimer.current) }, [])

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
          <Markers places={places} onSelect={onSelect} />
          <ViewerPin anchor={anchor} />
          <MapCamera anchor={anchor} recenterNonce={recenterNonce} />
        </Map>
      </div>
    </APIProvider>
  )
}
