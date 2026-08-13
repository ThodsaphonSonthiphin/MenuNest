import type {DiscoverPlaceDto} from '../../../shared/api/api'

/**
 * The single payload shape both Discover add paths use — AddToTripDialog (add to an
 * existing Trip) and DiscoverPage.handleCreateTrip (create-and-seed, ADR-098). Extracted
 * so they cannot drift: if only one passed originTripPlaceId, half the copies would split
 * into a second Discover card, which is the defect ADR-156 exists to prevent.
 *
 * Lives in lib/ because the SPA's vitest has no DOM harness — pure functions are the only
 * frontend logic that gets real unit coverage.
 */
export function addTripPlaceArgsFor(tripId: string, place: DiscoverPlaceDto) {
  return {
    tripId,
    googlePlaceId: place.googlePlaceId,
    name: place.name,
    lat: place.lat,
    lng: place.lng,
    address: place.address,
    category: place.category,
    priceLevel: place.priceLevel,
    photoUrl: place.photoUrl,
    openingHoursJson: place.openingHoursJson,
    originTripPlaceId: place.originTripPlaceId,
    notes: place.notes,
    reviewLinks: place.reviewLinks,
    bestTimeWindows: place.bestTimeWindows,
    seasonPeriods: place.seasonPeriods,
  }
}
