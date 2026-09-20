// frontend/src/pages/trips/components/TripMap.tsx
// Google Maps: Syncfusion has no interactive street map (frontend-guidelines §2 allowed exception).
//
// Since menunest-234…241 this is the trip screen's ONE map — the surface, not furniture.
// It carries three pin classes (spec §6.1):
//   route pin  — a Stop on the active Day. Numbered, severity-coloured, TAPPABLE.
//   ghost pin  — a Place on this Trip that is not a Stop on the active Day. Faint, tappable.
//   viewer pin — the device location. Unchanged, and it never extends the bounds.
import {useCallback, useEffect, useMemo, useState} from 'react'
import {APIProvider, Map, AdvancedMarker, Pin, useMap, useMapsLibrary} from '@vis.gl/react-google-maps'
import type {TripPlaceDto} from '../../../shared/api/api'
import type {RouteStop, RouteSegment} from '../hooks/useDayRoute'
import type {FlagSeverity} from '../hooks/useSchedule'
import type {GhostPin} from '../lib/ghostPins'
import {trackGoogleMapsError} from '../../../shared/telemetry/googleMapsTelemetry'
import {AddPlaceMode, type AddStopContext} from './AddPlaceMode'

type LatLng = {lat: number; lng: number}

// Severity → route-pin CSS modifier. NEVER interpolate the raw severity string
// (`.route-pin.suggestion` does not exist).
const PIN_CLASS: Record<FlagSeverity, string> = {problem: 'problem', suggestion: 'amber'}

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

function prefersReducedMotion(): boolean {
  return typeof window !== 'undefined' && !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
}

// Per-leg route lines. Routed legs draw the decoded encodedPolyline (road-following,
// solid teal); Estimated legs draw a dashed, faded, straight line between the two stops
// — an honest "we're guessing this segment" signal (ADR-023/024). @vis.gl/react-google-maps
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

// Frame the active Day's Stops. LatLngBounds lives in the 'core' library, not 'maps' (CF6).
//
// `fitPadding` keeps the route clear of the chrome overlaid on the map — the floating top
// bar, the Plan sheet, the Plan panel (spec §6.2). It is an OBJECT, and its identity is
// deliberately load-bearing: when the detent or the panel-collapsed state changes, the map
// CONTAINER does not resize, so the ResizeObserver below never fires and a new padding
// object is the only signal this effect gets that the visible area moved. Callers must
// therefore memoise the padding on the SETTLED detent, never on a live drag height.
//
// The ResizeObserver still earns its keep for real container changes (an orientation flip,
// the desktop window resizing): @vis.gl/react-google-maps (v1.8.3) ships none of its own,
// and without a re-fit the newly-revealed tiles stay grey.
function FitBounds({path, fitPadding = 64}: {path: LatLng[]; fitPadding?: number | google.maps.Padding}) {
  const map = useMap()
  const core = useMapsLibrary('core')
  useEffect(() => {
    if (!map || !core || path.length === 0) return
    const fit = () => {
      if (path.length === 1) {
        map.setCenter(path[0])
        map.setZoom(14)
        return
      }
      const bounds = new core.LatLngBounds()
      path.forEach((p) => bounds.extend(p))
      map.fitBounds(bounds, fitPadding)
    }
    fit()
    if (typeof ResizeObserver === 'undefined') return
    const el = map.getDiv()
    if (!el) return
    let raf = 0
    const ro = new ResizeObserver(() => {
      cancelAnimationFrame(raf)
      raf = requestAnimationFrame(fit)
    })
    ro.observe(el)
    return () => {
      ro.disconnect()
      cancelAnimationFrame(raf)
    }
  }, [map, core, path, fitPadding])
  return null
}

/**
 * Bring the **Selected Stop** into view (menunest-238). Only pans — never zooms — so the
 * frame the user built by pinching is not thrown away by a tap on a card.
 */
function PanToSelected({stop}: {stop: RouteStop | null}) {
  const map = useMap()
  const lat = stop?.lat
  const lng = stop?.lng
  useEffect(() => {
    if (!map || lat == null || lng == null) return
    if (prefersReducedMotion()) map.setCenter({lat, lng})
    else map.panTo({lat, lng})
  }, [map, lat, lng])
  return null
}

