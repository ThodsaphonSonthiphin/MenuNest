/// <reference types="google.maps" />
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
      reqId.current++ // invalidate any in-flight response so it can't repopulate
      setSuggestions([])
      rawRef.current = []
      setLoading(false)
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
    try {
      await place.fetchFields({fields: [...PLACE_DETAIL_FIELDS]})
    } finally {
      tokenRef.current = null // session consumed/invalidated — next search mints a fresh token
    }
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
    reqId.current++ // invalidate any in-flight response so it can't repopulate after reset
    setQueryState('')
    setSuggestions([])
    rawRef.current = []
    setError(null)
    setLoading(false)
    tokenRef.current = null
  }, [])

  useEffect(() => () => { if (debRef.current) clearTimeout(debRef.current) }, [])

  return {query, setQuery, suggestions, loading, error, ready, resolveSuggestion, resolveById, reset}
}
