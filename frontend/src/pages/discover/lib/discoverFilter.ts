import type {DiscoverPlaceDto, PlaceCategory} from '../../../shared/api/api'
import {haversineKm} from './distance'
import {isOpenAt} from '../../trips/hooks/useSchedule'
import {monthStatus} from '../../trips/lib/season'

export interface DiscoverToggles {
  openNow: boolean
  season: boolean
  bestTime: boolean
  hideVisited: boolean
}

export interface ViewportBounds {
  north: number
  south: number
  east: number
  west: number
}

export interface DiscoverInput {
  anchor: {lat: number; lng: number} | null
  viewport: ViewportBounds | null
  category: PlaceCategory | 'all'
  toggles: DiscoverToggles
  now: Date
}

export interface DiscoverPlaceView extends DiscoverPlaceDto {
  distanceKm: number | null
  openNow: boolean | null
  seasonStatus: 'good' | 'bad' | 'none'
  bestTimeMatch: boolean | null
}

function hmsToMinutes(hms: string | null): number | null {
  if (!hms) return null
  const [h, m] = hms.split(':')
  return Number(h) * 60 + Number(m)
}

/** now ∈ [start, end)? null when the window is not fully defined. */
function bestTimeMatch(start: string | null, end: string | null, now: Date): boolean | null {
  const s = hmsToMinutes(start)
  const e = hmsToMinutes(end)
  if (s == null || e == null) return null
  const cur = now.getHours() * 60 + now.getMinutes()
  return cur >= s && cur < e
}

function inViewport(p: DiscoverPlaceDto, v: ViewportBounds): boolean {
  return p.lat <= v.north && p.lat >= v.south && p.lng >= v.west && p.lng <= v.east
}

function toView(p: DiscoverPlaceDto, input: DiscoverInput): DiscoverPlaceView {
  return {
    ...p,
    distanceKm: input.anchor ? haversineKm(input.anchor, {lat: p.lat, lng: p.lng}) : null,
    openNow: isOpenAt(p.openingHoursJson, input.now.getDay(), input.now.getHours() * 60 + input.now.getMinutes()),
    seasonStatus: monthStatus(p.seasonPeriods, input.now.getMonth()).kind,
    bestTimeMatch: bestTimeMatch(p.bestTimeStart, p.bestTimeEnd, input.now),
  }
}

function seasonRank(v: DiscoverPlaceView): number {
  return v.seasonStatus === 'good' ? 1 : 0
}

/** Compute per-place signals, apply category/viewport/toggle filters, and rank. */
export function applyDiscover(places: DiscoverPlaceDto[], input: DiscoverInput): DiscoverPlaceView[] {
  const views = places.map((p) => toView(p, input))
  const filtered = views.filter((v) => {
    if (input.category !== 'all' && v.category !== input.category) return false
    if (input.viewport && !inViewport(v, input.viewport)) return false
    if (input.toggles.openNow && v.openNow === false) return false
    if (input.toggles.season && v.seasonStatus === 'bad') return false
    if (input.toggles.hideVisited && v.visited) return false
    return true
  })
  filtered.sort((a, b) => {
    if (input.toggles.season) {
      const s = seasonRank(b) - seasonRank(a)
      if (s) return s
    }
    if (input.toggles.bestTime) {
      const t = (b.bestTimeMatch === true ? 1 : 0) - (a.bestTimeMatch === true ? 1 : 0)
      if (t) return t
    }
    const da = a.distanceKm ?? Infinity
    const db = b.distanceKm ?? Infinity
    if (da !== db) return da - db
    return a.name.localeCompare(b.name)
  })
  return filtered
}