/**
 * The locate-me control. Keyed on a NONCE, not on the point: the viewer's position barely
 * changes between reads, so panning "when it changes" would make the button dead on the
 * second tap.
 */
function RecenterOnViewer({point, nonce}: {point?: {lat: number; lng: number} | null; nonce: number}) {
  const map = useMap()
  useEffect(() => {
    if (!map || !point || nonce === 0) return
    if (prefersReducedMotion()) map.setCenter(point)
    else map.panTo(point)
    // `point` is intentionally not a dependency — only a fresh tap recentres.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map, nonce])
  return null
}

export function TripMap({
  places,
  route,
  segments,
  summaryLabel,
  summaryText,
  addMode = false,
  addStopContext = null,
  gestureHandling = 'greedy',
  fitPadding,
  tripId,
  viewerLocation,
  ghostPins = [],
  ghostLabels = true,
  recenterNonce = 0,
  selectedStopId = null,
  onSelectStop,
  onSelectGhost,
  onExitAddMode,
}: {
  places: TripPlaceDto[]
  route?: RouteStop[]
  segments?: RouteSegment[]
  summaryLabel?: string
  summaryText?: string
  addMode?: boolean
  addStopContext?: AddStopContext | null
  gestureHandling?: string
  fitPadding?: number | google.maps.Padding
  tripId?: string
  viewerLocation?: {lat: number; lng: number} | null
  /** Places on this Trip that are not Stops on the active Day (spec §6.1). */
  ghostPins?: GhostPin[]
  /** False above the density limit — dots only, no labels (menunest-241). */
  ghostLabels?: boolean
  /** Bumped by the locate-me control; each bump pans to `viewerLocation`. */
  recenterNonce?: number
  selectedStopId?: string | null
  onSelectStop?(stopId: string | null): void
  onSelectGhost?(ghost: GhostPin): void
  onExitAddMode?: () => void
}) {
  const routeStops = route ?? []
  // menunest-239: the frame is the active Day's Stops and NOTHING else. `viewerLocation` used
  // to be prepended here, which is what zoomed the map out across the province until the
  // Stops piled up in one corner — the Approach leg still draws, because `useDayRoute` keeps
  // the viewer point in `segments`; only the bounds stop counting it.
  const path = useMemo<LatLng[]>(
    () => (route ?? []).map((r) => ({lat: r.lat, lng: r.lng})),
    [route],
  )

  // The POI place_id most recently tapped on the map (add-mode only). Pushed down
  // to AddPlaceMode, which resolves it once and clears it via onTapConsumed.
  const [tappedPlaceId, setTappedPlaceId] = useState<string | null>(null)
  // Stable callback keeps AddPlaceMode's tap-resolving effect from re-firing.
  const onTapConsumed = useCallback(() => setTappedPlaceId(null), [])
  // The empty-ground point most recently tapped (add-mode only). Pushed down to
  // AddPlaceMode, which turns it into a coordinate capture and clears it the same way.
  const [tappedLatLng, setTappedLatLng] = useState<{lat: number; lng: number} | null>(null)
  const onLatLngConsumed = useCallback(() => setTappedLatLng(null), [])
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
  const selectedStop = routeStops.find((r) => r.id === selectedStopId) ?? null

  return (
    <APIProvider apiKey={KEY} onError={trackGoogleMapsError}>
      {/* CF2: .trip-map has an explicit height defined in TripDetailPage.css */}
      <div className="trip-map">
        <Map
          mapId={MAP_ID}
          defaultCenter={center}
          defaultZoom={12}
          gestureHandling={gestureHandling}
          disableDefaultUI
          internalUsageAttributionIds={['gmp_git_agentskills_v1']}
          onClick={(ev) => {
            if (!addMode) {
              // Tapping empty map drops the selection — the way out of a Compact stop card
              // that does not require finding its ✕ (spec §6.3).
              onSelectStop?.(null)
              return
            }
            // POI clicks carry a placeId; empty-ground clicks do not (ADR-016).
            // Grounded: google IconMouseEvent exposes `placeId` + `latLng`, and
            // event.stop() suppresses the default POI info window; @vis.gl surfaces
            // these as ev.detail.placeId and ev.stop().
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
        >
          {routeMode ? (
            <>
              <RouteSegments segments={segments ?? []} />
              <FitBounds path={path} fitPadding={fitPadding} />
              <PanToSelected stop={selectedStop} />
              <RecenterOnViewer point={viewerLocation} nonce={recenterNonce} />
              {viewerLocation && (
                <AdvancedMarker
                  position={{lat: viewerLocation.lat, lng: viewerLocation.lng}}
                  title="คุณอยู่ที่นี่"
                  zIndex={0}
                >
                  <div className="viewer-pin" aria-label="ตำแหน่งปัจจุบันของคุณ" />
                </AdvancedMarker>
              )}

              {/* Ghost pins render in route mode TOO — this layer used to be the `else` of
                  route mode, which is exactly why a saved Place was invisible while planning
                  a Day (issue #6, menunest-235). Below the route pins, always. */}
              <GhostLayer pins={ghostPins} labels={ghostLabels} armed={addMode} onSelect={onSelectGhost} />

              {routeStops.map((r) => (
                <AdvancedMarker
                  key={r.id}
                  position={{lat: r.lat, lng: r.lng}}
                  title={r.name}
                  zIndex={r.id === selectedStopId ? 500 : r.order}
                  // While armed every tap belongs to capture (ADR-163), so the pins stop
                  // taking selections rather than competing with the capture surface.
                  clickable={!addMode}
                  onClick={() => { if (!addMode) onSelectStop?.(r.id) }}
                >
                  {/* A real <button>: the map is not the only route to any action, but every
                      map-tappable pin still has to be reachable by keyboard and named for AT
                      (spec §8). The marker's own onClick above covers the pointer path. */}
                  <button
                    type="button"
                    className={`route-pin${r.severity ? ' ' + PIN_CLASS[r.severity] : ''}${r.id === selectedStopId ? ' sel' : ''}`}
                    data-testid="route-pin"
                    aria-pressed={r.id === selectedStopId}
                    aria-label={
                      `จุดที่ ${r.order} — ${r.name}, ถึง ${r.arrival}` + (r.flagNote ? ` (${r.flagNote})` : '')
                    }
                    onClick={(e) => {
                      e.stopPropagation()
                      if (!addMode) onSelectStop?.(r.id)
                    }}
                  >
                    <span className="route-callout">{r.arrival} · {r.name}</span>
                    <span className="route-dot">{r.order}</span>
                  </button>
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
            target={{kind: 'trip', tripId}}
            onExit={() => onExitAddMode?.()}
            tappedPlaceId={tappedPlaceId}
            onTapConsumed={onTapConsumed}
            tappedLatLng={tappedLatLng}
            onLatLngConsumed={onLatLngConsumed}
            onSelectedChange={setAddPin}
            addStopContext={addStopContext}
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

/**
 * The ghost layer. 12px desaturated dot with the Place's name beside it, `zIndex` below
 * every route pin so it can never bury the route it is meant to sit behind.
 */
function GhostLayer({
  pins,
  labels,
  armed,
  onSelect,
}: {
  pins: GhostPin[]
  labels: boolean
  armed: boolean
  onSelect?(ghost: GhostPin): void
}) {
  return (
    <>
      {pins.map((g) => (
        <AdvancedMarker
          key={g.id}
          position={{lat: g.lat, lng: g.lng}}
          title={g.name}
          zIndex={1}
          clickable={!armed}
          onClick={() => { if (!armed) onSelect?.(g) }}
        >
          <button
            type="button"
            className={`ghost-pin${armed ? ' armed' : ''}`}
            data-testid="ghost-pin"
            aria-label={
              g.scheduledDayNumber == null
                ? `${g.name} — ยังไม่อยู่ในแผนวันไหน`
                : `${g.name} — อยู่ในวัน ${g.scheduledDayNumber}`
            }
            onClick={(e) => {
              e.stopPropagation()
              if (!armed) onSelect?.(g)
            }}
          >
            <span className="ghost-dot" aria-hidden="true" />
            {labels && <span className="ghost-label">{g.name}</span>}
          </button>
        </AdvancedMarker>
      ))}
    </>
  )
}
