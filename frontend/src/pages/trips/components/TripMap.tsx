// frontend/src/pages/trips/components/TripMap.tsx
// Google Maps: Syncfusion has no interactive street map (frontend-guidelines §2 allowed exception).
import { APIProvider, Map, AdvancedMarker, Pin } from '@vis.gl/react-google-maps'
import type { TripPlaceDto } from '../../../shared/api/api'

const CAT_COLOR: Record<string, string> = {
  Stay:  '#6d5ae6',
  Eat:   '#e2553e',
  See:   '#1f9d76',
  Cafe:  '#b4791f',
  Shop:  '#c2418f',
  Other: '#0e8f9e',
}

const KEY    = import.meta.env.VITE_GOOGLE_MAPS_BROWSER_KEY as string | undefined
const MAP_ID = (import.meta.env.VITE_GOOGLE_MAPS_MAP_ID as string | undefined) ?? 'DEMO_MAP_ID'

// Bangkok city-centre fallback when no places are loaded yet.
const BKK_CENTER = { lat: 13.7563, lng: 100.5018 }

export function TripMap({ places }: { places: TripPlaceDto[] }) {
  if (!KEY) {
    return (
      <div className="trip-map-fallback">
        ตั้งค่า VITE_GOOGLE_MAPS_BROWSER_KEY เพื่อแสดงแผนที่
      </div>
    )
  }

  const center = places.length
    ? { lat: places[0].lat, lng: places[0].lng }
    : BKK_CENTER

  return (
    <APIProvider apiKey={KEY}>
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
          {places.map((p) => (
            <AdvancedMarker
              key={p.id}
              position={{ lat: p.lat, lng: p.lng }}
              title={p.name}
            >
              <Pin
                background={CAT_COLOR[p.category] ?? CAT_COLOR.Other}
                borderColor="#fff"
                glyphColor="#fff"
              />
            </AdvancedMarker>
          ))}
        </Map>
      </div>
    </APIProvider>
  )
}
