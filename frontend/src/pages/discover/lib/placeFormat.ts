// frontend/src/pages/discover/lib/placeFormat.ts
//
// Shared display formatting for the Discover screen. `distanceLabel` lived twice —
// once in PlaceBottomSheet (".28 ม.") and once in PlaceSheet ("28 ม. จากคุณ") —
// same name, sibling files, different output. That is the drift categoryStyle.ts
// exists to prevent, so the suffix is a parameter rather than a second copy.
//
// vitest runs in environment: 'node' with no DOM harness, so formatting logic only
// gets real coverage when it lives in lib/.

/**
 * Metres under a kilometre, kilometres to one decimal above it — the app-wide
 * convention (TravelLeg renders legs the same way).
 *
 * @param suffix appended after the unit when the caller wants to say what the
 *   distance is measured from, e.g. "จากคุณ" on the detail sheet. Omitted in the
 *   ranked list, where the whole list is already "เรียงตามระยะ".
 */
export function distanceLabel(km: number | null | undefined, suffix = ''): string {
  if (km == null || Number.isNaN(km)) return ''
  const core = km < 1 ? `${Math.round(km * 1000)} ม.` : `${km.toFixed(1)} กม.`
  return suffix ? `${core} ${suffix}` : core
}
