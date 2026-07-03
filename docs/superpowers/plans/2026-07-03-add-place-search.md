# Add-Place "Search Like Google" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user add a Place to a Trip by typing a name for live Google Places suggestions, or by tapping a POI on the in-app map — a map-centric add-mode that replaces the paste-a-link modal (link paste kept as a hidden fallback).

**Architecture:** Frontend-only. Autocomplete and place-detail fetch run client-side on the Maps JS `places` library (via `@vis.gl/react-google-maps`) with the existing referrer-restricted browser key; the assembled snapshot is POSTed to the **unchanged** `addTripPlace` endpoint. Pure logic (category guess, snapshot mapping, add-mode reducer) is unit-tested with Vitest; Maps-touching React components are typecheck- and manual-verified against the confirmed mock (the repo has no React component test harness — pure-logic-in-Vitest / UI-in-Playwright is the established pattern).

**Tech Stack:** React 19, Redux Toolkit + RTK Query, `@vis.gl/react-google-maps` ^1.8.3, Syncfusion react-* 33.1.44, Vitest 4, TypeScript ~6.

**Spec:** [docs/superpowers/specs/2026-07-03-add-place-search-design.md](../specs/2026-07-03-add-place-search-design.md)
**ADRs:** [014](../../adr/014-add-place-entry-paths-search-and-map-tap.md) · [015](../../adr/015-client-side-autocomplete-and-place-details.md) · [016](../../adr/016-map-centric-add-place-ux.md)
**Mock (source of truth for UI):** [docs/mocks/trip-add-place-search-mock.html](../../mocks/trip-add-place-search-mock.html)

## Global Constraints

- **No backend change.** Reuse `POST /api/trips/{id}/places` (`useAddTripPlaceMutation`) and `POST /api/trips/resolve-place` (`useResolvePlaceMutation`) exactly as they are today. Do not touch `MenuNest.WebApi` / Infrastructure.
- **`PlaceCategory`** is exactly `'Stay' | 'Eat' | 'See' | 'Cafe' | 'Shop' | 'Other'` (`frontend/src/shared/api/api.ts:492`).
- **`ResolvedPlaceDto`** shape (api.ts:504): `{ googlePlaceId: string|null; name: string; lat: number; lng: number; address: string|null; category: PlaceCategory; priceLevel: number|null; photoUrl: string|null; openingHoursJson: string|null }`.
- **`addTripPlace` body** = `{ tripId } & Omit<TripPlaceDto,'id'|'tripId'|'bestTimeStart'|'bestTimeEnd'|'feeNote'|'notes'>` → send `{ tripId, googlePlaceId, name, lat, lng, address, category, priceLevel, photoUrl, openingHoursJson }`.
- **Client-side Maps only** for search/tap (ADR-015). API key never widened server-side. Browser key must have **Places API (New)** enabled; dev uses the Demo Key.
- **No emoji in chrome** (user rule): categories render as a colour dot (per-category colour from `TripMap.tsx` `CAT_COLOR`) + Thai label; icons are inline SVG.
- **Teal design tokens** from `frontend/src/pages/trips/trips-tokens.css` (`--teal #0e8f9e`, `--teal-soft #e3f5f6`, category colours). Match the confirmed mock.
- **Cost guards:** debounce ≈300 ms, min input length ≥ 2 chars, one `AutocompleteSessionToken` per search session, scoped `fetchFields` field mask.
- **ToS / attribution:** keep `internalUsageAttributionIds={['gmp_git_agentskills_v1']}` on the map; ground Places JS calls against the `google-maps-platform` skill and run its `compliance-review` before the final commit (ADR-007).
- **UI copy is Thai** (matches the existing trips UI).
- Tests run with `cd frontend && npm run test` (Vitest); typecheck/build with `cd frontend && npm run build`.

---

## File Structure

**Create:**
- `frontend/src/pages/trips/lib/placeCategory.ts` — `categorizePlace(types): PlaceCategory` (pure).
- `frontend/src/pages/trips/lib/placeCategory.test.ts`
- `frontend/src/pages/trips/lib/placeSnapshot.ts` — `PLACE_DETAIL_FIELDS`, `toResolvedPlace(raw): ResolvedPlaceDto` (pure).
- `frontend/src/pages/trips/lib/placeSnapshot.test.ts`
- `frontend/src/pages/trips/hooks/usePlaceSearch.ts` — Maps JS glue: autocomplete + session token + viewport bias + `fetchDetails(placeId)`.
- `frontend/src/pages/trips/components/AddPlacePreviewCard.tsx` — presentational preview (name, address, category control, buttons).
- `frontend/src/pages/trips/components/AddPlaceSearchBar.tsx` — floating search bar + suggestions dropdown + "วางลิงก์" fallback trigger.
- `frontend/src/pages/trips/components/PlaceLinkFallbackDialog.tsx` — small dialog wrapping the existing `resolvePlace` (extracted from the old sheet).
- `frontend/src/pages/trips/components/AddPlaceMode.tsx` — orchestrator rendered inside `<Map>`: temp pin, wires search + tap + preview, calls `addTripPlace`, stays armed.

**Modify:**
- `frontend/src/pages/trips/tripsSlice.ts` — replace `addPlaceOpen` with `addMode` + `setAddMode`.
- `frontend/src/pages/trips/components/TripMap.tsx` — accept `addMode` + `tripId`; render `<AddPlaceMode>` and enable POI clicks when armed.
- `frontend/src/pages/trips/TripDetailPage.tsx` — arm/disarm add-mode from the toolbar; pass props to `TripMap`; force map view on mobile when arming; drop `AddPlaceSheet`.
- `frontend/src/pages/trips/trips-tokens.css` — add-mode styles (search bar, suggestions, temp pin, preview card / bottom sheet).

**Delete:**
- `frontend/src/pages/trips/components/AddPlaceSheet.tsx` (superseded; its paste-resolve logic lives on in `PlaceLinkFallbackDialog`).

---

### Task 1: `categorizePlace` — Google types → PlaceCategory (pure)

**Files:**
- Create: `frontend/src/pages/trips/lib/placeCategory.ts`
- Test: `frontend/src/pages/trips/lib/placeCategory.test.ts`

**Interfaces:**
- Consumes: `PlaceCategory` from `../../../shared/api/api`.
- Produces: `categorizePlace(types: string[] | null | undefined): PlaceCategory`.

- [ ] **Step 1: Write the failing test**

```ts
// frontend/src/pages/trips/lib/placeCategory.test.ts
import {describe, it, expect} from 'vitest'
import {categorizePlace} from './placeCategory'

describe('categorizePlace', () => {
  it('maps food types to Eat', () => {
    expect(categorizePlace(['restaurant'])).toBe('Eat')
    expect(categorizePlace(['bakery', 'store'])).toBe('Eat') // first match wins
  })
  it('maps cafe/coffee_shop to Cafe', () => {
    expect(categorizePlace(['coffee_shop'])).toBe('Cafe')
    expect(categorizePlace(['cafe'])).toBe('Cafe')
  })
  it('maps lodging types to Stay', () => {
    expect(categorizePlace(['lodging'])).toBe('Stay')
    expect(categorizePlace(['resort_hotel'])).toBe('Stay')
  })
  it('maps sightseeing types to See', () => {
    expect(categorizePlace(['tourist_attraction'])).toBe('See')
    expect(categorizePlace(['place_of_worship'])).toBe('See')
    expect(categorizePlace(['museum'])).toBe('See')
  })
  it('maps retail types to Shop', () => {
    expect(categorizePlace(['shopping_mall'])).toBe('Shop')
    expect(categorizePlace(['store'])).toBe('Shop')
  })
  it('falls back to Other for unknown / empty / null', () => {
    expect(categorizePlace(['premise'])).toBe('Other')
    expect(categorizePlace([])).toBe('Other')
    expect(categorizePlace(null)).toBe('Other')
    expect(categorizePlace(undefined)).toBe('Other')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/lib/placeCategory.test.ts`
