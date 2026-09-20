// frontend/src/pages/trips/TripDetailPage.tsx
//
// The map-driven trip screen (menunest-234 … 241, issue #154 closing #6). ONE map fills the
// viewport; the day's plan floats over it in a **Plan sheet** (mobile/tablet) or a
// **Plan panel** (desktop). The tab pair, the segmented map/list switch, the 188px map band
// and the second full-screen capture map are all gone — they were the four things that made
// this screen "ใช้งานยากมาก".
import {useCallback, useEffect, useMemo, useState} from 'react'
import {useNavigate, useParams} from 'react-router-dom'
import {
  useAddStopMutation,
  useGetTripQuery,
  useListTripPlacesQuery,
  useSetStopVisitedMutation,
  useUpdateTripMutation,
} from '../../shared/api/api'
import type {TravelMode} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store/index'
import {setActiveDay, setAddMode, setPlaceEditor, setSelectedStop, setStopEditor, setViewerLocation} from './tripsSlice'
import {addStopDayLabel} from './lib/addStopCapture'
import {buildGhostPins, readGhostPref, showGhostLabels, writeGhostPref, type GhostPin} from './lib/ghostPins'
import {desktopFitPadding, mobileFitPadding} from './lib/fitPadding'
import type {Detent} from './lib/detent'
import {buildStopNavUrl} from './lib/navUrl'
import {getErrorMessage} from '../../shared/utils/getErrorMessage'
import {useBreakpoint} from '../../shared/hooks/useBreakpoint'
import {BottomSheet} from '../../shared/components/BottomSheet'
import {PlanPanel} from './components/PlanPanel'
import {PlanSummary} from './components/PlanSummary'
import {PlanContent} from './components/PlanContent'
import {CompactStopCard} from './components/CompactStopCard'
import {GhostPinCard} from './components/GhostPinCard'
import {PlaceEditorDialog} from './components/PlaceEditorDialog'
import {StopDetailSheet} from './components/StopDetailSheet'
import {StopEditorDialog} from './components/StopEditorDialog'
import {TripMap} from './components/TripMap'
import {TripDateEditor} from './components/TripDateEditor'
import {DailyToggle} from './components/DailyToggle'
import {EditTripDialog} from './components/EditTripDialog'
import {ChevronLeftIcon, CloseIcon, GhostDotIcon, LocateIcon, PencilIcon, PlusIcon} from './components/TripFormIcons'
import {useDayRoute} from './hooks/useDayRoute'
import {usePlan} from './hooks/usePlan'
import {appInsights} from '../../shared/telemetry/appInsights'
import './trips-tokens.css'
import './TripDetailPage.css'

/** EditTripDialog's own ceiling — the `+ วัน` chip must not step past it. */
const MAX_DAYS = 30

