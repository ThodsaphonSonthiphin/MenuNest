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

const KEY = import.meta.env.VITE_GOOGLE_MAPS_BROWSER_KEY as string | undefined
// `||` not `??`: an unset GitHub Actions secret renders as '' (not undefined),
// and '' ?? 'DEMO_MAP_ID' keeps the empty string → <Map mapId=""> → Google logs
// "initialized without a valid Map ID" and AdvancedMarkers silently break.
const MAP_ID = (import.meta.env.VITE_GOOGLE_MAPS_MAP_ID as string | undefined) || 'DEMO_MAP_ID'
// Bangkok city-centre fallback when there's no viewer anchor yet.
const BKK_CENTER = {lat: 13.7563, lng: 100.5018}

// Category → pin colour (mirrors TripMap CAT_COLOR).
const CAT_COLOR: Record<string, string> = {
  Stay: '#6d5ae6', Eat: '#e2553e', See: '#1f9d76', Cafe: '#b4791f', Shop: '#c2418f', Other: '#0e8f9e',
}

interface Props {
  places: DiscoverPlaceView[]
  anchor: {lat: number; lng: number} | null
  selectedKey: string | null
  onSelect: (key: string) => void
  onScopeChange: (b: {north: number; south: number; east: number; west: number}) => void
}

function pinElement(color: string, dimmed: boolean): HTMLElement {
  const el = document.createElement('div')
  el.className = 'disc-pin'
  el.style.cssText = `width:26px;height:26px;border-radius:50% 50% 50% 2px;transform:rotate(45deg);border:2.5px solid #fff;box-shadow:0 3px 8px rgba(15,23,42,.3);background:${color};opacity:${dimmed ? 0.45 : 1}`
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
        content: pinElement(CAT_COLOR[p.category] ?? CAT_COLOR.Other, p.visited),
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

export function DiscoverMap({places, anchor, selectedKey: _sel, onSelect, onScopeChange}: Props) {
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
            onScopeChange({north: ne.lat(), south: sw.lat(), east: ne.lng(), west: sw.lng()})
          }}
        >
          <Markers places={places} onSelect={onSelect} />
          <ViewerPin anchor={anchor} />
        </Map>
      </div>
    </APIProvider>
  )
}