Expected: FAIL — `Failed to resolve import "./placeCategory"` / `categorizePlace is not a function`.

- [ ] **Step 3: Write minimal implementation**

```ts
// frontend/src/pages/trips/lib/placeCategory.ts
// Pure lookup: Google Places (New) place `types` → MenuNest PlaceCategory.
// First matching rule wins; unknown/empty → 'Other'. See ADR-016 §4 and the
// spec's category table. `types` vocabulary: Places API (New) type tables.
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run src/pages/trips/lib/placeCategory.test.ts`
Expected: PASS (6 tests).

> Note the test `['bakery','store'] → 'Eat'`: `bakery` (Eat) is checked before `store` (Shop) because the outer loop walks `types` in order and `bakery` appears first. Keep this test — it locks the "first type wins" contract.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/placeCategory.ts frontend/src/pages/trips/lib/placeCategory.test.ts
git commit -m "feat(trips): categorizePlace — Google types → PlaceCategory"
```

---

### Task 2: `toResolvedPlace` + `PLACE_DETAIL_FIELDS` — snapshot mapper (pure)

**Files:**
- Create: `frontend/src/pages/trips/lib/placeSnapshot.ts`
- Test: `frontend/src/pages/trips/lib/placeSnapshot.test.ts`

**Interfaces:**
- Consumes: `categorizePlace` (Task 1); `ResolvedPlaceDto`, `PlaceCategory` from `../../../shared/api/api`.
- Produces:
  - `PLACE_DETAIL_FIELDS: string[]` — the scoped field mask for `Place.fetchFields`.
  - `RawPlaceFields` interface (plain, google-free) — what `usePlaceSearch` extracts from a Maps JS `Place`.
  - `toResolvedPlace(raw: RawPlaceFields): ResolvedPlaceDto`.

- [ ] **Step 1: Write the failing test**

```ts
// frontend/src/pages/trips/lib/placeSnapshot.test.ts
import {describe, it, expect} from 'vitest'
import {toResolvedPlace, PLACE_DETAIL_FIELDS} from './placeSnapshot'

const base = {
  placeId: 'ChIJxyz', name: 'Ristr8to Coffee', lat: 18.8, lng: 98.97,
  address: 'Nimman Rd', types: ['coffee_shop'], priceLevel: 'MODERATE',
  openingHoursJson: '{"weekdayDescriptions":["Mon: 8AM-6PM"]}',
}

describe('PLACE_DETAIL_FIELDS', () => {
  it('requests only the fields the snapshot needs', () => {
    expect(PLACE_DETAIL_FIELDS).toEqual(
      ['id', 'displayName', 'location', 'formattedAddress', 'types', 'priceLevel', 'regularOpeningHours'],
    )
  })
})

