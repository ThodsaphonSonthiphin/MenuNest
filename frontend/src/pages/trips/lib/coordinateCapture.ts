// frontend/src/pages/trips/lib/coordinateCapture.ts
// The coordinate capture input: a place whose identity IS the point the user tapped.
// Lives in lib/ because the SPA's vitest has no DOM harness — pure functions are the
// only frontend logic that gets real unit coverage (CLAUDE.md).
import type {ResolvedPlaceDto} from '../../../shared/api/api'

/**
 * Build the place an empty-ground tap captures (ADR-164 §2: the tap opens the
 * coordinate input prefilled with that lat/lng).
 *
 * Deliberately makes NO Geocoding call, so there is nothing to prefill but the point
 * itself — spec R8.2 puts "empty ground" at "no Geocoding call" and R7.3 puts its full
 * cost at $0. The user supplies the name (R4.1); the address stays optional (R4.4).
 */
export function coordinatePlaceFrom(lat: number, lng: number): ResolvedPlaceDto {
  return {
    googlePlaceId: null,
    name: '',
    lat,
    lng,
    address: null,
    category: 'Other',
    priceLevel: null,
    photoUrl: null,
    openingHoursJson: null,
  }
}

/**
 * Whether the capture form must offer an editable name. True exactly when Google did
 * not identify the place — every resolver path (search, POI tap, link) returns a
 * `place_id` and a display name, so a missing `place_id` means the coordinate input.
 */
export function needsTypedName(place: ResolvedPlaceDto): boolean {
  return place.googlePlaceId === null
}

/** R4.1: there is no "save without a name". */
export function captureNameValid(name: string): boolean {
  return name.trim().length > 0
}
