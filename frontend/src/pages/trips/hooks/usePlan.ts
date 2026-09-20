// frontend/src/pages/trips/hooks/usePlan.ts
//
// The active Day's plan, in one place. This is `ItineraryTab`'s data cascade hoisted out of
// it (spec §7) so the Plan sheet's header, the Plan sheet's body and the map all read the
// SAME projection — the Compact stop card in particular must not re-derive a Stop's times
// from scratch (spec §6.3).
//
// It shares RTK Query's caches with `useDayRoute`, so calling both costs no extra request.
import {useMemo} from 'react'
import {useGetItineraryQuery, useListTripPlacesQuery} from '../../../shared/api/api'
import type {ItineraryDayDto, TripDto, TripPlaceDto} from '../../../shared/api/api'
import {useAppSelector} from '../../../store/index'
import {useSchedule} from './useSchedule'
import {useStopWeather} from './useStopWeather'
import {buildDayNavUrl, getWaypointCap} from '../lib/navUrl'
import {monthOfDate} from '../lib/season'
import {getViewerTimeZone} from '../utils/time'

// Used as a fallback so useSchedule is ALWAYS called unconditionally (Rules of Hooks: the
// hook count must be identical on every render, including the loading and error renders).
const EMPTY_DAY: ItineraryDayDto = {
  id: '', date: '', dayStartTime: '09:00:00', useCurrentTimeAsStart: false, stops: [],
}

/**
 * `trip` comes from the page's own `useGetTripQuery` rather than a second `useListTripsQuery`
 * here — `ItineraryTab` used to list every Trip just to read this one's default travel mode.
 */
export function usePlan(tripId: string, trip: TripDto | undefined) {
  const activeDayId = useAppSelector((s) => s.trips.activeDayId)
  const viewerLocation = useAppSelector((s) => s.trips.viewerLocation)

  const {
    data: days,
    isLoading,
    error,
    refetch,
  } = useGetItineraryQuery(
    {tripId, tz: getViewerTimeZone(), lat: viewerLocation?.lat, lng: viewerLocation?.lng},
    {skip: !tripId},
  )
  const {data: places} = useListTripPlacesQuery(tripId, {skip: !tripId})

  const dayList = useMemo(() => days ?? [], [days])
  // An activeDayId the itinerary does not contain (a deleted Day, a Day from another Trip)
  // falls back to the first Day rather than rendering an empty plan.
  const dayId = activeDayId && dayList.some((d) => d.id === activeDayId) ? activeDayId : dayList[0]?.id ?? null
  const day = dayList.find((d) => d.id === dayId)
  const dayIndex = dayList.findIndex((d) => d.id === dayId)

  const placesById = useMemo(
    () => Object.fromEntries((places ?? []).map((p) => [p.id, p])) as Record<string, TripPlaceDto>,
    [places],
  )

  const {scheduled, dayEnd, totalTravelSeconds, remainingTravelSeconds} = useSchedule(day ?? EMPTY_DAY, placesById)
  const stopWeather = useStopWeather(day ?? EMPTY_DAY, scheduled, placesById)

  const dayMode = trip?.defaultTravelMode ?? 'Drive'

  const remaining = useMemo(() => scheduled.filter((s) => !s.stop.isVisited), [scheduled])
  const done = useMemo(() => scheduled.filter((s) => s.stop.isVisited), [scheduled])

  const cap = useMemo(() => getWaypointCap(), [])
  // Every scheduled Stop, visited included — unchanged from ItineraryTab: the day's
  // navigation link is the day's whole route, not just what is left of it.
  const navPoints = scheduled
    .map((s) => placesById[s.stop.tripPlaceId])
    .filter((p): p is TripPlaceDto => !!p)
    .map((p) => ({lat: p.lat, lng: p.lng, placeId: p.googlePlaceId}))
  const dayNav = buildDayNavUrl(navPoints, cap, dayMode)
  const mixedMode = scheduled.slice(1).some((s) => s.stop.travelModeToReach !== dayMode)

  return {
    dayList,
    dayId,
    day,
    /** 1-based number of the active Day, or 0 while the itinerary is still loading. */
    dayNumber: dayIndex + 1,
    /** The active Day's month, for the season treatment. '' date → 0, same as today. */
    tripMonth: day ? monthOfDate(day.date) : 0,
    scheduled,
    remaining,
    done,
    dayEnd,
    totalTravelSeconds,
    remainingTravelSeconds,
    placesById,
    places: places ?? [],
    stopWeather,
    dayNav,
    dayMode,
    mixedMode,
    navPointCount: navPoints.length,
    isLoading,
    error,
    refetch,
  }
}

export type Plan = ReturnType<typeof usePlan>
