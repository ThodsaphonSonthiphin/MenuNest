// frontend/src/pages/discover/lib/categoryStyle.ts
//
// The single category → colour/label table for the Discover screen, so a map pin,
// its list row and the detail sheet can never disagree about what a category looks
// like. Previously the colour lived only inside DiscoverMap.tsx and the list rows
// had no colour at all.
//
// Palette note: docs/mocks/place-discovery-mock.html carries its own --cat-* values
// but labels them "category colours (illustrative)" in its own CSS comment. The
// values below are the app-wide ones already shipped by TripMap, and cross-screen
// consistency with the Trips map beats matching an illustrative swatch — a place
// must not change colour when the user moves between Trips and Discover.

/** Category → pin/badge colour (mirrors TripMap's CAT_COLOR). */
export const CAT_COLOR: Record<string, string> = {
  Stay: '#6d5ae6',
  Eat: '#e2553e',
  See: '#1f9d76',
  Cafe: '#b4791f',
  Shop: '#c2418f',
  Other: '#0e8f9e',
}

/** Category → Thai label shown in list rows and filter chips. */
export const CAT_LABEL: Record<string, string> = {
  See: 'เที่ยว',
  Eat: 'กิน',
  Cafe: 'คาเฟ่',
  Stay: 'ที่พัก',
  Shop: 'ช้อป',
  Other: 'อื่น ๆ',
}

export function catColor(category: string): string {
  return CAT_COLOR[category] ?? CAT_COLOR.Other
}

export function catLabel(category: string): string {
  return CAT_LABEL[category] ?? category
}
