// Pure mapping from Maps-JS-extracted place fields → ResolvedPlaceDto (the shape
// addTripPlace already accepts). The google-specific extraction (location.lat(),
// serialising regularOpeningHours) happens in usePlaceSearch; this stays testable.
// priceLevel enum values mirror the backend GooglePlaceResolver.MapPriceLevel.
import type {PlaceCategory, ResolvedPlaceDto} from '../../../shared/api/api'
import {categorizePlace} from './placeCategory'

// Scoped field mask for Place.fetchFields — request only what the snapshot needs.
export const PLACE_DETAIL_FIELDS = [
  'id', 'displayName', 'location', 'formattedAddress', 'types', 'priceLevel', 'regularOpeningHours',
] as const

export interface RawPlaceFields {
  placeId: string | null
  name: string
  lat: number
  lng: number
  address: string | null
  types: string[]
  priceLevel: string | null   // JS-SDK PriceLevel enum string, e.g. 'MODERATE'
  openingHoursJson: string | null
}

// JS SDK Place.priceLevel enum values are 'FREE' | 'INEXPENSIVE' | 'MODERATE' |
// 'EXPENSIVE' | 'VERY_EXPENSIVE' — NOT the REST API's 'PRICE_LEVEL_*' form the
// backend GooglePlaceResolver uses. Grounded against Google's extended-component-
// library place_utils.ts (PRICE_LEVEL_CONVERSIONS).
const PRICE: Record<string, number> = {
  FREE: 0,
  INEXPENSIVE: 1,
  MODERATE: 2,
  EXPENSIVE: 3,
  VERY_EXPENSIVE: 4,
}

export function toResolvedPlace(raw: RawPlaceFields): ResolvedPlaceDto {
  const category: PlaceCategory = categorizePlace(raw.types)
  const priceLevel = raw.priceLevel != null && raw.priceLevel in PRICE ? PRICE[raw.priceLevel] : null
  return {
    googlePlaceId: raw.placeId,
    name: raw.name,
    lat: raw.lat,
    lng: raw.lng,
    address: raw.address,
    category,
    priceLevel,
    photoUrl: null,
    openingHoursJson: raw.openingHoursJson,
  }
}
