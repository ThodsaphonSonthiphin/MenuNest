// frontend/src/pages/trips/components/AddPlaceMode.tsx
// Add-mode controller, rendered as a .trip-map sibling of <Map>. Owns the selected
// place, the search bar + preview card + link fallback, and the addTripPlace call.
// Reports the selected place's coords upward (TripMap renders the temp teal pin
// inside <Map>). Stays armed after a successful add (ADR-016); Esc exits.
import {useCallback, useEffect, useRef, useState} from 'react'
import {useAddTripPlaceMutation, useAddStopMutation, type PlaceCategory, type ResolvedPlaceDto, type TravelMode} from '../../../shared/api/api'
import {usePlaceSearch} from '../hooks/usePlaceSearch'
import {AddPlaceSearchBar} from './AddPlaceSearchBar'
import {AddPlacePreviewCard} from './AddPlacePreviewCard'
import {PlaceLinkFallbackDialog} from './PlaceLinkFallbackDialog'
import {useBreakpoint} from '../../../shared/hooks/useBreakpoint'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {draftsValid, MAX_REVIEW_LINKS, type ReviewDraft} from '../lib/reviewLinks'
import {addTripPlaceArgsForCapture, capturePayloadFrom, type CapturePayload} from '../lib/capturePayload'
import {captureCommitLabel} from '../../discover/lib/captureCommit'
import {captureNameValid, coordinatePlaceFrom, needsTypedName} from '../lib/coordinateCapture'

export interface AddStopContext {
  dayId: string
  dayLabel: string
  travelMode: TravelMode
}

/**
 * ADR-163 §3 / TC-910: the capture component takes a commit TARGET, not a tripId.
 *
 * `trip`   — the Trips surfaces. The Trip is already chosen, and `addStopContext` may
 *            additionally schedule the new Place as a Stop on a Day (ADR-067/068).
 * `choose` — Discover. No Trip is chosen yet, so the preview offers two same-level
 *            actions and the HOST performs the write, because the host owns both the
 *            trip picker and the create-and-seed call. Nothing is saved trip-less
 *            (ADR-155 superseding ADR-147).
 */
export type CaptureTarget =
  | {kind: 'trip'; tripId: string}
  | {
      kind: 'choose'
      /** Commit to a Trip the user picks now. Resolves once the Place is saved. */
      /**
       * Resolves true when the Place was saved, false when the user backed out.
       * `forcePicker` is R8.5's `▾`: it reopens the trip picker even when this run
       * already remembered a Trip, so a remembered Trip is never a one-way door.
       */
      onPickTrip(payload: CapturePayload, forcePicker?: boolean): Promise<boolean>
      /** Create a Trip in one tap, seeded with this Place as its first (ADR-098). */
      onCreateTrip(payload: CapturePayload): Promise<boolean>
      /** R8.5: once this run has used a Trip, the primary action names it. */
      rememberedTripName?: string | null
    }

export interface AddPlaceModeProps {
  target: CaptureTarget
  onExit(): void
  tappedPlaceId: string | null
  onTapConsumed(): void
  tappedLatLng: {lat: number; lng: number} | null
  onLatLngConsumed(): void
  onSelectedChange(pos: {lat: number; lng: number} | null): void
  addStopContext?: AddStopContext | null
  /** The armed-mode strip for a surface with no addStopContext of its own (Discover). */
  bannerText?: {text: string; sub?: string} | null
}

