// frontend/src/pages/trips/lib/tripEdit.ts
//
// Pure logic behind EditTripDialog (issue #50). Kept out of the component on purpose:
// the SPA's vitest runs in environment:'node' with no jsdom, so a component's rendering
// is untestable here — but the draft diffing and the at-risk-stop arithmetic, which are
// the parts that can silently destroy data, are not.
import type {ItineraryDayDto, TravelMode, TripDto} from '../../../shared/api/api'

/** The five fields the edit form stages. Mirrors what PUT /api/trips/{id} carries. */
export interface TripEditDraft {
  name: string
  destination: string // '' rather than null — a text input's empty value
  startDate: string // "yyyy-MM-dd"
  dayCount: number
  defaultTravelMode: TravelMode
}

export function draftFromTrip(trip: TripDto): TripEditDraft {
  return {
    name: trip.name,
    destination: trip.destination ?? '',
    startDate: trip.startDate.slice(0, 10),
    dayCount: trip.dayCount,
    defaultTravelMode: trip.defaultTravelMode,
  }
}

/** Trim the free-text fields exactly as the server does — Trip.UpdateDetails trims both. */
export function normalizeDraft(d: TripEditDraft): TripEditDraft {
  return {...d, name: d.name.trim(), destination: d.destination.trim()}
}

/** '' / '   ' / null all mean "no destination"; Trip.Destination stores null. */
function normDest(v: string | null | undefined): string | null {
  const t = (v ?? '').trim()
  return t.length ? t : null
}

/**
 * True when the draft differs from the trip in any field the PUT carries.
 *
 * A `false` here means the save must issue NO PUT at all (ADR-141): updateTrip invalidates
 * {type:'TripItinerary', id} on EVERY call, and a getItinerary refetch re-bills the Google
 * Routes API and re-fetches Weather. A no-op save would be a cost this feature newly introduces.
 */
export function isDraftDirty(d: TripEditDraft, trip: TripDto): boolean {
  return (
    d.name.trim() !== trip.name ||
    normDest(d.destination) !== normDest(trip.destination) ||
    d.startDate !== trip.startDate.slice(0, 10) ||
    d.dayCount !== trip.dayCount ||
    d.defaultTravelMode !== trip.defaultTravelMode
  )
}

export interface ShrinkLossStop {
  name: string
  isVisited: boolean
}

export interface ShrinkLoss {
  dayFrom: number // 1-based number of the first day that goes
  dayTo: number // 1-based number of the last day that goes
  dateFrom: string // "yyyy-MM-dd"
  dateTo: string
  stops: ShrinkLossStop[]
}

/**
 * What shrinking to `newDayCount` would destroy — or null when nothing is at risk.
 *
 * Null covers three cases that all mean "save straight through": the itinerary is not
 * loaded, this is not a shrink, or the dropped days are empty. ADR-138 fires the confirm
 * only on real loss; a red modal on a harmless 5 -> 3 over empty days trains tap-through
 * and costs the signal on the shrink that destroys six stops.
 *
 * The drop set is taken BY INDEX, never by date: GetItineraryHandler projects a single-day
 * current-time-start trip's date to the viewer's today, so date matching is unsafe.
 */
export function shrinkLoss(
  days: ItineraryDayDto[],
  placeNameById: Record<string, string>,
  newDayCount: number,
): ShrinkLoss | null {
  if (days.length === 0 || newDayCount >= days.length) return null
  const dropped = days.slice(newDayCount)
  const stops: ShrinkLossStop[] = dropped.flatMap((d) =>
    d.stops.map((s) => ({
      name: placeNameById[s.tripPlaceId] ?? 'สถานที่',
      isVisited: s.isVisited,
    })),
  )
  if (stops.length === 0) return null
  return {
    dayFrom: newDayCount + 1,
    dayTo: days.length,
    dateFrom: dropped[0].date.slice(0, 10),
    dateTo: dropped[dropped.length - 1].date.slice(0, 10),
    stops,
  }
}

/** Cap a list for the 420px confirm dialog: the first `max`, plus an overflow count. */
export function capNames<T>(items: T[], max = 5): {shown: T[]; moreCount: number} {
  return {shown: items.slice(0, max), moreCount: Math.max(0, items.length - max)}
}

/** Total stops across the cached itinerary — the "M จุดแวะ" in the delete confirm. */
export function totalStops(days: ItineraryDayDto[]): number {
  return days.reduce((n, d) => n + d.stops.length, 0)
}
