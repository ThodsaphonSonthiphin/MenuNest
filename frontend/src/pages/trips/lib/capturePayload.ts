// frontend/src/pages/trips/lib/capturePayload.ts
// The one shape a finished Capture hands to whoever commits it.
//
// ADR-163 §3 / TC-910: the capture component takes a commit TARGET, not a tripId.
// That only works if the thing it hands over is Trip-free — so the payload below
// carries the place and the user's edits, and nothing about where it lands. The
// Trips surface turns it into an addTripPlace call for a Trip it already knows;
// Discover turns it into the same call for a Trip picked (or created) at commit
// time, because nothing is ever saved trip-less (ADR-155).
//
// Lives in lib/ because the SPA's vitest runs in environment:'node' with no DOM
// harness (CLAUDE.md) — pure functions are the only frontend logic that gets real
// unit coverage.
import type {PlaceCategory, ResolvedPlaceDto} from '../../../shared/api/api'
import type {ReviewLink} from '../../../shared/api/api'
import {sanitizeReviewDrafts, type ReviewDraft} from './reviewLinks'

export interface CapturePayload {
  googlePlaceId: string | null
  name: string
  lat: number
  lng: number
  address: string | null
  category: PlaceCategory
  priceLevel: number | null
  photoUrl: string | null
  openingHoursJson: string | null
  reviewLinks: ReviewLink[]
}

/**
 * Build the payload from the resolved place plus the three things the preview form
 * lets the user change: the name, the category and the review links.
 *
 * The name is trimmed here rather than at each call site so every commit path stores
 * the same string — a coordinate capture's name is typed by hand (R4.1) and is the
 * field most likely to arrive with stray whitespace.
 */
export function capturePayloadFrom(
  place: ResolvedPlaceDto,
  name: string,
  category: PlaceCategory,
  reviewDrafts: ReviewDraft[],
): CapturePayload {
  return {
    googlePlaceId: place.googlePlaceId,
    name: name.trim(),
    lat: place.lat,
    lng: place.lng,
    address: place.address,
    category,
    priceLevel: place.priceLevel,
    photoUrl: place.photoUrl,
    openingHoursJson: place.openingHoursJson,
    reviewLinks: sanitizeReviewDrafts(reviewDrafts),
  }
}

/**
 * Turn a payload into addTripPlace arguments for one Trip.
 *
 * Deliberately does NOT pass originTripPlaceId: a Capture is a place entering the
 * Place library for the first time, so it has no origin root to flatten. Copying an
 * EXISTING library Place into another Trip is a different action and uses
 * discover/lib/originPassthrough.addTripPlaceArgsFor, which does pass it (ADR-156).
 */
export function addTripPlaceArgsForCapture(tripId: string, payload: CapturePayload) {
  return {tripId, ...payload}
}

/**
 * The name a Trip takes when a Capture creates one in a single tap (ADR-098, ADR-155:
 * "สร้างทริปใหม่" seeds the captured Place as the new Trip's first Place). Matches what
 * DiscoverPage.handleCreateTrip already does for an existing library Place, so a Trip
 * created from a capture and one created from a Discover card are named the same way.
 */
export function newTripNameFor(payload: CapturePayload): string {
  return payload.name
}