export function AddPlaceMode({target, onExit, tappedPlaceId, onTapConsumed, tappedLatLng, onLatLngConsumed, onSelectedChange, addStopContext, bannerText}: AddPlaceModeProps) {
  // R8.3 requires a THIN strip and never a fill over the map (#36 shipped a banner
  // that covered the whole map and read as a black screen), so both surfaces render
  // the same .add-capture-banner treatment rather than each inventing one.
  const banner = addStopContext
    ? {text: 'เพิ่มสถานที่ใหม่เป็นจุดแวะ', sub: addStopContext.dayLabel}
    : bannerText ?? null
  const search = usePlaceSearch()
  const [selected, setSelected] = useState<ResolvedPlaceDto | null>(null)
  // The name actually saved. Google supplies one for every resolved place; a coordinate
  // capture starts blank and the user must type it (R4.1).
  const [name, setName] = useState('')
  const [category, setCategory] = useState<PlaceCategory>('Other')
  // The category Google originally guessed. Kept separate from the (editable)
  // `category` so the "เดาจาก Google" badge can hide once the user overrides it.
  const [guessedCategory, setGuessedCategory] = useState<PlaceCategory | undefined>(undefined)
  const [showLink, setShowLink] = useState(false)
  const [reviewDrafts, setReviewDrafts] = useState<ReviewDraft[]>([])
  const [formError, setFormError] = useState<string | null>(null)
  // The choose-target write happens in the host, so `saving` from the local mutation
  // hooks cannot report it. Without this the two Discover buttons stayed live during
  // the round-trip and a double-tap wrote the place twice.
  const [committing, setCommitting] = useState(false)
  const [addTripPlace, {isLoading: saving}] = useAddTripPlaceMutation()
  const [addStop, {isLoading: adding}] = useAddStopMutation()
  // Idempotency: id of a Place already created during THIS attempt. If addTripPlace
  // succeeds but the addStop chain fails, a retry reuses this id instead of creating a
  // duplicate — AddTripPlaceHandler does not dedupe on (TripId, GooglePlaceId).
  const createdRef = useRef<string | null>(null)
  const bp = useBreakpoint()

  const present = useCallback((dto: ResolvedPlaceDto) => {
    setSelected(dto)
    setName(dto.name)
    setCategory(dto.category)
    // A coordinate capture has no Google guess to badge — its 'Other' is our default,
    // not Google's opinion, so the "เดาจาก Google" badge must not claim otherwise.
    setGuessedCategory(needsTypedName(dto) ? undefined : dto.category)
    setReviewDrafts([])
    setFormError(null)
    createdRef.current = null // a different place — forget any half-created previous one
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

  // An empty-ground tap (TripMap pushes its lat/lng down). Unlike a POI tap there is
  // nothing to resolve — the point IS the place — so this presents immediately and makes
  // no network call at all. Consumed the same way, so the same spot can be re-tapped.
  useEffect(() => {
    if (!tappedLatLng) return
    present(coordinatePlaceFrom(tappedLatLng.lat, tappedLatLng.lng))
    onLatLngConsumed()
  }, [tappedLatLng, present, onLatLngConsumed])

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
    setName('')
    setGuessedCategory(undefined)
    setReviewDrafts([])
    setFormError(null)
    createdRef.current = null
    search.reset()
  }, [search])

  const doAdd = useCallback(async (via: 'primary' | 'createTrip' | 'pickOther') => {
    if (!selected) return
    if (!captureNameValid(name)) {
      setFormError('ตั้งชื่อสถานที่ก่อนบันทึก')
      return
    }
    if (!draftsValid(reviewDrafts)) {
      setFormError(`ลิงก์รีวิวไม่ถูกต้อง หรือเกิน ${MAX_REVIEW_LINKS} ลิงก์`)
      return
    }
    setFormError(null)
    const payload = capturePayloadFrom(selected, name, category, reviewDrafts)

    // ADR-155: launched from Discover the capture still ends on a Trip — but WHICH Trip is
    // decided here, at commit time. The host owns that write (it owns the trip picker and
    // the create-and-seed call), so this branch never calls addTripPlace itself. On success
    // the form clears and the mode stays armed (R8.4).
    if (target.kind === 'choose') {
      setCommitting(true)
      try {
        const saved = await (via === 'createTrip'
          ? target.onCreateTrip(payload)
          : target.onPickTrip(payload, via === 'pickOther'))
        // Only a real save clears the form. A cancelled trip pick must leave the
        // typed name, category and review links exactly where the user left them.
        if (saved) clearSelection()
      } catch (err) {
        setFormError(getErrorMessage(err))
      } finally {
        setCommitting(false)
      }
      return
    }

    try {
      // Idempotent retry: reuse the Place if a prior attempt already created it
      // (addTripPlace succeeded, addStop failed). AddTripPlace is idempotent for an exact
      // place_id (#48), but a place_id-less capture still has no dedupe, so re-creating here
      // would leave a duplicate library Place on every retry.
      const placeId =
        createdRef.current ??
        (await addTripPlace(addTripPlaceArgsForCapture(target.tripId, payload)).unwrap()).id
      createdRef.current = placeId
      if (addStopContext) {
        // ADR-071: non-atomic — if addStop fails, the Place stays captured and a retry
        // reuses createdRef (above) rather than creating a duplicate.
        await addStop({
          tripId: target.tripId,
          dayId: addStopContext.dayId,
          tripPlaceId: placeId,
          dwellMinutes: 60,
          travelModeToReach: addStopContext.travelMode,
        }).unwrap()
        createdRef.current = null
        onExit() // ADR-068 single-shot: leave capture; host clears the flag + itinerary refetches
      } else {
        createdRef.current = null
        clearSelection() // stay armed for the next place (ADR-016)
      }
    } catch (err) {
      setFormError(getErrorMessage(err))
    }
  }, [selected, name, category, target, reviewDrafts, addTripPlace, addStop, addStopContext, clearSelection, onExit])

  // R8.5: once this run has committed to a Trip, the primary action names that Trip so a
  // second capture is one tap. Until then it opens the picker. On the Trips surfaces the
  // Trip is not in question, so the label states what the tap does to the itinerary.
  const primaryLabel =
    target.kind === 'choose'
      ? captureCommitLabel(target.rememberedTripName ? {id: '', name: target.rememberedTripName} : null)
      : addStopContext
        ? 'เพิ่มเป็นจุดแวะ'
        : 'เพิ่มลงทริป'

  return (
    <>
      {banner && (
        <div className="add-capture-banner">
          <button type="button" className="add-capture-back" aria-label="ยกเลิก" onClick={onExit}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"><path d="M15 6l-6 6 6 6" /></svg>
          </button>
          <span className="add-capture-txt">{banner.text}{banner.sub && <small>{banner.sub}</small>}</span>
        </div>
      )}

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
        bannerOffset={!!banner}
      />

      {/* R8.5's ▾ (onPrimaryAlt) is only meaningful once a Trip is remembered,
          because until then the primary action already opens the picker. */}
      {selected && (
        <AddPlacePreviewCard
          place={selected}
          name={name}
          onNameChange={setName}
          nameEditable={needsTypedName(selected)}
          category={category}
          guessedCategory={guessedCategory}
          onCategoryChange={setCategory}
          onCancel={clearSelection}
          onAdd={() => { void doAdd('primary') }}
          saving={saving || adding || committing}
          variant={bp === 'desktop' ? 'floating' : 'sheet'}
          reviewDrafts={reviewDrafts}
          onReviewDraftsChange={setReviewDrafts}
          confirmLabel={primaryLabel}
          onPrimaryAlt={target.kind === 'choose' && target.rememberedTripName ? () => { void doAdd('pickOther') } : undefined}
          primaryAltLabel="เลือกทริปอื่น"
          secondaryLabel={target.kind === 'choose' ? 'สร้างทริปใหม่' : undefined}
          onSecondary={target.kind === 'choose' ? () => { void doAdd('createTrip') } : undefined}
          error={formError}
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