export function TripDetailPage() {
  const {tripId = ''} = useParams()
  const navigate = useNavigate()
  const dispatch = useAppDispatch()
  const bp = useBreakpoint()
  const isDesktop = bp === 'desktop'

  const addMode = useAppSelector((s) => s.trips.addMode)
  const selectedStopId = useAppSelector((s) => s.trips.selectedStopId)
  const placeEditorPlaceId = useAppSelector((s) => s.trips.placeEditorPlaceId)
  const editorStopId = useAppSelector((s) => s.trips.stopEditorStopId)

  // Working state, not preferences: a Trip opens at `summary` every Day, every time
  // (spec §4.1), and the panel opens expanded.
  const [detent, setDetent] = useState<Detent>('summary')
  const [sheetHeight, setSheetHeight] = useState(150)
  const [panelCollapsed, setPanelCollapsed] = useState(false)
  const [ghostsVisible, setGhostsVisible] = useState(readGhostPref)
  const [dateError, setDateError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [editOpen, setEditOpen] = useState(false)
  const [detailStopId, setDetailStopId] = useState<string | null>(null)
  const [ghostCard, setGhostCard] = useState<GhostPin | null>(null)
  const [recenterNonce, setRecenterNonce] = useState(0)

  // Capture the viewer's live location once per trip-detail visit — feeds the
  // Approach leg into each Day's first Stop (ADR-027). Denied, unsupported, or a
  // failed/timed-out read leave viewerLocation null: identical to today's
  // no-Approach-leg rendering (ADR-027 decision 4), no error surfaced here.
  useEffect(() => {
    if (!('geolocation' in navigator)) return
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        dispatch(setViewerLocation({
          // Rounded to ~11m so repeated reads at the same spot keep hitting the
          // same RTK Query cache entry instead of refetching on float jitter.
          lat: Math.round(pos.coords.latitude * 10000) / 10000,
          lng: Math.round(pos.coords.longitude * 10000) / 10000,
        }))
      },
      () => {},
    )
  }, [dispatch, recenterNonce])

  // Leaving this Trip (unmount) or switching Trips drops both the armed map and the
  // selection, so neither can leak into the next Trip's screen.
  useEffect(() => {
    return () => {
      dispatch(setAddMode(false))
      dispatch(setSelectedStop(null))
    }
  }, [dispatch, tripId])

  const {data: trip, isLoading: tripLoading, isError: tripError} = useGetTripQuery(tripId, {skip: !tripId})
  const {data: places} = useListTripPlacesQuery(tripId, {skip: !tripId})
  const editingPlace = (places ?? []).find((p) => p.id === placeEditorPlaceId)

  // Two hooks over ONE set of RTK Query caches: the route the map draws and the plan the
  // sheet lists can never drift apart, and neither fires a request the other did not.
  const dayRoute = useDayRoute(tripId)
  const plan = usePlan(tripId, trip)

  const [addStop] = useAddStopMutation()
  const [setStopVisited] = useSetStopVisitedMutation()
  const [updateTrip, {isLoading: addingDay}] = useUpdateTripMutation()

  // Built once: the toggle hides the LAYER, but the `คลัง · N` count must keep reporting
  // how many Places are out there, or the control that reveals them cannot say what it does.
  const allGhosts = useMemo(
    () => buildGhostPins(places ?? [], plan.dayList, plan.dayId),
    [places, plan.dayList, plan.dayId],
  )
  const ghostPins = ghostsVisible ? allGhosts : []

  // Padding is memoised on the SETTLED detent / collapsed state — a new object identity is
  // what re-runs `fitBounds` when the visible area moves without the container resizing
  // (spec §6.2). Keying it on a live drag height would refit the camera on every frame.
  const viewportH = typeof window === 'undefined' ? 0 : window.innerHeight
  const fitPadding = useMemo(
    () => (isDesktop ? desktopFitPadding(panelCollapsed) : mobileFitPadding(sheetHeight, viewportH)),
    [isDesktop, panelCollapsed, sheetHeight, viewportH],
  )

  // Tapping a PIN drops the sheet to summary, so the Compact stop card is the thing in front
  // of the user with the most map still showing (menunest-238, mock frame 4).
  const selectStopFromMap = useCallback((stopId: string | null) => {
    dispatch(setSelectedStop(stopId))
    setGhostCard(null)
    if (stopId) setDetent('summary')
  }, [dispatch])

  // Tapping a CARD selects too — but leaves the detent alone, because the user is reading
  // the list. On the panel, where no Compact stop card exists to carry `ดูทั้งหมด`, the tap
  // also opens the detail, which is what the card's chevron has always promised (#34).
  const activateStopFromList = useCallback((stopId: string) => {
    dispatch(setSelectedStop(stopId))
    setGhostCard(null)
    if (isDesktop) setDetailStopId(stopId)
  }, [dispatch, isDesktop])

  const selectGhost = useCallback((g: GhostPin) => {
    dispatch(setSelectedStop(null))
    setGhostCard(g)
    setDetent('summary')
  }, [dispatch])

  // Not-found / error guard (after all hooks, so Rules of Hooks hold). Covers
  // deep-links to a trip the user does not own or that was deleted.
  if (!tripId || tripError || (!tripLoading && !trip)) {
    return (
      <section className="trip-detail">
        <p className="trips-empty">ไม่พบทริปนี้ — อาจถูกลบ หรือลิงก์ไม่ถูกต้อง</p>
      </section>
    )
  }

  // Top-bar date: mirror the backend's OWN projection guard from a single source.
  // GetItinerary projects day[0].date to today exactly when its day list has one Day
  // flagged current-time-start (ADR-054/055/056); key on that same list (dayRoute.days —
  // the itinerary query useDayRoute already fired) so the header and the server can never
  // disagree, and so no second itinerary subscription is needed.
  const currentDay = dayRoute.days.length === 1 && dayRoute.days[0]?.useCurrentTimeAsStart === true
  const overrideDate = currentDay ? dayRoute.days[0].date.slice(0, 10) : undefined

  // menunest-240: the armed map commits to the ACTIVE Day. There is no separate
  // "which Day am I adding to" flag any more — one map, one active Day.
  const dayLabel = plan.dayId ? addStopDayLabel(plan.dayList, plan.dayId, trip?.destination) : null
  const addStopContext =
    plan.dayId && dayLabel
      ? {
          dayId: plan.dayId,
          dayLabel,
          dayNumber: plan.dayNumber,
          travelMode: (trip?.defaultTravelMode ?? 'Drive') as TravelMode,
        }
      : null

  const selected = selectedStopId ? plan.remaining.find((s) => s.stop.id === selectedStopId) ?? null : null
  const selectedPlace = selected ? plan.placesById[selected.stop.tripPlaceId] : undefined
  const detail = detailStopId ? plan.scheduled.find((s) => s.stop.id === detailStopId) ?? null : null
  const detailPlace = detail ? plan.placesById[detail.stop.tripPlaceId] : undefined

  const addGhostToDay = async (g: GhostPin) => {
    if (!plan.dayId) return
    try {
      await addStop({
        tripId,
        dayId: plan.dayId,
        tripPlaceId: g.id,
        dwellMinutes: 60,
        travelModeToReach: (trip?.defaultTravelMode ?? 'Drive') as TravelMode,
      }).unwrap()
      setGhostCard(null)
    } catch (err) {
      setActionError(getErrorMessage(err))
    }
  }

  const addDay = async () => {
    if (!trip || trip.isDaily || trip.dayCount >= MAX_DAYS) return
    try {
      await updateTrip({
        id: trip.id,
        name: trip.name,
        destination: trip.destination,
        startDate: trip.startDate,
        dayCount: trip.dayCount + 1,
        defaultTravelMode: trip.defaultTravelMode,
      }).unwrap()
    } catch (err) {
      setActionError(getErrorMessage(err))
    }
  }

  // ── The plan, in whichever container this breakpoint calls for ───────────────
  const summaryBlock =
    ghostCard ? (
      <GhostPinCard
        ghost={ghostCard}
        dayNumber={plan.dayNumber}
        onAddToDay={() => { void addGhostToDay(ghostCard) }}
        onGoToDay={() => {
          if (ghostCard.scheduledDayId) dispatch(setActiveDay(ghostCard.scheduledDayId))
          setGhostCard(null)
        }}
        onEditPlace={() => { dispatch(setPlaceEditor(ghostCard.id)); setGhostCard(null) }}
        onClose={() => setGhostCard(null)}
      />
    ) : selected && selectedPlace && !isDesktop ? (
      <CompactStopCard
        place={selectedPlace}
        ordinal={plan.remaining.indexOf(selected) + 1}
        dayNumber={plan.dayNumber}
        arrival={selected.arrival}
        depart={selected.depart}
        dwell={selected.stop.dwellMinutes}
        flag={selected.flag}
        arrivalReading={plan.stopWeather[selected.stop.id]?.arrival}
        navUrl={buildStopNavUrl(selectedPlace, selected.stop.travelModeToReach)}
        isVisited={selected.stop.isVisited}
        onNavigate={() =>
          appInsights.trackEvent(
            {name: 'TripNavHandoff'},
            {scope: 'stop', travelMode: selected.stop.travelModeToReach, hasPlaceId: !!selectedPlace.googlePlaceId},
          )
        }
        onEdit={() => dispatch(setStopEditor(selected.stop.id))}
        onToggleVisited={async (next) => {
          try {
            await setStopVisited({tripId, stopId: selected.stop.id, isVisited: next}).unwrap()
            dispatch(setSelectedStop(null))
          } catch (err) {
            setActionError(getErrorMessage(err))
          }
        }}
        onOpenDetail={() => setDetailStopId(selected.stop.id)}
        onClose={() => dispatch(setSelectedStop(null))}
      />
    ) : (
      <PlanSummary tripId={tripId} plan={plan} isDaily={trip?.isDaily ?? false} onError={setActionError} />
    )

  const planBody = <PlanContent tripId={tripId} plan={plan} onActivateStop={activateStopFromList} />

  const tripHeader = (
    <div className="trip-head">
      <button type="button" className="trip-head-btn" aria-label="ย้อนกลับ" onClick={() => navigate('/trips')}>
        <ChevronLeftIcon />
      </button>
      <div className="trip-head-txt">
        <div className="trip-head-name">{trip?.name ?? '…'}</div>
        {trip && (
          <div className="trip-head-meta">
            <TripDateEditor trip={trip} overrideDate={overrideDate} locked={currentDay} onError={setDateError} />
            {trip.dayCount != null && <> · {trip.dayCount} วัน</>}
            <DailyToggle trip={trip} onError={setDateError} />
          </div>
        )}
      </div>
      {trip && (
        <button
          type="button"
          className="trip-head-btn"
          aria-label="แก้ไขทริป"
          title="แก้ไขทริป"
          onClick={() => setEditOpen(true)}
        >
          <PencilIcon />
        </button>
      )}
    </div>
  )

  // Chips only earn their row on a multi-day Trip; a one-day Trip has nothing to switch to.
  const dayChips = plan.dayList.length > 1 || (trip && !trip.isDaily && trip.dayCount < MAX_DAYS) ? (
    <div className="day-chips" role="tablist" aria-label="วันในทริป">
      {plan.dayList.map((d, i) => (
        <button
          key={d.id}
          type="button"
          role="tab"
          aria-selected={d.id === plan.dayId}
          className={`day-chip${d.id === plan.dayId ? ' on' : ''}`}
          onClick={() => dispatch(setActiveDay(d.id))}
        >
          วัน {i + 1}
        </button>
      ))}
      {trip && !trip.isDaily && trip.dayCount < MAX_DAYS && (
        <button type="button" className="day-chip add" disabled={addingDay} onClick={() => { void addDay() }}>
          + วัน
        </button>
      )}
    </div>
  ) : null

  return (
    <section className={`trip-detail map-driven${isDesktop ? ' desktop' : ''}`} data-testid="trip-detail">
      {/* The ONE map. It is the page, and it keeps pan/zoom/tap at every detent — nothing
          below is modal over it (menunest-234). */}
      <TripMap
        places={places ?? []}
        route={dayRoute.route}
        segments={dayRoute.segments}
        summaryLabel={dayRoute.dayLabel}
        // Desktop only: on a phone the floating top bar already owns that corner, and the
        // sheet header states the same day roll-up (mock frames 1-4 carry no badge).
        summaryText={isDesktop ? dayRoute.summaryText : undefined}
        viewerLocation={dayRoute.viewerLocation}
        fitPadding={fitPadding}
        ghostPins={ghostPins}
        ghostLabels={showGhostLabels(ghostPins.length)}
        recenterNonce={recenterNonce}
        selectedStopId={selectedStopId}
        onSelectStop={selectStopFromMap}
        onSelectGhost={selectGhost}
        addMode={addMode}
        addStopContext={addMode ? addStopContext : null}
        tripId={tripId}
        onExitAddMode={() => dispatch(setAddMode(false))}
      />

      {/* Floating chrome. Hidden while armed: capture owns the top of the map (R8.3), and
          leaving the browse chrome up over a capture banner reads as two screens at once. */}
      {!addMode && (
        <>
          {!isDesktop && (
            <div className="trip-topchrome">
              {tripHeader}
              {dayChips}
            </div>
          )}

          <div className={`map-controls${isDesktop ? ' desktop' : ''}`} style={!isDesktop ? {bottom: sheetHeight + 12} : undefined}>
            {allGhosts.length > 0 && (
              <button
                type="button"
                className={`map-ctl wide${ghostsVisible ? ' on' : ''}`}
                aria-pressed={ghostsVisible}
                aria-label={ghostsVisible ? 'ซ่อนสถานที่ในคลังบนแผนที่' : 'แสดงสถานที่ในคลังบนแผนที่'}
                onClick={() => {
                  const next = !ghostsVisible
                  setGhostsVisible(next)
                  writeGhostPref(next)
                }}
              >
                <GhostDotIcon /> คลัง {allGhosts.length}
              </button>
            )}
            {/* Hidden until geolocation has actually produced a point, so it is never a
                button that does nothing (the pattern Discover already uses). */}
            {dayRoute.viewerLocation && (
              <button
                type="button"
                className="map-ctl"
                aria-label="กลับไปตำแหน่งของฉัน"
                onClick={() => setRecenterNonce((n) => n + 1)}
              >
                <LocateIcon />
              </button>
            )}
            <button
              type="button"
              className="map-ctl primary"
              aria-label="เพิ่มสถานที่"
              onClick={() => dispatch(setAddMode(true))}
            >
              <PlusIcon />
            </button>
          </div>
        </>
      )}

      {addMode && (
        // The armed map is still the map — one tap out, always in the same place.
        <button
          type="button"
          className="map-disarm"
          aria-label="ออกจากโหมดเพิ่มสถานที่"
          onClick={() => dispatch(setAddMode(false))}
        >
          <CloseIcon />
        </button>
      )}

      {(dateError || actionError) && (
        <p className="trip-float-error" role="alert">{dateError ?? actionError}</p>
      )}

      {/* Armed capture takes the whole surface (ADR-163), so the plan container steps aside
          rather than sitting under the capture sheet fighting for the same taps. */}
      {!addMode && (isDesktop ? (
        <PlanPanel
          collapsed={panelCollapsed}
          onToggle={() => setPanelCollapsed((v) => !v)}
          header={<>{tripHeader}{dayChips}{summaryBlock}</>}
        >
          {planBody}
        </PlanPanel>
      ) : (
        <BottomSheet
          detent={detent}
          onDetentChange={setDetent}
          onSettledHeightChange={setSheetHeight}
          header={summaryBlock}
        >
          {planBody}
        </BottomSheet>
      ))}

      {detail && detailPlace && plan.day && (
        <StopDetailSheet
          place={detailPlace}
          stopId={detail.stop.id}
          arrival={detail.arrival}
          depart={detail.depart}
          dwell={detail.stop.dwellMinutes}
          flag={detail.flag}
          tripMonth={plan.tripMonth}
          dayNumber={plan.dayNumber}
          ordinal={plan.remaining.indexOf(detail) + 1}
          navUrl={buildStopNavUrl(detailPlace, detail.stop.travelModeToReach)}
          nowReading={plan.stopWeather[detail.stop.id]?.now}
          arrivalReading={plan.stopWeather[detail.stop.id]?.arrival}
          weatherLoading={
            (plan.stopWeather[detail.stop.id]?.nowLoading ?? false) ||
            (plan.stopWeather[detail.stop.id]?.arrivalLoading ?? false)
          }
          planner={{tripId, day: plan.day, tripDayCount: plan.dayList.length, isDaily: trip?.isDaily ?? false}}
          onEdit={() => {
            setDetailStopId(null)
            dispatch(setStopEditor(detail.stop.id))
          }}
          onNavigate={() =>
            appInsights.trackEvent(
              {name: 'TripNavHandoff'},
              {scope: 'stop', travelMode: detail.stop.travelModeToReach, hasPlaceId: !!detailPlace.googlePlaceId},
            )
          }
          onToggleVisited={async (next) => {
            try {
              await setStopVisited({tripId, stopId: detail.stop.id, isVisited: next}).unwrap()
              setDetailStopId(null)
            } catch (err) {
              setActionError(getErrorMessage(err))
            }
          }}
          onClose={() => setDetailStopId(null)}
        />
      )}

      {editorStopId && plan.day && (
        <StopEditorDialog
          tripId={tripId}
          day={plan.day}
          dayNumber={plan.dayNumber}
          stopId={editorStopId}
          placesById={plan.placesById}
          onClose={() => dispatch(setStopEditor(null))}
        />
      )}

      {editingPlace && (
        <PlaceEditorDialog tripId={tripId} place={editingPlace} onClose={() => dispatch(setPlaceEditor(null))} />
      )}

      {trip && editOpen && (
        <EditTripDialog
          trip={trip}
          days={dayRoute.days}
          places={places ?? []}
          overrideDate={overrideDate}
          locked={currentDay}
          onClose={() => setEditOpen(false)}
        />
      )}
    </section>
  )
}
