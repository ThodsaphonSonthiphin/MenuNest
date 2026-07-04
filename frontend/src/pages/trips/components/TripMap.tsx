// frontend/src/pages/trips/components/TripMap.tsx
// Google Maps: Syncfusion has no interactive street map (frontend-guidelines §2 allowed exception).
import {useCallback, useEffect, useMemo, useState} from 'react'
import {APIProvider, Map, AdvancedMarker, Pin, useMap, useMapsLibrary} from '@vis.gl/react-google-maps'
import type {TripPlaceDto} from '../../../shared/api/api'
import type {RouteStop, RouteSegment} from '../hooks/useDayRoute'
import {trackGoogleMapsError} from '../../../shared/telemetry/googleMapsTelemetry'
import {AddPlaceMode} from './AddPlaceMode'

type LatLng = {lat: number; lng: number}

const CAT_COLOR: Record<string, string> = {
  Stay:  '#6d5ae6',
  Eat:   '#e2553e',
  See:   '#1f9d76',
  Cafe:  '#b4791f',
  Shop:  '#c2418f',
  Other: '#0e8f9e',
}

const KEY    = import.meta.env.VITE_GOOGLE_MAPS_BROWSER_KEY as string | undefined
// `||` not `??`: an unset GitHub Actions secret renders as '' (not undefined),
// and '' ?? 'DEMO_MAP_ID' keeps the empty string → <Map mapId=""> → Google logs
// "initialized without a valid Map ID" and AdvancedMarkers silently break.
const MAP_ID = (import.meta.env.VITE_GOOGLE_MAPS_MAP_ID as string | undefined) || 'DEMO_MAP_ID'

// Bangkok city-centre fallback when no places are loaded yet.
const BKK_CENTER = {lat: 13.7563, lng: 100.5018}

// Per-leg route lines. Routed legs draw the decoded encodedPolyline (road-following,
// solid teal); Estimated legs draw a dashed, faded, straight line between the two stops
// — an honest "we're guessing this segment" signal (ADR-016/019). @vis.gl/react-google-maps
// has no <Polyline>, so create google.maps.Polyline imperatively and dispose ALL of them.
const DASH = {path: 'M 0,-1 0,1', strokeOpacity: 0.55, strokeColor: '#0e8f9e', scale: 3}

function RouteSegments({segments}: {segments: RouteSegment[]}) {
  const map = useMap()
  const maps = useMapsLibrary('maps')
  const geometry = useMapsLibrary('geometry')
  useEffect(() => {
    if (!map || !maps || !geometry || segments.length === 0) return
    const lines = segments.map((seg) => {
      const routed = seg.source === 'Routed' && !!seg.encodedPolyline
      const path = routed
        ? geometry.encoding.decodePath(seg.encodedPolyline as string)
        : [seg.from, seg.to]
      const opts = routed
        ? {path, strokeColor: '#0e8f9e', strokeOpacity: 0.9, strokeWeight: 4}
        : {path, strokeOpacity: 0, icons: [{icon: DASH, offset: '0', repeat: '12px'}]}
      const line = new maps.Polyline(opts)
      line.setMap(map)
      return line
    })
    return () => lines.forEach((l) => l.setMap(null))
  }, [map, maps, geometry, segments])
  return null
}

// Frame all stops. LatLngBounds lives in the 'core' library, not 'maps' (CF6).
function FitBounds({path}: {path: LatLng[]}) {
  const map = useMap()
  const core = useMapsLibrary('core')
  useEffect(() => {
    if (!map || !core || path.length === 0) return
    if (path.length === 1) {
      map.setCenter(path[0])
      map.setZoom(14)
      return
    }
    const bounds = new core.LatLngBounds()
    path.forEach((p) => bounds.extend(p))
    map.fitBounds(bounds, 64)
  }, [map, core, path])
  return null
}

