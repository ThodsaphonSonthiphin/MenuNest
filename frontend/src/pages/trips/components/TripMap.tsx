// frontend/src/pages/trips/components/TripMap.tsx
// Google Maps: Syncfusion has no interactive street map (frontend-guidelines §2 allowed exception).
import {useEffect, useMemo} from 'react'
import {APIProvider, Map, AdvancedMarker, Pin, useMap, useMapsLibrary} from '@vis.gl/react-google-maps'
import type {TripPlaceDto} from '../../../shared/api/api'
import type {RouteStop} from '../hooks/useDayRoute'
import {trackGoogleMapsError} from '../../../shared/telemetry/googleMapsTelemetry'

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

// Straight teal line through the day's stops in order. @vis.gl/react-google-maps
// has no <Polyline>, so create google.maps.Polyline imperatively and clean it up.
// (Road-accurate geometry would need the Routes API; the per-leg distance/time
// shown in the itinerary already comes from the backend, so straight legs here.)
function RoutePolyline({path}: {path: LatLng[]}) {
  const map = useMap()
  const maps = useMapsLibrary('maps')
  useEffect(() => {
    if (!map || !maps || path.length < 2) return
    const line = new maps.Polyline({
      path,
      geodesic: true,
      strokeColor: '#0e8f9e',
      strokeOpacity: 0.9,
      strokeWeight: 4,
    })
    line.setMap(map)
    return () => line.setMap(null)
  }, [map, maps, path])
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
  summaryLabel,
  summaryText,
}: {
  places: TripPlaceDto[]
  route?: RouteStop[]
  summaryLabel?: string
  summaryText?: string
}) {
  const routeStops = route ?? []
  const path = useMemo<LatLng[]>(
    () => routeStops.map((r) => ({lat: r.lat, lng: r.lng})),
    // Re-run only when the actual coordinates change, not on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [routeStops.map((r) => `${r.lat},${r.lng}`).join('|')],
  )

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
        >
          {routeMode ? (
            <>
              <RoutePolyline path={path} />
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
        </Map>

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
