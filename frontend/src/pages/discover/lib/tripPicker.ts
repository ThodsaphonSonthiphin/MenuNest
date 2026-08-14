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
 * Client-side name/destination filter for the picker's search box. Filtering here
 * rather than round-tripping `search` to the server keeps typing instant, and the
 * whole page (≤100) is already in memory. Blank query → everything, unchanged.
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
