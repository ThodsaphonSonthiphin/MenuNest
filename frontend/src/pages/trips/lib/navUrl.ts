// frontend/src/pages/trips/lib/navUrl.ts
//
// Pure builders for Google Maps "Maps URLs" directions deep links (the Trip
// navigate hand-off). No React, no RTK, no window — callers open the returned
// URL string. See ADR-011 and
// docs/superpowers/specs/2026-07-03-trip-navigate-handoff-design.md.
import type {TravelMode} from '../../../shared/api/api'

const DIR_BASE = 'https://www.google.com/maps/dir/'

export interface NavPoint {
  lat: number
  lng: number
  placeId?: string | null
}

export interface DayNav {
  url: string
  coveredCount: number
  overflow: boolean
}

const GMAPS_MODE: Record<TravelMode, 'driving' | 'walking' | 'transit'> = {
  Drive: 'driving',
  Walk: 'walking',
  Transit: 'transit',
}

export function travelModeToGmaps(mode: TravelMode): 'driving' | 'walking' | 'transit' | undefined {
  return GMAPS_MODE[mode]
}

const isUsable = (p: {lat: number; lng: number}): boolean =>
  Number.isFinite(p.lat) && Number.isFinite(p.lng) && !(p.lat === 0 && p.lng === 0)

const coord = (p: {lat: number; lng: number}): string =>
  `${p.lat.toFixed(6)},${p.lng.toFixed(6)}`

/** Collapse consecutive duplicate points (same placeId, else same 6-dp coord). */
function dedupeConsecutive(points: NavPoint[]): NavPoint[] {
  const out: NavPoint[] = []
  for (const p of points) {
    const prev = out[out.length - 1]
    if (prev) {
      const samePlace = !!p.placeId && !!prev.placeId && p.placeId === prev.placeId
      if (samePlace || coord(p) === coord(prev)) continue
    }
    out.push(p)
  }
  return out
}

/** Single-destination link to one Place. null when coords are unusable. */
export function buildStopNavUrl(
  place: {lat: number; lng: number; googlePlaceId?: string | null},
  mode: TravelMode,
): string | null {
  if (!isUsable(place)) return null
  const params = new URLSearchParams({api: '1', destination: coord(place)})
  if (place.googlePlaceId) params.set('destination_place_id', place.googlePlaceId)
  const gmode = travelModeToGmaps(mode)
  if (gmode) params.set('travelmode', gmode)
  params.set('dir_action', 'navigate')
  return `${DIR_BASE}?${params.toString()}`
}

/**
 * Whole-day route from the device's current location (origin omitted) through
 * the day's Stops in order. Filters unusable points, collapses consecutive
 * dupes, applies the waypoint cap, and encodes lat,lng only (no place_ids —
 * positional-alignment + URL-length safety). null when no usable point remains.
 */
export function buildDayNavUrl(points: NavPoint[], cap: number, mode: TravelMode): DayNav | null {
  const usable = dedupeConsecutive(points.filter(isUsable))
  if (usable.length === 0) return null

  const fit = cap + 1
  const overflow = usable.length > fit
  const covered = overflow ? fit : usable.length
  const included = usable.slice(0, covered)

  const destination = included[included.length - 1]
  const waypoints = included.slice(0, -1)

  const params = new URLSearchParams({api: '1', destination: coord(destination)})
  if (waypoints.length > 0) params.set('waypoints', waypoints.map(coord).join('|'))
  const gmode = travelModeToGmaps(mode)
  if (gmode) params.set('travelmode', gmode)
  params.set('dir_action', 'navigate')

  return {url: `${DIR_BASE}?${params.toString()}`, coveredCount: covered, overflow}
}
