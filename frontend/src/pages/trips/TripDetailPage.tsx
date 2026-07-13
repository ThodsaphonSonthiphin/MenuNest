// frontend/src/pages/trips/TripDetailPage.tsx
import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { useGetTripQuery, useListTripPlacesQuery } from '../../shared/api/api'
import { useAppDispatch, useAppSelector } from '../../store/index'
import { setActiveTab, setPlacesView, setAddMode, setViewerLocation, setPlaceEditor } from './tripsSlice'
import { useBreakpoint } from '../../shared/hooks/useBreakpoint'
import { SegmentedTabs } from './components/SegmentedTabs'
import { PlaceCard } from './components/PlaceCard'
import { PlaceEditorDialog } from './components/PlaceEditorDialog'
import { ItineraryTab } from './components/ItineraryTab'
import { TripMap } from './components/TripMap'
import { TripDateEditor } from './components/TripDateEditor'
import { useDayRoute } from './hooks/useDayRoute'
import './trips-tokens.css'
import './TripDetailPage.css'

export function TripDetailPage() {
  const { tripId = '' } = useParams()
  const dispatch = useAppDispatch()
  const tab = useAppSelector((s) => s.trips.activeTab)
  const placesView = useAppSelector((s) => s.trips.placesView)
  const addMode = useAppSelector((s) => s.trips.addMode)
  const placeEditorPlaceId = useAppSelector((s) => s.trips.placeEditorPlaceId)
  const bp = useBreakpoint()
  // Error surfaced by the inline trip-date edit (rescheduling failed).
  const [dateError, setDateError] = useState<string | null>(null)

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
  }, [dispatch])

  const { data: trip, isLoading: tripLoading, isError: tripError } = useGetTripQuery(tripId, { skip: !tripId })
  const { data: places } = useListTripPlacesQuery(tripId, { skip: !tripId })
  const editingPlace = (places ?? []).find((p) => p.id === placeEditorPlaceId)
  // Active day's ordered, time-aware stops → numbered pins + route on the map.
  // Called unconditionally (before the not-found guard) to keep Rules of Hooks.
  const dayRoute = useDayRoute(tripId)

  const isDesktop = bp === 'desktop'

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

  // ── Desktop split: dark top-bar + two-pane grid ───────────────────────────
  if (isDesktop) {
    return (
      <section className="trip-detail desktop">
        <header className="trip-topbar">
          <span className="trip-topbar-name">🗺️ {trip?.name ?? '…'}</span>
          {trip && (
            <span className="trip-topbar-meta">
              {trip.destination && <>{trip.destination} · </>}
              <TripDateEditor trip={trip} overrideDate={overrideDate} locked={currentDay} onError={setDateError} />
              {trip.dayCount != null && <> · {trip.dayCount} วัน</>}
            </span>
          )}
          {dateError && <span className="trip-topbar-error">{dateError}</span>}
        </header>

        <div className="trip-detail-body">
        {/* Left column — itinerary / places panel */}
        <div className="trip-detail-col-left">
          <SegmentedTabs
            value={tab}
            onChange={(v) => dispatch(setActiveTab(v))}
            options={[
              { label: 'คลังสถานที่', value: 'places' },
              { label: 'แผนเที่ยว', value: 'itinerary' },
            ]}
          />

          {tab === 'places' && (
            <div className="trip-places">
              <div className="trip-places-toolbar">
                <Button
                  color={Color.Primary}
                  variant={addMode ? Variant.Outlined : Variant.Filled}
                  onClick={() => dispatch(setAddMode(!addMode))}
                >
                  {addMode ? 'เสร็จ' : '+ เพิ่มสถานที่'}
                </Button>
              </div>

              {places?.length ? (
                <div className="place-list">
                  {places.map((p) => (
                    <PlaceCard key={p.id} place={p} onClick={() => dispatch(setPlaceEditor(p.id))} />
                  ))}
                </div>
              ) : (
                <p className="trips-empty">
                  ยังไม่มีสถานที่ — วางลิงก์จาก Google Maps เพื่อเริ่ม
                </p>
              )}
            </div>
          )}

          {tab === 'itinerary' && <ItineraryTab tripId={tripId} />}
        </div>

        {/* Right column — persistent map. In the itinerary tab it shows the
            active day's numbered route; otherwise all saved places. */}
        <div className="trip-detail-col-right">
          <TripMap
            places={places ?? []}
            route={tab === 'itinerary' ? dayRoute.route : undefined}
            segments={tab === 'itinerary' ? dayRoute.segments : undefined}
            summaryLabel={dayRoute.dayLabel}
            summaryText={dayRoute.summaryText}
            viewerLocation={tab === 'itinerary' ? dayRoute.viewerLocation : undefined}
            addMode={tab === 'places' && addMode}
            tripId={tripId}
            onExitAddMode={() => dispatch(setAddMode(false))}
          />
        </div>
        </div>
        {editingPlace && (
          <PlaceEditorDialog tripId={tripId} place={editingPlace} onClose={() => dispatch(setPlaceEditor(null))} />
        )}
      </section>
    )
  }

  // ── Mobile / tablet: single column, tabbed ────────────────────────────────
  return (
    <section className="trip-detail">
      <header className="trip-detail-header">
        <div className="trip-detail-name">{trip?.name ?? '…'}</div>
        {trip && (
          <div className="trip-detail-meta">
            {trip.destination && <>{trip.destination} · </>}
            <TripDateEditor trip={trip} overrideDate={overrideDate} locked={currentDay} onError={setDateError} />
            {trip.dayCount != null && <> · {trip.dayCount} วัน</>}
          </div>
        )}
        {dateError && <p className="trips-field-error">{dateError}</p>}
      </header>

      <SegmentedTabs
        value={tab}
        onChange={(v) => dispatch(setActiveTab(v))}
        options={[
          { label: 'คลังสถานที่', value: 'places' },
          { label: 'แผนเที่ยว', value: 'itinerary' },
        ]}
      />

      {tab === 'places' && (
        <div className="trip-places">
          <div className="trip-places-toolbar">
            <SegmentedTabs
              value={placesView}
              onChange={(v) => dispatch(setPlacesView(v))}
              options={[
                { label: 'แผนที่', value: 'map' },
                { label: 'รายการ', value: 'list' },
              ]}
            />
            <Button
              color={Color.Primary}
              variant={addMode ? Variant.Outlined : Variant.Filled}
              onClick={() => {
                if (addMode) { dispatch(setAddMode(false)) }
                else { dispatch(setPlacesView('map')); dispatch(setAddMode(true)) }
              }}
            >
              {addMode ? 'เสร็จ' : '+ เพิ่มสถานที่'}
            </Button>
          </div>

          {placesView === 'map' ? (
            <TripMap
              places={places ?? []}
              addMode={addMode}
              tripId={tripId}
              onExitAddMode={() => dispatch(setAddMode(false))}
            />
          ) : places?.length ? (
            <div className="place-list">
              {places.map((p) => (
                <PlaceCard key={p.id} place={p} onClick={() => dispatch(setPlaceEditor(p.id))} />
              ))}
            </div>
          ) : (
            <p className="trips-empty">
              ยังไม่มีสถานที่ — วางลิงก์จาก Google Maps เพื่อเริ่ม
            </p>
          )}
        </div>
      )}

      {tab === 'itinerary' && <ItineraryTab tripId={tripId} dayRoute={dayRoute} />}

      {editingPlace && (
        <PlaceEditorDialog tripId={tripId} place={editingPlace} onClose={() => dispatch(setPlaceEditor(null))} />
      )}
    </section>
  )
}
