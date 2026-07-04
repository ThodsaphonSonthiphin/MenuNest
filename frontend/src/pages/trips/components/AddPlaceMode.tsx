// frontend/src/pages/trips/components/AddPlaceMode.tsx
// Add-mode controller, rendered as a .trip-map sibling of <Map>. Owns the selected
// place, the search bar + preview card + link fallback, and the addTripPlace call.
// Reports the selected place's coords upward (TripMap renders the temp teal pin
// inside <Map>). Stays armed after a successful add (ADR-016); Esc exits.
import {useCallback, useEffect, useRef, useState} from 'react'
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
  onSelectedChange(pos: {lat: number; lng: number} | null): void
}

export function AddPlaceMode({tripId, onExit, tappedPlaceId, onTapConsumed, onSelectedChange}: AddPlaceModeProps) {
  const search = usePlaceSearch()
  const [selected, setSelected] = useState<ResolvedPlaceDto | null>(null)
  const [category, setCategory] = useState<PlaceCategory>('Other')
  // The category Google originally guessed. Kept separate from the (editable)
  // `category` so the "เดาจาก Google" badge can hide once the user overrides it.
  const [guessedCategory, setGuessedCategory] = useState<PlaceCategory | undefined>(undefined)
  const [showLink, setShowLink] = useState(false)
  const [addTripPlace, {isLoading: saving}] = useAddTripPlaceMutation()
  const bp = useBreakpoint()

  const present = useCallback((dto: ResolvedPlaceDto) => {
    setSelected(dto)
    setCategory(dto.category)
    setGuessedCategory(dto.category)
  }, [])

  // Report the selected place's coords upward so TripMap can render the temp teal
  // pin inside <Map> (markers need the map subtree). Clear on unmount.
  useEffect(() => {
    onSelectedChange(selected ? {lat: selected.lat, lng: selected.lng} : null)
    return () => onSelectedChange(null)
  }, [selected, onSelectedChange])

  // A POI tapped on the map (Task 9 pushes its place_id down). Resolve each tap
  // exactly once: usePlaceSearch returns a fresh object each render, so a plain
  // `search` dep would re-fire the effect mid-flight — double-billing a Places
  // Details call and (via a per-invocation cancelled flag) dropping the tap.
  // Guard by the tapped id, reset when it clears, so a re-render can't re-resolve
  // and the same POI can still be re-tapped later.
  const handledTapRef = useRef<string | null>(null)
  useEffect(() => {
    if (!tappedPlaceId) { handledTapRef.current = null; return }
    if (handledTapRef.current === tappedPlaceId) return
    handledTapRef.current = tappedPlaceId
    const id = tappedPlaceId
    void search.resolveById(id)
      .then((dto) => { present(dto); search.reset() })
      .catch(() => { /* ignore bad/blank POI */ })
      .finally(() => onTapConsumed())
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `search` is a fresh object each render (see comment above); depend on the stable memoised `search.resolveById`, not the whole object.
  }, [tappedPlaceId, search.resolveById, present, onTapConsumed])

  // Esc exits add-mode.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onExit() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onExit])

  const pick = useCallback(async (placeId: string) => {
    try {
      const dto = await search.resolveSuggestion(placeId)
      present(dto)
      search.reset()   // close the dropdown + clear the query after selecting
    } catch { /* surfaced by hook */ }
  }, [search, present])

  const clearSelection = useCallback(() => {
    setSelected(null)
    setGuessedCategory(undefined)
    search.reset()
  }, [search])

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
        onClose={onExit}
        autoFocus={bp === 'desktop'}
      />

      {selected && (
        <AddPlacePreviewCard
          place={selected}
          category={category}
          guessedCategory={guessedCategory}
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
