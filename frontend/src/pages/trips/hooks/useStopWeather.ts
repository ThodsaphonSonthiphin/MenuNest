import {useGetStopWeatherQuery} from '../../../shared/api/api'
import type {ItineraryDayDto, TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {ScheduledStopWithFlag} from './useSchedule'
import {arrivalIso, buildWeatherBatches} from '../lib/weather'

export interface StopWeather {
  now: WeatherReadingDto | undefined
  arrival: WeatherReadingDto | undefined
  nowLoading: boolean
  arrivalLoading: boolean
}

const noData = (stopId: string): WeatherReadingDto => ({
  stopId, hasData: false, conditionType: null, iconBaseUri: null, tempC: null, rainPct: null, description: null,
})

export function useStopWeather(
  day: ItineraryDayDto,
  scheduled: ScheduledStopWithFlag[],
  placesById: Record<string, TripPlaceDto>,
): Record<string, StopWeather> {
  // Recomputed on every render (NOT memoised on a captured clock) so the horizon gate is
  // re-evaluated on read — a Stop flips data<->No-data as its arrival crosses now+240h (ADR-030).
  // RTK Query collapses the batch args to a stable cache key via the endpoint's serializeQueryArgs,
  // so passing fresh arrays each render does not cause refetch churn.
  const stops = scheduled
    .map((s) => {
      const p = placesById[s.stop.tripPlaceId]
      return p ? {stopId: s.stop.id, lat: p.lat, lng: p.lng, arrivalIso: arrivalIso(day.date, s.arrival)} : null
    })
    .filter((s): s is {stopId: string; lat: number; lng: number; arrivalIso: string} => s !== null)

  const batches = buildWeatherBatches(stops, Date.now())
  const inArrivalBatch = new Set(batches.arrival.map((p) => p.stopId))

  const {data: nowData, isLoading: nowLoading} = useGetStopWeatherQuery(
    {kind: 'Now', points: batches.now},
    {skip: batches.now.length === 0},
  )
  const {data: arrData, isLoading: arrLoading} = useGetStopWeatherQuery(
    {kind: 'OnArrival', points: batches.arrival},
    {skip: batches.arrival.length === 0},
  )

  const nowById = new Map((nowData ?? []).map((r) => [r.stopId, r]))
  const arrById = new Map((arrData ?? []).map((r) => [r.stopId, r]))
  const out: Record<string, StopWeather> = {}
  for (const s of stops) {
    const gatedOut = !inArrivalBatch.has(s.stopId)
    out[s.stopId] = {
      now: nowById.get(s.stopId),
      // Past/beyond stops are gated out client-side: synthetic No-data, never "loading".
      arrival: gatedOut ? noData(s.stopId) : arrById.get(s.stopId),
      nowLoading,
      arrivalLoading: gatedOut ? false : arrLoading,
    }
  }
  return out
}