export function TripMap({
  places,
  route,
  segments,
  summaryLabel,
  summaryText,
  addMode = false,
  tripId,
  onExitAddMode,
}: {
  places: TripPlaceDto[]
  route?: RouteStop[]
  segments?: RouteSegment[]
  summaryLabel?: string
  summaryText?: string
  addMode?: boolean
  tripId?: string
  onExitAddMode?: () => void
}) {
  const routeStops = route ?? []
  // `route` is a stable reference from useDayRoute (memoised), so depending on it
  // directly memoises path correctly without rebuilding the polyline each render.
  const path = useMemo<LatLng[]>(
    () => (route ?? []).map((r) => ({lat: r.lat, lng: r.lng})),
    [route],
  )

  // The POI place_id most recently tapped on the map (add-mode only). Pushed down
  // to AddPlaceMode, which resolves it once and clears it via onTapConsumed.
  const [tappedPlaceId, setTappedPlaceId] = useState<string | null>(null)
  // Stable callback keeps AddPlaceMode's tap-resolving effect from re-firing.
  const onTapConsumed = useCallback(() => setTappedPlaceId(null), [])
  // Coords of the currently-selected add-mode place. AddPlaceMode (a .trip-map
  // sibling of <Map>) reports these up; the temp teal pin is rendered inside <Map>
  // below, because AdvancedMarker needs the map subtree.
  const [addPin, setAddPin] = useState<{lat: number; lng: number} | null>(null)

  if (!KEY) {
    return (
      <div className="trip-map-fallback">
        ตั้งค่า VITE_GOOGLE_MAPS_BROWSER_KEY เพื่อแสดงแผนที่
      </div>
    )
  }

  const routeMode = routeStops.length > 0
  const center = routeMode
    ? {lat: routeStops[0].lat, lng: routeStops[0].lng}
    : places.length
      ? {lat: places[0].lat, lng: places[0].lng}
      : BKK_CENTER

  return (
    <APIProvider apiKey={KEY} onError={trackGoogleMapsError}>
      {/* CF2: .trip-map has an explicit height defined in TripDetailPage.css */}
      <div className="trip-map">
        <Map
          mapId={MAP_ID}
          defaultCenter={center}
          defaultZoom={12}
          gestureHandling="greedy"
          disableDefaultUI
          internalUsageAttributionIds={['gmp_git_agentskills_v1']}
          onClick={(ev) => {
            if (!addMode) return
            // POI clicks carry a placeId; empty-ground clicks do not (ADR-016).
            // Grounded: google IconMouseEvent exposes `placeId` + `latLng`, and
            // event.stop() suppresses the default POI info window; @vis.gl surfaces
            // these as ev.detail.placeId and ev.stop().
            const placeId = ev.detail.placeId
            if (placeId) {
              ev.stop() // suppress the default Google info window
              setTappedPlaceId(placeId)
            }
          }}
        >
          {routeMode ? (
            <>
              <RouteSegments segments={segments ?? []} />
              <FitBounds path={path} />
              {routeStops.map((r) => (
                <AdvancedMarker
                  key={r.id}
                  position={{lat: r.lat, lng: r.lng}}
                  title={r.name}
                  zIndex={r.order}
                >
                  <div className={`route-pin${r.amber ? ' amber' : ''}`}>
                    <div className="route-callout">{r.arrival} · {r.name}</div>
                    <div className="route-dot">{r.order}</div>
                  </div>
                </AdvancedMarker>
              ))}
            </>
          ) : (
            places.map((p) => (
              <AdvancedMarker
                key={p.id}
                position={{lat: p.lat, lng: p.lng}}
                title={p.name}
              >
                <Pin
                  background={CAT_COLOR[p.category] ?? CAT_COLOR.Other}
                  borderColor="#fff"
                  glyphColor="#fff"
                />
              </AdvancedMarker>
            ))
          )}

          {addMode && addPin && (
            <AdvancedMarker position={addPin} zIndex={999}>
              <Pin background="#0e8f9e" borderColor="#fff" glyphColor="#fff" scale={1.3} />
            </AdvancedMarker>
          )}
        </Map>

        {addMode && tripId && (
          <AddPlaceMode
            tripId={tripId}
            onExit={() => onExitAddMode?.()}
            tappedPlaceId={tappedPlaceId}
            onTapConsumed={onTapConsumed}
            onSelectedChange={setAddPin}
          />
        )}

        {routeMode && summaryText && (
          <div className="map-day-card">
            <div className="map-day-label">{summaryLabel}</div>
            <div className="map-day-text">{summaryText}</div>
          </div>
        )}
      </div>
    </APIProvider>
  )
}
