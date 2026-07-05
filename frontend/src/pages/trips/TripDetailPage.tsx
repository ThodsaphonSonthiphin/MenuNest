// frontend/src/pages/trips/TripDetailPage.tsx
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { useGetTripQuery, useListTripPlacesQuery } from '../../shared/api/api'
import { useAppDispatch, useAppSelector } from '../../store/index'
import { setActiveTab, setPlacesView, setAddMode } from './tripsSlice'
import { useBreakpoint } from '../../shared/hooks/useBreakpoint'
import { SegmentedTabs } from './components/SegmentedTabs'
import { PlaceCard } from './components/PlaceCard'
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
  const bp = useBreakpoint()
  // Error surfaced by the inline trip-date edit (rescheduling failed).
  const [dateError, setDateError] = useState<string | null>(null)

  const { data: trip, isLoading: tripLoading, isError: tripError } = useGetTripQuery(tripId, { skip: !tripId })
  const { data: places } = useListTripPlacesQuery(tripId, { skip: !tripId })
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

  // ── Desktop split: dark top-bar + two-pane grid ───────────────────────────
  if (isDesktop) {
    return (
      <section className="trip-detail desktop">
        <header className="trip-topbar">
          <span className="trip-topbar-name">🗺️ {trip?.name ?? '…'}</span>
          {trip && (
            <span className="trip-topbar-meta">
              {trip.destination && <>{trip.destination} · </>}
              <TripDateEditor trip={trip} onError={setDateError} />
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
                    <PlaceCard key={p.id} place={p} />
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
            addMode={tab === 'places' && addMode}
            tripId={tripId}
            onExitAddMode={() => dispatch(setAddMode(false))}
          />
        </div>
        </div>
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
            <TripDateEditor trip={trip} onError={setDateError} />
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
                <PlaceCard key={p.id} place={p} />
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
    </section>
  )
}
