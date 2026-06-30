// frontend/src/pages/trips/hooks/useDayRoute.ts
//
// Derives the active day's ordered, time-aware itinerary as a list of map
// "route stops" (numbered pins + callout text) plus a one-line day summary.
// Shares the same RTK Query caches and useSchedule cascade as ItineraryTab,
// so the map on the right and the stop list on the left never drift apart.
import {useMemo} from 'react'
import {useGetItineraryQuery, useListTripPlacesQuery} from '../../../shared/api/api'
import type {ItineraryDayDto} from '../../../shared/api/api'
import {useAppSelector} from '../../../store/index'
import {useSchedule} from './useSchedule'

export interface RouteStop {
  id: string
  lat: number
  lng: number
  name: string
  arrival: string // "HH:MM"
  order: number // 1-based
  amber: boolean // bad-timing / closed → amber pin
}

// Always pass a real day to useSchedule so its hook count is stable (Rules of Hooks).
const EMPTY_DAY: ItineraryDayDto = {id: '', date: '', dayStartTime: '09:00:00', stops: []}

const toMin = (hhmm: string) => {
  const [h, m] = hhmm.slice(0, 5).split(':').map(Number)
  return h * 60 + (m || 0)
}

export function useDayRoute(tripId: string) {
  const activeDayId = useAppSelector((s) => s.trips.activeDayId)
  // skip on empty tripId: this hook is called before TripDetailPage's not-found
  // guard, so without skip an empty id would fire GET /api/trips//itinerary.
  const {data: days} = useGetItineraryQuery(tripId, {skip: !tripId})
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
            amber: s.flag === 'amber',
          }
        })
        .filter((r): r is RouteStop => r !== null),
    [scheduled, placesById],
  )

  const totalKm =
    scheduled.reduce((m, s) => m + (s.stop.legToReach?.meters ?? 0), 0) / 1000

  const dayStart = (day?.dayStartTime ?? '').slice(0, 5)
  const spanMin = route.length ? Math.max(0, toMin(dayEnd) - toMin(dayStart)) : 0
  const spanText =
    spanMin >= 60 ? `~${Math.floor(spanMin / 60)}ชม ${spanMin % 60}น` : `~${spanMin}น`

  const summaryText = route.length
    ? `${route.length} จุด · ${totalKm.toFixed(1)} กม · ${spanText}`
    : ''

  return {
    route,
    dayLabel: dayIndex >= 0 ? `วัน ${dayIndex + 1}` : '',
    summaryText,
  }
}
