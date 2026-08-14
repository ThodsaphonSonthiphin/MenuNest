// frontend/src/pages/discover/lib/tripPicker.ts
//
// Pure helpers for the Discover "เพิ่มเข้าทริป" picker.
//
// Why the query args live here as a constant: AddToTripDialog previously called
// useListTripsQuery() with NO arguments, so the request went out as a bare
// GET /api/trips and the server applied its own defaults — ListTripsQuery.cs's
// `int Take = 10` plus ListTripsHandler's fallback `OrderBy(t => t.StartDate)`
// (ascending). On the live account that returned 10 of 20 trips, and because the
// sort is ascending the 10 that were dropped were the NEWEST ones — i.e. exactly
// the trips a user is most likely adding a place to. Spec R1.5.
//
// The SPA's vitest has no DOM harness (vite.config.ts → environment: 'node'), so
// the filter/label logic lives here to get real unit coverage.
import type {TripDto} from '../../../shared/api/api'
import {ymdToDate, thaiDate} from '../../trips/utils/date'

/** Mock frame 7's "100 ทริปที่แก้ล่าสุด". */
export const TRIP_PICKER_PAGE_SIZE = 100

/** Below this length a query round-trip is not worth it; the client filter covers it. */
export const TRIP_SEARCH_MIN_CHARS = 2

/**
 * Newest-first, one page deep. The backend already supports every one of these
 * (ListTripsQuery: Skip/Take/Search/SortColumn/SortDirection) — no API change.
 */
export const TRIP_PICKER_QUERY_ARGS = {
  take: TRIP_PICKER_PAGE_SIZE,
  sortColumn: 'startDate',
  sortDirection: 'Descending',
} as const

/**
 * The args for one keystroke's worth of picker state.
 *
 * The page cap is real: a user past 100 trips cannot see #101+, and a purely
 * client-side filter would make the search box unable to reach them either —
 * which is the same "the picker silently hides your trips" defect as the old
 * Take=10 default, just at a higher ceiling. So a query of at least
 * TRIP_SEARCH_MIN_CHARS is handed to the server, which searches Name and
 * Destination across the whole library rather than the loaded page.
 */
export function tripPickerArgs(query: string) {
  const q = query.trim()
  return q.length >= TRIP_SEARCH_MIN_CHARS
    ? {...TRIP_PICKER_QUERY_ARGS, search: q}
    : {...TRIP_PICKER_QUERY_ARGS}
}

/**
 * Client-side name/destination filter, still applied on top of whatever the server
 * returned: it narrows the visible list on the very first keystroke, before the
 * debounced request has gone out, so typing never feels like it stalls. Once the
 * server response lands the two agree. Blank query → everything, unchanged.
 */
export function filterTrips(trips: readonly TripDto[], query: string): TripDto[] {
  const q = query.trim().toLowerCase()
  if (!q) return [...trips]
  return trips.filter((t) => {
    const name = t.name.toLowerCase()
    const dest = (t.destination ?? '').toLowerCase()
    return name.includes(q) || dest.includes(q)
  })
}

/**
 * Secondary line under a trip name in the picker: destination, when it starts, and
 * how long. A daily trip has no meaningful start date (it re-bases on today, #49),
 * so it reads "ทุกวัน" instead.
 */
export function tripSubtitle(trip: TripDto): string {
  const parts: string[] = []
  if (trip.destination) parts.push(trip.destination)
  if (trip.isDaily) {
    parts.push('ทุกวัน')
  } else {
    const start = ymdToDate(trip.startDate)
    if (start) parts.push(thaiDate(start))
  }
  if (trip.dayCount > 1) parts.push(`${trip.dayCount} วัน`)
  return parts.join(' · ')
}
