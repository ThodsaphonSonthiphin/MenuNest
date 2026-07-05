// frontend/src/pages/trips/hooks/useDayRoute.ts
//
// Derives the active day's ordered, time-aware itinerary as a list of map
// "route stops" (numbered pins + callout text) plus a one-line day summary.
// Shares the same RTK Query caches and useSchedule cascade as ItineraryTab,
// so the map on the right and the stop list on the left never drift apart.
import {useMemo} from 'react'
import {useGetItineraryQuery, useListTripPlacesQuery} from '../../../shared/api/api'
import type {ItineraryDayDto, RouteSource} from '../../../shared/api/api'
import {useAppSelector} from '../../../store/index'
import {useSchedule} from './useSchedule'
import type {FlagSeverity} from './useSchedule'
import {flagText, severityWord} from '../timingFlag'

export interface RouteStop {
  id: string
  lat: number
  lng: number
  name: string
  arrival: string // "HH:MM"
  order: number // 1-based
  severity: FlagSeverity | null // pin colour: problem=red, suggestion=amber, null=teal
  flagNote: string | null       // reason line for the marker's accessible name
}

export interface RouteSegment {
  from: {lat: number; lng: number}
  to: {lat: number; lng: number}
  encodedPolyline: string | null
  source: RouteSource
}

export interface LegPoint {
  lat: number
  lng: number
  alive: boolean // coords finite & place resolved
  encodedPolyline: string | null // the leg that REACHES this point
  source: RouteSource
}

// Connect consecutive SURVIVING points. A segment keeps the incoming leg's real
// geometry only when its two endpoints are adjacent in the original order; a dropped
// point in between invalidates that geometry, so we render a straight Estimated line.
export function buildSegments(points: LegPoint[]): RouteSegment[] {
  const alive = points.map((p, i) => ({...p, i})).filter((p) => p.alive)
  const segs: RouteSegment[] = []
  for (let k = 1; k < alive.length; k++) {
    const a = alive[k - 1]
    const b = alive[k]
    const adjacent = b.i === a.i + 1
    segs.push({
      from: {lat: a.lat, lng: a.lng},
      to: {lat: b.lat, lng: b.lng},
      encodedPolyline: adjacent ? b.encodedPolyline : null,
      source: adjacent ? b.source : 'Estimated',
    })
  }
  return segs
}

// Always pass a real day to useSchedule so its hook count is stable (Rules of Hooks).
const EMPTY_DAY: ItineraryDayDto = {id: '', date: '', dayStartTime: '09:00:00', stops: []}

const toMin = (hhmm: string) => {
  const [h, m] = hhmm.slice(0, 5).split(':').map(Number)
  return h * 60 + (m || 0)
}

export function useDayRoute(tripId: string) {
  const activeDayId = useAppSelector((s) => s.trips.activeDayId)
  const viewerLocation = useAppSelector((s) => s.trips.viewerLocation)
  // skip on empty tripId: this hook is called before TripDetailPage's not-found
  // guard, so without skip an empty id would fire GET /api/trips//itinerary.
  const {data: days} = useGetItineraryQuery(
    {tripId, lat: viewerLocation?.lat, lng: viewerLocation?.lng},
    {skip: !tripId},
  )
  const {data: places} = useListTripPlacesQuery(tripId, {skip: !tripId})

  const dayList = days ?? []
  const dayId =
    activeDayId && dayList.some((d) => d.id === activeDayId) ? activeDayId : dayList[0]?.id
  const day = dayList.find((d) => d.id === dayId)
  const dayIndex = dayList.findIndex((d) => d.id === dayId)

  const placesById = useMemo(
    () => Object.fromEntries((places ?? []).map((p) => [p.id, p])),
    [places],
  )

  const {scheduled, dayEnd} = useSchedule(day ?? EMPTY_DAY, placesById)

  const route = useMemo<RouteStop[]>(
    () =>
      scheduled
        .map((s, i) => {
          const p = placesById[s.stop.tripPlaceId]
          // Drop stops with no place or non-finite coords — they would make the
          // map center / polyline / bounds NaN and break the map silently.
          if (!p || !Number.isFinite(p.lat) || !Number.isFinite(p.lng)) return null
          return {
            id: s.stop.id,
            lat: p.lat,
            lng: p.lng,
            name: p.name,
            arrival: s.arrival,
            order: i + 1,
            severity: s.flag?.severity ?? null,
            flagNote: s.flag ? `${flagText(s.flag).reasonLine} (${severityWord(s.flag.severity)})` : null,
          }
        })
        .filter((r): r is RouteStop => r !== null),
    [scheduled, placesById],
  )

  const segments = useMemo<RouteSegment[]>(
    () =>
      buildSegments(
        scheduled.map((s) => {
          const p = placesById[s.stop.tripPlaceId]
          const alive = !!p && Number.isFinite(p.lat) && Number.isFinite(p.lng)
          return {
            lat: alive ? p.lat : 0,
            lng: alive ? p.lng : 0,
            alive,
            encodedPolyline: s.stop.legToReach?.encodedPolyline ?? null,
            source: s.stop.legToReach?.source ?? 'Estimated', // missing source → treat as Estimated
          }
        }),
      ),
    [scheduled, placesById],
  )

  // Mirror the point-mapper's `?? 'Estimated'` rule: a present leg whose source is
  // missing/undefined (stale/partial payload) counts as Estimated too, so the summary
  // flag never disagrees with what `segments` renders. `legToReach === null` (e.g. the
  // first stop) still doesn't count.
  const anyEstimated = scheduled.some((s) => !!s.stop.legToReach && s.stop.legToReach.source !== 'Routed')

  const totalKm =
    scheduled.reduce((m, s) => m + (s.stop.legToReach?.meters ?? 0), 0) / 1000

  const dayStart = (day?.dayStartTime ?? '').slice(0, 5)
  const spanMin = route.length ? Math.max(0, toMin(dayEnd) - toMin(dayStart)) : 0
  const spanText =
    spanMin >= 60 ? `~${Math.floor(spanMin / 60)}ชม ${spanMin % 60}น` : `~${spanMin}น`

  const summaryText = route.length
    ? `${route.length} จุด · ${anyEstimated ? '~' : ''}${totalKm.toFixed(1)} กม · ${spanText}${anyEstimated ? ' · ระยะโดยประมาณ' : ''}`
    : ''

  return {
    route,
    segments,
    dayLabel: dayIndex >= 0 ? `วัน ${dayIndex + 1}` : '',
    summaryText,
  }
}

export type DayRoute = ReturnType<typeof useDayRoute>