describe('toResolvedPlace', () => {
  it('maps fields and derives category from types', () => {
    const r = toResolvedPlace(base)
    expect(r.googlePlaceId).toBe('ChIJxyz')
    expect(r.name).toBe('Ristr8to Coffee')
    expect(r.lat).toBe(18.8)
    expect(r.lng).toBe(98.97)
    expect(r.address).toBe('Nimman Rd')
    expect(r.category).toBe('Cafe')
    expect(r.openingHoursJson).toBe('{"weekdayDescriptions":["Mon: 8AM-6PM"]}')
    expect(r.photoUrl).toBeNull()
  })
  it('converts the JS-SDK priceLevel enum to an int', () => {
    expect(toResolvedPlace({...base, priceLevel: 'FREE'}).priceLevel).toBe(0)
    expect(toResolvedPlace({...base, priceLevel: 'INEXPENSIVE'}).priceLevel).toBe(1)
    expect(toResolvedPlace({...base, priceLevel: 'MODERATE'}).priceLevel).toBe(2)
    expect(toResolvedPlace({...base, priceLevel: 'EXPENSIVE'}).priceLevel).toBe(3)
    expect(toResolvedPlace({...base, priceLevel: 'VERY_EXPENSIVE'}).priceLevel).toBe(4)
    expect(toResolvedPlace({...base, priceLevel: null}).priceLevel).toBeNull()
    expect(toResolvedPlace({...base, priceLevel: 'weird'}).priceLevel).toBeNull()
  })
  it('falls back to null address / empty types safely', () => {
    const r = toResolvedPlace({...base, address: null, types: []})
    expect(r.address).toBeNull()
    expect(r.category).toBe('Other')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/lib/placeSnapshot.test.ts`
Expected: FAIL — cannot resolve `./placeSnapshot`.

- [ ] **Step 3: Write minimal implementation**

```ts
// frontend/src/pages/trips/lib/placeSnapshot.ts
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run src/pages/trips/lib/placeSnapshot.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/lib/placeSnapshot.ts frontend/src/pages/trips/lib/placeSnapshot.test.ts
git commit -m "feat(trips): toResolvedPlace snapshot mapper + field mask"
```

---

### Task 3: add-mode state in tripsSlice

**Files:**
- Modify: `frontend/src/pages/trips/tripsSlice.ts`
- Test: `frontend/src/pages/trips/tripsSlice.test.ts` (create)

**Interfaces:**
- Consumes: nothing new.
- Produces: `addMode: boolean` on `TripsState`; action `setAddMode(boolean)`. **Keeps** `addPlaceOpen`/`setAddPlaceOpen` for now — `TripDetailPage`/`AddPlaceSheet` still use them until Task 10, so the project keeps compiling task-by-task. Task 10 removes them.

- [ ] **Step 1: Write the failing test**

```ts
// frontend/src/pages/trips/tripsSlice.test.ts
import {describe, it, expect} from 'vitest'
import reducer, {setAddMode} from './tripsSlice'

const init = reducer(undefined, {type: '@@INIT'})

describe('tripsSlice add-mode', () => {
  it('defaults addMode to false', () => {
    expect(init.addMode).toBe(false)
  })
  it('setAddMode toggles the flag', () => {
    const on = reducer(init, setAddMode(true))
    expect(on.addMode).toBe(true)
    const off = reducer(on, setAddMode(false))
    expect(off.addMode).toBe(false)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/trips/tripsSlice.test.ts`
Expected: FAIL — `setAddMode` is not exported / `addMode` is undefined.

- [ ] **Step 3: Edit the slice — add `addMode`, keep `addPlaceOpen`**

In `frontend/src/pages/trips/tripsSlice.ts`, **add** `addMode` alongside the existing `addPlaceOpen` (do NOT remove `addPlaceOpen`; Task 10 removes it once its consumers are gone, so the project keeps compiling in between):

Add to the `TripsState` interface (after `addPlaceOpen: boolean`):
```ts
  addMode: boolean
```
In `initialState`, add `addMode: false` (keep `addPlaceOpen: false`):
```ts
  createTripOpen: false, addPlaceOpen: false, addMode: false, stopEditorStopId: null,
```
Add a reducer next to `setAddPlaceOpen` (keep `setAddPlaceOpen`):
```ts
    setAddMode(s, a: PayloadAction<boolean>) { s.addMode = a.payload },
```
Add `setAddMode` to the exported actions block (keep `setAddPlaceOpen`).

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run src/pages/trips/tripsSlice.test.ts`
Expected: PASS (2 tests).

> The project still compiles after this task — nothing was removed; `addPlaceOpen` and `addMode` coexist until Task 10.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/tripsSlice.ts frontend/src/pages/trips/tripsSlice.test.ts
git commit -m "feat(trips): add addMode slice state (alongside addPlaceOpen)"
```

---

### Task 4: `usePlaceSearch` hook — Maps JS autocomplete + details

**Files:**
- Create: `frontend/src/pages/trips/hooks/usePlaceSearch.ts`

**Interfaces:**
- Consumes: `useMapsLibrary`, `useMap` from `@vis.gl/react-google-maps`; `toResolvedPlace`, `RawPlaceFields`, `PLACE_DETAIL_FIELDS` (Task 2); `ResolvedPlaceDto` from api.
- Produces:
  ```ts
  interface Suggestion { placeId: string; primary: string; secondary: string }
  interface UsePlaceSearch {
    query: string
    setQuery(q: string): void                             // debounced autocomplete internally
    suggestions: Suggestion[]
    loading: boolean
    error: string | null
    ready: boolean                                        // places library loaded
    resolveSuggestion(placeId: string): Promise<ResolvedPlaceDto>  // search path: placePrediction.toPlace() → fetchFields, then resets session
    resolveById(placeId: string): Promise<ResolvedPlaceDto>        // POI-tap path: new Place({id}) → fetchFields (standalone, no session)
    reset(): void                                         // clear query+suggestions, mint a fresh session token
  }
  function usePlaceSearch(): UsePlaceSearch
  ```

**Grounded (google-maps-platform skill, REST retrieveContexts — @vis.gl/react-google-maps + Google extended-component-library):**
- Search path resolves via `suggestion.placePrediction.toPlace()` then `place.fetchFields({fields})` **without** a `sessionToken` arg — the token is carried by the prediction; calling `fetchFields` **invalidates** the session, so the token must be reset afterward (vis.gl `useAutocompleteSuggestions` / `autocomplete-custom.tsx`).
- `Place` fields read directly: `place.id` (string), `place.displayName` (string), `place.formattedAddress` (string|null), `place.location` (LatLng → `.lat()`/`.lng()`), `place.types` (string[]), `place.priceLevel` (PriceLevel enum), `place.regularOpeningHours` (`.periods`, `.weekdayDescriptions`).
- The Autocomplete **Data API** (`AutocompleteSuggestion`) is stable on the default channel — **no `version="beta"`** needed (beta is only for the `<gmp-place-autocomplete>` web component, which we are not using).

**Verification note:** This hook touches the Google SDK, which cannot render in Vitest/jsdom, so it has no unit test (matches the repo's convention). It is verified by typecheck (Task-12 build) and by driving the app (Task 12 manual checklist).

- [ ] **Step 1: Implement the hook**

```ts
// frontend/src/pages/trips/hooks/usePlaceSearch.ts
// Client-side Google Places (New) autocomplete + details, on the browser key
// already loaded for the map (ADR-015). One AutocompleteSessionToken threads the
// autocomplete calls; the picked prediction's toPlace().fetchFields() completes and
// invalidates that session (→ resetSession). Suggestions are biased to the current
// map viewport. Debounced, min-length gated.
// Grounded: @vis.gl/react-google-maps examples/autocomplete (use-autocomplete-
// suggestions.ts, autocomplete-custom.tsx) + Google extended-component-library.
import {useCallback, useEffect, useRef, useState} from 'react'
import {useMap, useMapsLibrary} from '@vis.gl/react-google-maps'
import {toResolvedPlace, PLACE_DETAIL_FIELDS, type RawPlaceFields} from '../lib/placeSnapshot'
import type {ResolvedPlaceDto} from '../../../shared/api/api'

const DEBOUNCE_MS = 300
const MIN_CHARS = 2

export interface Suggestion {
  placeId: string
  primary: string
  secondary: string
}

// Shared extraction: a populated google Place → the plain, testable RawPlaceFields.
function extract(place: google.maps.places.Place, fallbackId: string): RawPlaceFields {
  return {
    placeId: place.id ?? fallbackId,
    name: place.displayName ?? '',
    lat: place.location?.lat() ?? 0,
    lng: place.location?.lng() ?? 0,
    address: place.formattedAddress ?? null,
    types: place.types ?? [],
    priceLevel: (place.priceLevel as unknown as string) ?? null,
    openingHoursJson: place.regularOpeningHours
      ? JSON.stringify({
          periods: place.regularOpeningHours.periods,
          weekdayDescriptions: place.regularOpeningHours.weekdayDescriptions,
        })
      : null,
  }
}

export function usePlaceSearch() {
  const placesLib = useMapsLibrary('places')
  const map = useMap()
  const [query, setQueryState] = useState('')
  const [suggestions, setSuggestions] = useState<Suggestion[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const tokenRef = useRef<google.maps.places.AutocompleteSessionToken | null>(null)
  // Retain the raw predictions so resolveSuggestion can call toPlace() on the exact
  // prediction the user picked (toPlace() carries the session-token binding).
  const rawRef = useRef<google.maps.places.AutocompleteSuggestion[]>([])
  const debRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const reqId = useRef(0)

  const ready = !!placesLib

  const ensureToken = useCallback(() => {
    if (!placesLib) return null
    if (!tokenRef.current) tokenRef.current = new placesLib.AutocompleteSessionToken()
    return tokenRef.current
  }, [placesLib])

  const runFetch = useCallback(async (input: string) => {
    if (!placesLib || input.trim().length < MIN_CHARS) {
      setSuggestions([])
      rawRef.current = []
      return
    }
    const mine = ++reqId.current
    setLoading(true)
    setError(null)
    try {
      const request: google.maps.places.AutocompleteRequest = {
        input,
        sessionToken: ensureToken()!,
        language: 'th',
      }
      const bounds = map?.getBounds()
      if (bounds) request.locationBias = bounds
      const {suggestions: raw} =
        await placesLib.AutocompleteSuggestion.fetchAutocompleteSuggestions(request)
      if (mine !== reqId.current) return // stale response — a newer keystroke won
      rawRef.current = raw
      setSuggestions(
        raw
          .filter((s) => !!s.placePrediction)
          .map((s) => {
            const p = s.placePrediction!
            return {
              placeId: p.placeId,
              primary: p.mainText?.text ?? p.text.text,
              secondary: p.secondaryText?.text ?? '',
            }
          }),
      )
    } catch {
      if (mine === reqId.current) setError('ค้นหาสถานที่ไม่สำเร็จ ลองใหม่ หรือใช้ “วางลิงก์”')
    } finally {
      if (mine === reqId.current) setLoading(false)
    }
  }, [placesLib, map, ensureToken])

  const setQuery = useCallback((q: string) => {
    setQueryState(q)
    if (debRef.current) clearTimeout(debRef.current)
    debRef.current = setTimeout(() => void runFetch(q), DEBOUNCE_MS)
  }, [runFetch])

  // Search path: resolve the picked prediction via toPlace() (keeps the session),
  // fetch fields WITHOUT a sessionToken arg, then reset the now-consumed session.
  const resolveSuggestion = useCallback(async (placeId: string): Promise<ResolvedPlaceDto> => {
    const suggestion = rawRef.current.find((s) => s.placePrediction?.placeId === placeId)
    if (!suggestion?.placePrediction) throw new Error('suggestion not found')
    const place = suggestion.placePrediction.toPlace()
    await place.fetchFields({fields: [...PLACE_DETAIL_FIELDS]})
    tokenRef.current = null // session consumed by fetchFields — next search mints a fresh token
    return toResolvedPlace(extract(place, placeId))
  }, [])

  // POI-tap path: standalone detail fetch, no autocomplete session.
  const resolveById = useCallback(async (placeId: string): Promise<ResolvedPlaceDto> => {
    if (!placesLib) throw new Error('places library not ready')
    const place = new placesLib.Place({id: placeId})
    await place.fetchFields({fields: [...PLACE_DETAIL_FIELDS]})
    return toResolvedPlace(extract(place, placeId))
  }, [placesLib])

  const reset = useCallback(() => {
    setQueryState('')
    setSuggestions([])
    rawRef.current = []
    setError(null)
    tokenRef.current = null
  }, [])

  useEffect(() => () => { if (debRef.current) clearTimeout(debRef.current) }, [])

  return {query, setQuery, suggestions, loading, error, ready, resolveSuggestion, resolveById, reset}
}
```

- [ ] **Step 2: Typecheck the hook in isolation**

Run: `cd frontend && npx tsc -b --noEmit`
Expected: PASS. If `google.maps.places.*` types are missing, add `@types/google.maps` as a dev dependency (`npm i -D @types/google.maps`) and re-run — note that `@vis.gl/react-google-maps` re-exports the runtime but the ambient `google` namespace types come from `@types/google.maps`. Fix any signature drift flagged against the real Places-New API per the `google-maps-platform` skill.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/trips/hooks/usePlaceSearch.ts frontend/package.json frontend/package-lock.json
git commit -m "feat(trips): usePlaceSearch — client-side Places autocomplete + details"
```

---

### Task 5: `AddPlacePreviewCard` — presentational preview

**Files:**
- Create: `frontend/src/pages/trips/components/AddPlacePreviewCard.tsx`

**Interfaces:**
- Consumes: `ResolvedPlaceDto`, `PlaceCategory` from api; `DropDownList` from `@syncfusion/react-dropdowns`.
- Produces:
  ```ts
  interface AddPlacePreviewCardProps {
    place: ResolvedPlaceDto
    category: PlaceCategory
    onCategoryChange(c: PlaceCategory): void
    onCancel(): void
    onAdd(): void
    saving: boolean
    variant?: 'floating' | 'sheet'   // desktop card vs mobile bottom sheet
  }
  function AddPlacePreviewCard(props: AddPlacePreviewCardProps): JSX.Element
  ```

**Verification:** presentational; no unit test (no RTL). Verified by build + visual parity with the mock (Task 12).

- [ ] **Step 1: Implement the component**

```tsx
// frontend/src/pages/trips/components/AddPlacePreviewCard.tsx
// Preview card shown after a place is picked (search) or tapped (map). Category is
// pre-filled from the Google-types guess (ADR-016) and stays editable. Colour dot +
// Thai label — no emoji (project rule). Layout mirrors docs/mocks/trip-add-place-search-mock.html.
import {DropDownList} from '@syncfusion/react-dropdowns'
import type {PlaceCategory, ResolvedPlaceDto} from '../../../shared/api/api'

const CAT_COLOR: Record<PlaceCategory, string> = {
  Stay: '#6d5ae6', Eat: '#e2553e', See: '#1f9d76',
  Cafe: '#b4791f', Shop: '#c2418f', Other: '#0e8f9e',
}
const CAT_LABEL: Record<PlaceCategory, string> = {
  Stay: 'ที่พัก', Eat: 'ร้านอาหาร', See: 'ที่เที่ยว',
  Cafe: 'คาเฟ่', Shop: 'ช้อปปิ้ง', Other: 'อื่นๆ',
}
const CATS = (Object.keys(CAT_LABEL) as PlaceCategory[]).map((value) => ({
  label: CAT_LABEL[value], value,
}))

export interface AddPlacePreviewCardProps {
  place: ResolvedPlaceDto
  category: PlaceCategory
  onCategoryChange(c: PlaceCategory): void
  onCancel(): void
  onAdd(): void
  saving: boolean
  variant?: 'floating' | 'sheet'
}

export function AddPlacePreviewCard({
  place, category, onCategoryChange, onCancel, onAdd, saving, variant = 'floating',
}: AddPlacePreviewCardProps) {
  return (
    <div className={`add-preview add-preview-${variant}`}>
      {variant === 'sheet' && <div className="add-preview-grip" />}
      <div className="add-preview-head">
        <div className="add-preview-title">
          <div className="add-preview-name">{place.name}</div>
          {place.address && <div className="add-preview-addr">{place.address}</div>}
        </div>
        <button type="button" className="add-preview-close" aria-label="ปิด" onClick={onCancel}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"><path d="M6 6l12 12M18 6L6 18" /></svg>
        </button>
      </div>

      <div className="add-preview-cat">
        <div className="add-preview-cat-lab">
          หมวดหมู่ <span className="add-preview-auto">เดาจาก Google: {CAT_LABEL[category]}</span>
        </div>
        <span className="add-preview-cat-dot" style={{background: CAT_COLOR[category]}} />
        <DropDownList
          dataSource={CATS}
          fields={{text: 'label', value: 'value'}}
          value={category}
          onChange={(e: {value: unknown}) => onCategoryChange((e.value as PlaceCategory) ?? 'Other')}
        />
      </div>

      <div className="add-preview-foot">
        <button type="button" className="add-preview-cancel" onClick={onCancel}>ยกเลิก</button>
        <button type="button" className="add-preview-add" onClick={onAdd} disabled={saving}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round"><path d="M12 5v14M5 12h14" /></svg>
          {saving ? 'กำลังเพิ่ม…' : 'เพิ่มลงทริป'}
        </button>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Typecheck**

Run: `cd frontend && npx tsc -b --noEmit`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/trips/components/AddPlacePreviewCard.tsx
git commit -m "feat(trips): AddPlacePreviewCard preview component"
```

---

### Task 6: `PlaceLinkFallbackDialog` — hidden paste-a-link path

**Files:**
- Create: `frontend/src/pages/trips/components/PlaceLinkFallbackDialog.tsx`

**Interfaces:**
- Consumes: `useResolvePlaceMutation`, `ResolvedPlaceDto` from api; `Dialog` (`@syncfusion/react-popups`), `TextBox` (`@syncfusion/react-inputs`), `Button` (`@syncfusion/react-buttons`); `getErrorMessage` from `../../../shared/utils/getErrorMessage`.
- Produces:
  ```ts
  interface PlaceLinkFallbackDialogProps {
    onResolved(dto: ResolvedPlaceDto): void   // hand the resolved place to AddPlaceMode's preview
    onClose(): void
  }
  ```

**Verification:** build + manual (paste a `maps.app.goo.gl` link → resolves → preview appears).

- [ ] **Step 1: Implement (lift the resolve logic from the old AddPlaceSheet)**

```tsx
// frontend/src/pages/trips/components/PlaceLinkFallbackDialog.tsx
// Hidden fallback (ADR-014): paste a Google Maps link → server-side resolve
// (SSRF-guarded) → hand the ResolvedPlaceDto up so AddPlaceMode shows the same
// preview as the search/tap paths. This is the surviving half of the old
// AddPlaceSheet; the search/tap paths are the primary entry now.
import {useState} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox} from '@syncfusion/react-inputs'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {useResolvePlaceMutation, type ResolvedPlaceDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

export interface PlaceLinkFallbackDialogProps {
  onResolved(dto: ResolvedPlaceDto): void
  onClose(): void
}

export function PlaceLinkFallbackDialog({onResolved, onClose}: PlaceLinkFallbackDialogProps) {
  const [url, setUrl] = useState('')
  const [resolvePlace, {isLoading, error}] = useResolvePlaceMutation()

  const doResolve = async () => {
    try {
      const dto = await resolvePlace({url}).unwrap()
      onResolved(dto)
      onClose()
    } catch { /* surfaced via error */ }
  }

  return (
    <Dialog open onClose={onClose} modal header="วางลิงก์จาก Google Maps" style={{width: '420px'}}>
      <div className="add-place-sheet">
        <div className="trip-form-field">
          <label className="trip-form-label">วางลิงก์จาก Google Maps</label>
          <div className="add-place-row">
            <TextBox
              value={url}
              onChange={(e: {value?: string}) => setUrl(e.value ?? '')}
              placeholder="https://maps.app.goo.gl/…"
            />
            <Button
              type="button" variant={Variant.Filled} color={Color.Primary}
              disabled={!url || isLoading} onClick={doResolve}
            >
              {isLoading ? 'กำลังดึง…' : 'ดึงข้อมูล'}
            </Button>
          </div>
          {error && <p className="trips-field-error">{getErrorMessage(error)}</p>}
        </div>
      </div>
    </Dialog>
  )
}
```

- [ ] **Step 2: Typecheck**

Run: `cd frontend && npx tsc -b --noEmit`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/trips/components/PlaceLinkFallbackDialog.tsx
git commit -m "feat(trips): PlaceLinkFallbackDialog — hidden paste-a-link path"
```

---

### Task 7: `AddPlaceSearchBar` — floating search bar + suggestions

**Files:**
- Create: `frontend/src/pages/trips/components/AddPlaceSearchBar.tsx`

**Interfaces:**
- Consumes: `Suggestion` type + hook shape from `usePlaceSearch` (Task 4).
- Produces:
  ```ts
  interface AddPlaceSearchBarProps {
    query: string
    onQueryChange(q: string): void
    suggestions: Suggestion[]
    loading: boolean
    error: string | null
    onPick(placeId: string): void
    onOpenLinkFallback(): void
  }
  ```

**Verification:** build + manual (typing shows suggestions; "วางลิงก์" opens the fallback).

- [ ] **Step 1: Implement the component**

```tsx
// frontend/src/pages/trips/components/AddPlaceSearchBar.tsx
// Floating search bar over the map (ADR-016). Live suggestions from usePlaceSearch;
// the "วางลิงก์" control reveals the hidden paste fallback. Matches the mock.
import type {Suggestion} from '../hooks/usePlaceSearch'

export interface AddPlaceSearchBarProps {
  query: string
  onQueryChange(q: string): void
  suggestions: Suggestion[]
  loading: boolean
  error: string | null
  onPick(placeId: string): void
  onOpenLinkFallback(): void
}

export function AddPlaceSearchBar({
  query, onQueryChange, suggestions, loading, error, onPick, onOpenLinkFallback,
}: AddPlaceSearchBarProps) {
  return (
    <div className="add-search-wrap">
      <div className="add-search-box">
        <svg className="add-search-mag" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"><circle cx="11" cy="11" r="7" /><path d="M21 21l-4-4" /></svg>
        <input
          className="add-search-input"
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
          placeholder="ค้นหาสถานที่…"
          autoFocus
        />
        <button type="button" className="add-search-link" onClick={onOpenLinkFallback}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1" /><path d="M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1" /></svg>
          วางลิงก์
        </button>
      </div>

      {(suggestions.length > 0 || error) && (
        <div className="add-suggest">
          {error && <div className="add-suggest-error">{error}</div>}
          {suggestions.map((s) => (
            <button type="button" key={s.placeId} className="add-sug" onClick={() => onPick(s.placeId)}>
              <svg className="add-sug-mkr" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 21s-7-6.3-7-11a7 7 0 0 1 14 0c0 4.7-7 11-7 11z" /><circle cx="12" cy="10" r="2.5" /></svg>
              <span className="add-sug-txt"><b>{s.primary}</b><span>{s.secondary}</span></span>
            </button>
          ))}
        </div>
      )}
      {loading && suggestions.length === 0 && <div className="add-suggest add-suggest-loading">กำลังค้นหา…</div>}
    </div>
  )
}
```

- [ ] **Step 2: Typecheck**

Run: `cd frontend && npx tsc -b --noEmit`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/trips/components/AddPlaceSearchBar.tsx
git commit -m "feat(trips): AddPlaceSearchBar — floating search + live suggestions"
```

---

### Task 8: `AddPlaceMode` — orchestrator inside the map

**Files:**
- Create: `frontend/src/pages/trips/components/AddPlaceMode.tsx`

**Interfaces:**
- Consumes: `usePlaceSearch` (Task 4), `AddPlaceSearchBar` (Task 7), `AddPlacePreviewCard` (Task 5), `PlaceLinkFallbackDialog` (Task 6); `useAddTripPlaceMutation`, `ResolvedPlaceDto`, `PlaceCategory` from api; `AdvancedMarker`, `Pin` from `@vis.gl/react-google-maps`; `useBreakpoint` from `../../../shared/hooks/useBreakpoint`.
- Produces:
  ```ts
  interface AddPlaceModeProps {
    tripId: string
    onExit(): void
    tappedPlaceId: string | null       // POI place_id from TripMap's onClick (Task 9)
    onTapConsumed(): void
  }
  function AddPlaceMode(props: AddPlaceModeProps): JSX.Element
  ```
  Rendered **inside** `<Map>` so it can use the map context via `usePlaceSearch`.

**Verification:** build + manual (search→pick→pin+preview→add→stays armed; tap POI→preview; Esc exits).

- [ ] **Step 1: Implement the orchestrator**

```tsx
// frontend/src/pages/trips/components/AddPlaceMode.tsx
// Add-mode controller, rendered inside <Map>. Owns the selected place, the temp
// teal pin, the search bar + preview card + link fallback, and the addTripPlace
// call. Stays armed after a successful add (ADR-016); Esc exits.
import {useCallback, useEffect, useState} from 'react'
import {AdvancedMarker, Pin} from '@vis.gl/react-google-maps'
import {useAddTripPlaceMutation, type PlaceCategory, type ResolvedPlaceDto} from '../../../shared/api/api'
import {usePlaceSearch} from '../hooks/usePlaceSearch'
import {AddPlaceSearchBar} from './AddPlaceSearchBar'
import {AddPlacePreviewCard} from './AddPlacePreviewCard'
import {PlaceLinkFallbackDialog} from './PlaceLinkFallbackDialog'
import {useBreakpoint} from '../../../shared/hooks/useBreakpoint'

export interface AddPlaceModeProps {
  tripId: string
  onExit(): void
  tappedPlaceId: string | null
  onTapConsumed(): void
}

export function AddPlaceMode({tripId, onExit, tappedPlaceId, onTapConsumed}: AddPlaceModeProps) {
  const search = usePlaceSearch()
  const [selected, setSelected] = useState<ResolvedPlaceDto | null>(null)
  const [category, setCategory] = useState<PlaceCategory>('Other')
  const [showLink, setShowLink] = useState(false)
  const [addTripPlace, {isLoading: saving}] = useAddTripPlaceMutation()
  const bp = useBreakpoint()

  const present = useCallback((dto: ResolvedPlaceDto) => {
    setSelected(dto)
    setCategory(dto.category)
  }, [])

  // A POI tapped on the map (Task 9 pushes its place_id down).
  useEffect(() => {
    if (!tappedPlaceId) return
    let cancelled = false
    void search.resolveById(tappedPlaceId)
      .then((dto) => { if (!cancelled) present(dto) })
      .catch(() => { /* ignore — bad/blank POI */ })
      .finally(() => onTapConsumed())
    return () => { cancelled = true }
  }, [tappedPlaceId, search, present, onTapConsumed])

  // Esc exits add-mode.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onExit() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onExit])

  const pick = useCallback(async (placeId: string) => {
    try { present(await search.resolveSuggestion(placeId)) } catch { /* surfaced by hook */ }
  }, [search, present])

  const clearSelection = useCallback(() => { setSelected(null); search.reset() }, [search])

  const doAdd = useCallback(async () => {
    if (!selected) return
    try {
      await addTripPlace({
        tripId,
        googlePlaceId: selected.googlePlaceId,
        name: selected.name,
        lat: selected.lat,
        lng: selected.lng,
        address: selected.address,
        category,
        priceLevel: selected.priceLevel,
        photoUrl: selected.photoUrl,
        openingHoursJson: selected.openingHoursJson,
      }).unwrap()
      clearSelection() // stay armed for the next place (ADR-016)
    } catch { /* surfaced via mutation error state; keep the card open */ }
  }, [selected, category, tripId, addTripPlace, clearSelection])

  return (
    <>
      <AddPlaceSearchBar
        query={search.query}
        onQueryChange={search.setQuery}
        suggestions={search.suggestions}
        loading={search.loading}
        error={search.error}
        onPick={pick}
        onOpenLinkFallback={() => setShowLink(true)}
      />

      {selected && (
        <AdvancedMarker position={{lat: selected.lat, lng: selected.lng}} zIndex={999}>
          <Pin background="#0e8f9e" borderColor="#fff" glyphColor="#fff" scale={1.3} />
        </AdvancedMarker>
      )}

      {selected && (
        <AddPlacePreviewCard
          place={selected}
          category={category}
          onCategoryChange={setCategory}
          onCancel={clearSelection}
          onAdd={doAdd}
          saving={saving}
          variant={bp === 'desktop' ? 'floating' : 'sheet'}
        />
      )}

      {showLink && (
        <PlaceLinkFallbackDialog
          onResolved={present}
          onClose={() => setShowLink(false)}
        />
      )}
    </>
  )
}
```

> `AdvancedMarker` and the preview card render inside the map's DOM subtree. If the Syncfusion `Dialog` or the fixed-position preview card must escape the map's stacking context, they already portal to `document.body` (Syncfusion) / are `position: fixed` (CSS in Task 11) — verify visually in Task 12.

- [ ] **Step 2: Typecheck**

Run: `cd frontend && npx tsc -b --noEmit`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/trips/components/AddPlaceMode.tsx
git commit -m "feat(trips): AddPlaceMode orchestrator (search + tap + preview + add)"
```

---

### Task 9: Wire add-mode + POI tap into TripMap

**Files:**
- Modify: `frontend/src/pages/trips/components/TripMap.tsx`

**Interfaces:**
- Consumes: `AddPlaceMode` (Task 8).
- Produces: `TripMap` gains optional props `addMode?: boolean`, `tripId?: string`, `onExitAddMode?(): void`. When `addMode`, it renders `<AddPlaceMode>` and captures POI clicks.

- [ ] **Step 1: Extend the props type**

Change the `TripMap` signature to add:
```ts
  addMode = false,
  tripId,
  onExitAddMode,
}: {
  places: TripPlaceDto[]
  route?: RouteStop[]
  summaryLabel?: string
  summaryText?: string
  addMode?: boolean
  tripId?: string
  onExitAddMode?: () => void
}) {
```

- [ ] **Step 2: Track the tapped POI and pass onClick to `<Map>`**

Inside the component body (near the top, after `path`):
```ts
  const [tappedPlaceId, setTappedPlaceId] = useState<string | null>(null)
```
Add to the `<Map>` element props (alongside `mapId`, `defaultCenter`, …):
```tsx
          onClick={(ev) => {
            if (!addMode) return
            // POI clicks carry a placeId; empty-ground clicks do not (ADR-016).
            // Grounded: google IconMouseEvent exposes `placeId` + `latLng`, and
            // event.stop() suppresses the default POI info window; @vis.gl surfaces
            // these as ev.detail.placeId and ev.stop().
            const placeId = ev.detail.placeId
            if (placeId) {
              ev.stop() // suppress the default Google info window
              setTappedPlaceId(placeId)
            }
          }}
```
> `import {useState}` at the top (the file currently imports `useEffect, useMemo`). The map's `clickableIcons` must stay enabled (it is by default — `TripMap` does not set it to `false`), otherwise POI icons emit no `placeId`. If `ev.detail.placeId` is typed as never/unknown for the installed `@vis.gl` version, cast with `(ev.detail as {placeId?: string | null}).placeId` and leave a comment.

- [ ] **Step 3: Render `<AddPlaceMode>` as a child of `<Map>`**

Immediately after the `{routeMode ? (…) : (…)}` markers block, still inside `<Map>`:
```tsx
          {addMode && tripId && (
            <AddPlaceMode
              tripId={tripId}
              onExit={() => onExitAddMode?.()}
              tappedPlaceId={tappedPlaceId}
              onTapConsumed={() => setTappedPlaceId(null)}
            />
          )}
```
Add the import at the top: `import {AddPlaceMode} from './AddPlaceMode'`.

- [ ] **Step 4: Typecheck**

Run: `cd frontend && npx tsc -b --noEmit`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/TripMap.tsx
git commit -m "feat(trips): TripMap renders AddPlaceMode + captures POI taps in add-mode"
```

---

### Task 10: Wire TripDetailPage — arm add-mode, drop the old sheet

**Files:**
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx`
- Modify: `frontend/src/pages/trips/tripsSlice.ts` (remove the now-unused `addPlaceOpen`/`setAddPlaceOpen`)

**Interfaces:**
- Consumes: `setAddMode` (Task 3), `TripMap` new props (Task 9).

- [ ] **Step 1: Update imports & selector**

- Remove `import { AddPlaceSheet } from './components/AddPlaceSheet'`.
- Change `import { setActiveTab, setPlacesView, setAddPlaceOpen } from './tripsSlice'` → `import { setActiveTab, setPlacesView, setAddMode } from './tripsSlice'`.
- Change `const addOpen = useAppSelector((s) => s.trips.addPlaceOpen)` → `const addMode = useAppSelector((s) => s.trips.addMode)`.

- [ ] **Step 2: Desktop — arm add-mode from the toolbar and feed the map**

In the desktop block, change the "+ เพิ่มสถานที่" button onClick:
```tsx
                  onClick={() => dispatch(setAddMode(true))}
```
Change the right-column `<TripMap … />` to pass add-mode props:
```tsx
          <TripMap
            places={places ?? []}
            route={tab === 'itinerary' ? dayRoute.route : undefined}
            summaryLabel={dayRoute.dayLabel}
            summaryText={dayRoute.summaryText}
            addMode={tab === 'places' && addMode}
            tripId={tripId}
            onExitAddMode={() => dispatch(setAddMode(false))}
          />
```
Delete the desktop `{addOpen && (<AddPlaceSheet … />)}` block.

- [ ] **Step 3: Mobile — arm add-mode, force the map view**

In the mobile block, change the "+ เพิ่มสถานที่" button onClick to also switch to the map:
```tsx
              onClick={() => { dispatch(setPlacesView('map')); dispatch(setAddMode(true)) }}
```
Change the mobile map render:
```tsx
          {placesView === 'map' ? (
            <TripMap
              places={places ?? []}
              addMode={addMode}
              tripId={tripId}
              onExitAddMode={() => dispatch(setAddMode(false))}
            />
          ) : places?.length ? (
```
Delete the mobile `{addOpen && (<AddPlaceSheet … />)}` block.

- [ ] **Step 3b: Remove the superseded `addPlaceOpen` from the slice**

Now that no component references `addPlaceOpen`, delete it from `frontend/src/pages/trips/tripsSlice.ts`: remove `addPlaceOpen: boolean` from `TripsState`, remove `addPlaceOpen: false` from `initialState`, remove the `setAddPlaceOpen(...)` reducer, and remove `setAddPlaceOpen` from the exported actions block. (`addMode`/`setAddMode` stay.)

- [ ] **Step 4: Full build (all wiring now consistent)**

Run: `cd frontend && npm run build`
Expected: PASS — `tsc -b` clean, `vite build` succeeds. (The project compiled after every prior task; this task removes `addPlaceOpen` and its last consumers together, so it stays clean.)

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/TripDetailPage.tsx
git commit -m "feat(trips): arm map-centric add-mode from TripDetailPage; drop AddPlaceSheet usage"
```

---

### Task 11: Add-mode styles (trips-tokens.css)

**Files:**
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Verification:** build + visual parity with the mock (Task 12).

- [ ] **Step 1: Append the add-mode styles**

Append to `frontend/src/pages/trips/trips-tokens.css` (values from the confirmed mock; teal tokens already defined at the top of this file):

```css
/* ── Add-place add-mode: search bar + suggestions + preview (ADR-016) ── */
.add-search-wrap { position: absolute; top: 14px; left: 14px; right: 14px; z-index: 6; }
.add-search-box {
  display: flex; align-items: center; gap: 10px;
  background: #fff; border-radius: 12px; padding: 10px 13px;
  box-shadow: 0 6px 20px rgba(15, 23, 42, 0.18);
}
.add-search-mag { width: 18px; height: 18px; color: var(--teal); flex: none; }
.add-search-input { flex: 1; min-width: 0; border: 0; outline: 0; font: inherit; font-size: 14.5px; color: var(--ink); background: transparent; }
.add-search-link {
  display: inline-flex; align-items: center; gap: 5px; border: 0; background: transparent;
  color: var(--muted); font: inherit; font-size: 12px; cursor: pointer; padding: 4px 6px; border-radius: 8px; white-space: nowrap;
}
.add-search-link:hover { background: #f1f5f9; color: var(--teal-deep); }
.add-search-link svg { width: 14px; height: 14px; }

.add-suggest { margin-top: 6px; background: #fff; border-radius: 12px; overflow: hidden; box-shadow: 0 8px 24px rgba(15, 23, 42, 0.18); }
.add-suggest-loading, .add-suggest-error { padding: 11px 14px; font-size: 12.5px; color: var(--muted); }
.add-suggest-error { color: var(--warn); }
.add-sug { display: flex; width: 100%; text-align: left; align-items: center; gap: 11px; padding: 11px 14px; cursor: pointer; border: 0; border-bottom: 1px solid #f1f5f9; background: transparent; font: inherit; }
.add-sug:last-child { border-bottom: 0; }
.add-sug:hover { background: var(--teal-soft); }
.add-sug-mkr { width: 16px; height: 16px; color: var(--muted); flex: none; }
.add-sug-txt b { font-size: 14px; font-weight: 600; display: block; color: var(--ink); }
.add-sug-txt span { font-size: 12px; color: var(--muted); }

.add-preview { background: #fff; box-shadow: 0 14px 40px rgba(15, 23, 42, 0.26); z-index: 7; }
.add-preview-floating { position: absolute; left: 50%; bottom: 18px; transform: translateX(-50%); width: min(420px, calc(100% - 36px)); border-radius: 16px; padding: 16px 18px 15px; }
.add-preview-sheet { position: fixed; left: 0; right: 0; bottom: 0; border-radius: 20px 20px 0 0; padding: 8px 18px 20px; }
.add-preview-grip { width: 40px; height: 4px; border-radius: 999px; background: #cbd5e1; margin: 6px auto 12px; }
.add-preview-head { display: flex; align-items: flex-start; gap: 10px; }
.add-preview-title { flex: 1; min-width: 0; }
.add-preview-name { font-size: 17px; font-weight: 700; line-height: 1.2; color: var(--ink); }
.add-preview-addr { margin-top: 4px; font-size: 12.5px; color: var(--muted); line-height: 1.4; }
.add-preview-close { flex: none; width: 30px; height: 30px; border: 0; background: transparent; color: var(--muted); border-radius: 8px; cursor: pointer; display: flex; align-items: center; justify-content: center; }
.add-preview-close:hover { background: #f1f5f9; }
.add-preview-close svg { width: 16px; height: 16px; }
.add-preview-cat { margin-top: 13px; position: relative; }
.add-preview-cat-lab { font-size: 12px; color: var(--muted); font-weight: 600; margin-bottom: 6px; }
.add-preview-auto { background: var(--teal-soft); color: var(--teal-deep); border-radius: 999px; padding: 1px 8px; font-size: 10.5px; }
.add-preview-cat-dot { position: absolute; left: 13px; bottom: 15px; width: 11px; height: 11px; border-radius: 50%; z-index: 1; pointer-events: none; }
.add-preview-foot { display: flex; gap: 10px; margin-top: 16px; }
.add-preview-cancel { flex: none; border: 1.5px solid var(--border); background: #fff; color: #475569; border-radius: 11px; padding: 11px 18px; font: inherit; font-size: 14px; font-weight: 600; cursor: pointer; }
.add-preview-cancel:hover { background: #f8fafc; }
.add-preview-add { flex: 1; display: inline-flex; align-items: center; justify-content: center; gap: 7px; border: 0; background: var(--teal); color: #fff; border-radius: 11px; padding: 11px 18px; font: inherit; font-size: 14.5px; font-weight: 700; cursor: pointer; box-shadow: 0 6px 16px rgba(14, 143, 158, 0.4); }
.add-preview-add:hover:not(:disabled) { background: var(--teal-deep); }
.add-preview-add:disabled { opacity: 0.6; cursor: default; }
.add-preview-add svg { width: 16px; height: 16px; }
```

> The `.add-preview-cat-dot` is positioned over the Syncfusion `DropDownList`; if the dropdown's internal padding hides it, nudge `left`/`bottom` after checking the rendered `sf-*` markup in devtools (see the day-start-picker note elsewhere in this file for the sf-class caveat).

- [ ] **Step 2: Build**

Run: `cd frontend && npm run build`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/trips/trips-tokens.css
git commit -m "style(trips): add-mode search bar, suggestions & preview styles"
```

---

### Task 12: Delete the old sheet, verify end-to-end, compliance-review

**Files:**
- Delete: `frontend/src/pages/trips/components/AddPlaceSheet.tsx`

- [ ] **Step 1: Delete the superseded component**

```bash
git rm frontend/src/pages/trips/components/AddPlaceSheet.tsx
```

- [ ] **Step 2: Confirm nothing references it**

Run (Grep tool or): `cd frontend && grep -rn "AddPlaceSheet" src` → Expected: no matches.

- [ ] **Step 3: Full test + build + lint**

Run:
```bash
cd frontend && npm run test && npm run build && npm run lint
```
Expected: Vitest green (placeCategory, placeSnapshot, tripsSlice + existing suites); `tsc -b` + `vite build` clean; eslint clean.

- [ ] **Step 4: Manual end-to-end verification**

Start the app (`cd frontend && npm run dev`, ensure `VITE_GOOGLE_MAPS_BROWSER_KEY` is set — Demo Key is fine for dev), open a trip, and confirm against the mock ([docs/mocks/trip-add-place-search-mock.html](../../mocks/trip-add-place-search-mock.html)):
  - [ ] "+ เพิ่มสถานที่" arms add-mode; the floating search bar appears over the map (mobile switches to the map view).
  - [ ] Typing ≥2 chars shows live suggestions after ~300 ms, biased to the current viewport.
  - [ ] Picking a suggestion drops a teal pin and opens the preview with an auto-guessed, editable category.
  - [ ] Tapping a labelled POI on the map opens the same preview; tapping empty ground does nothing.
  - [ ] "+ เพิ่มลงทริป" saves; the place appears in the list/map; the card + input clear and add-mode stays armed.
  - [ ] Esc / close exits add-mode and removes the temp pin.
  - [ ] "วางลิงก์" opens the fallback dialog; pasting a `maps.app.goo.gl` link resolves into the same preview.
  - [ ] With a key lacking Places API (New), the search errors visibly and the "วางลิงก์" fallback still works (simulate by temporarily using a Maps-JS-only key, if available).

- [ ] **Step 5: Google Maps compliance-review (ADR-007)**

Invoke the `google-maps-platform` skill's `compliance-review` over the new Places usage (attribution id present, no scraped/stored data beyond `place_id` + snapshot, session tokens used, key restricted). Fix anything it flags.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(trips): remove AddPlaceSheet; finish map-centric add-place search"
```

---

## Self-Review

**1. Spec coverage:**
- §3 entry paths → Tasks 4/7 (search), 8/9 (tap), 6 (link fallback). ✓
- §4 client-side architecture + session tokens + field mask → Tasks 2 (`PLACE_DETAIL_FIELDS`), 4 (autocomplete/details/session token/viewport bias). ✓
- §4.2 key requirement → Global Constraints + Task 12 Step 4 degraded-key check. ✓
- §5 map-centric UX (search bar, temp pin, preview card, bottom sheet) → Tasks 5/7/8/9/11. ✓
- §5.1 category auto-guess table → Task 1. ✓
- §6 data contract (unchanged `addTripPlace`) → Task 8 `doAdd`. ✓
- §7 cost guards (debounce/min-chars/session/field mask) → Tasks 2 + 4. ✓
- §8 error/edge states → Task 4 (autocomplete/fetch errors, stale-response guard), Task 8 (bad POI ignored, save error keeps card), Task 12 Step 4 (degraded key). ✓
- §9 compliance → Task 12 Step 5. ✓
- §10 out of scope → not built (no reverse-geocode, no photos, no dupe detection). ✓
- §11 affected surfaces → File Structure matches. ✓

**2. Placeholder scan:** No TBD/TODO; every code step shows complete code; UI tasks that legitimately lack unit tests state why (no RTL) and give concrete typecheck/manual verification. ✓

**3. Type consistency:** `PlaceCategory`, `ResolvedPlaceDto`, `addTripPlace` body match api.ts verbatim. Hook surface (`resolve`, `resolveById`, `reset`, `suggestions`, `query`, `setQuery`, `loading`, `error`, `ready`) is consumed consistently by `AddPlaceMode` (Task 8) and `AddPlaceSearchBar` (Task 7). `setAddMode`/`addMode` consistent across Tasks 3/9/10. `PLACE_DETAIL_FIELDS` order matches its test. ✓

**SDK grounding (done — via the `google-maps-platform` skill, REST `retrieveContexts`):**
- `AutocompleteSuggestion.fetchAutocompleteSuggestions(request)` → `{suggestions}`; each `suggestion.placePrediction` has `placeId`, `text.text`, optional `mainText/secondaryText`, and `toPlace()` — matches Task 4. ✓
- Session: token in the autocomplete request; `placePrediction.toPlace().fetchFields({fields})` **without** a sessionToken arg; `fetchFields` invalidates the session → reset the token (Task 4). ✓
- `Place.priceLevel` JS-SDK enum values `'FREE'|'INEXPENSIVE'|'MODERATE'|'EXPENSIVE'|'VERY_EXPENSIVE'` (Task 2 PRICE map + test). ✓
- POI tap: `IconMouseEvent.placeId` + `event.stop()`, surfaced by `@vis.gl` as `ev.detail.placeId` / `ev.stop()` (Task 9). ✓
- No `version="beta"` needed for the Autocomplete Data API path (Task 4 note). ✓

The only residual runtime-verify items (Task 12 manual pass) are the exact `@vis.gl` `MapMouseEvent.detail` TS typing for the installed version and the `regularOpeningHours` serialized shape — both have inline fallbacks.
