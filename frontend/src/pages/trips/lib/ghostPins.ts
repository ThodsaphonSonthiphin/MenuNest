// frontend/src/pages/trips/lib/ghostPins.ts
//
// Ghost pins (menunest-235 / menunest-241): the **Places** on this Trip that are NOT a
// **Stop** on the active **Day**, drawn faintly on the same map as the route. Pure, so the
// partitioning and the label-density rule are unit-testable — the repo has no component
// harness, so anything decided inside a component is decided untested (CLAUDE.md).
//
// `useGetItineraryQuery` returns ALL Days with their Stops, so "is this Place scheduled on
// another Day?" is answerable client-side with no new endpoint.
import type {ItineraryDayDto, PlaceCategory, TripPlaceDto} from '../../../shared/api/api'

export interface GhostPin {
  /** The TripPlace id — stable, and what the add-to-day action commits against. */
  id: string
  name: string
  lat: number
  lng: number
  category: PlaceCategory
  /**
   * The 1-based Day this Place IS scheduled on, or null when it is on no Day at all.
   * Carried here so §6.1's two cards (`เพิ่มเข้าวัน N` vs `อยู่ในวัน M` / `ไปวัน M`) can be
   * rendered without a second pass over the itinerary.
   */
  scheduledDayNumber: number | null
  /** The Day id behind `scheduledDayNumber` — what `ไปวัน M` switches the active Day to. */
  scheduledDayId: string | null
}

/**
 * Above this many ghost pins, labels are dropped and only dots render (menunest-241) —
 * otherwise a Place-heavy Trip buries its own route under text.
 */
export const GHOST_LABEL_LIMIT = 12

/** Ghost pins keep their labels only while the map is not crowded. */
export function showGhostLabels(count: number): boolean {
  return count <= GHOST_LABEL_LIMIT
}

/**
 * Every Place on the Trip that is not a Stop on the active Day.
 *
 * Visited Stops still count as scheduled: `useDayRoute` drops them from the ROUTE, but a
 * Place the user has already visited today is not "unplanned", and re-drawing it as a ghost
 * would invite adding it twice.
 *
 * Non-finite coordinates are dropped for the same reason `useDayRoute` drops them — they
 * would make the map's bounds NaN and break the whole map silently.
 */
export function buildGhostPins(
  places: TripPlaceDto[],
  days: ItineraryDayDto[],
  activeDayId: string | null,
): GhostPin[] {
  const onActiveDay = new Set<string>()
  // First Day (in itinerary order) that schedules each Place, for the `อยู่ในวัน M` card.
  const scheduledOn = new Map<string, {dayNumber: number; dayId: string}>()

  days.forEach((day, i) => {
    for (const stop of day.stops) {
      if (day.id === activeDayId) onActiveDay.add(stop.tripPlaceId)
      if (!scheduledOn.has(stop.tripPlaceId)) {
        scheduledOn.set(stop.tripPlaceId, {dayNumber: i + 1, dayId: day.id})
      }
    }
  })

  return places
    .filter((p) => !onActiveDay.has(p.id) && Number.isFinite(p.lat) && Number.isFinite(p.lng))
    .map((p) => {
      const where = scheduledOn.get(p.id) ?? null
      return {
        id: p.id,
        name: p.name,
        lat: p.lat,
        lng: p.lng,
        category: p.category,
        scheduledDayNumber: where?.dayNumber ?? null,
        scheduledDayId: where?.dayId ?? null,
      }
    })
}

// ── The per-User ghost toggle (spec §11.3: localStorage, not per Trip, not server-side) ──

const GHOST_PREF_KEY = 'menunest.trips.ghostPins'

/**
 * Ghost pins are drawn BY DEFAULT (menunest-241), so every read failure — private mode, a
 * blocked store, a corrupt value — falls back to shown rather than to a map that silently
 * lost its library.
 */
export function readGhostPref(): boolean {
  try {
    return localStorage.getItem(GHOST_PREF_KEY) !== 'off'
  } catch {
    return true
  }
}

export function writeGhostPref(visible: boolean): void {
  try {
    localStorage.setItem(GHOST_PREF_KEY, visible ? 'on' : 'off')
  } catch {
    /* a blocked store must not break the toggle — the choice just doesn't survive the visit */
  }
}
