// Pure lookup: Google Places (New) place `types` → MenuNest PlaceCategory.
// First matching rule wins; unknown/empty → 'Other'. See
// docs/adr/016-map-centric-add-place-ux.md (Decision item 4) and the spec's
// §5.1 category table. `types` vocabulary: Places API (New) type tables.
import type {PlaceCategory} from '../../../shared/api/api'

// Ordered: earlier entries take precedence when a place carries several types.
const RULES: ReadonlyArray<readonly [PlaceCategory, ReadonlySet<string>]> = [
  ['Cafe', new Set(['cafe', 'coffee_shop'])],
  ['Eat', new Set(['restaurant', 'food', 'meal_takeaway', 'meal_delivery', 'bakery', 'bar'])],
  ['Stay', new Set(['lodging', 'hotel', 'resort_hotel', 'guest_house', 'motel', 'bed_and_breakfast'])],
  ['See', new Set(['tourist_attraction', 'museum', 'place_of_worship', 'park', 'landmark', 'art_gallery', 'zoo', 'national_park'])],
  ['Shop', new Set(['store', 'shopping_mall', 'market', 'department_store', 'supermarket', 'convenience_store'])],
]

export function categorizePlace(types: string[] | null | undefined): PlaceCategory {
  if (!types || types.length === 0) return 'Other'
  for (const t of types) {
    for (const [category, set] of RULES) {
      if (set.has(t)) return category
    }
  }
  return 'Other'
}
