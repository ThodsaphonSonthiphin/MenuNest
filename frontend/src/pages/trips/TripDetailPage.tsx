// frontend/src/pages/trips/TripDetailPage.tsx
import { useParams } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { useGetTripQuery, useListTripPlacesQuery } from '../../shared/api/api'
import { useAppDispatch, useAppSelector } from '../../store/index'
import { setActiveTab, setPlacesView, setAddPlaceOpen } from './tripsSlice'
import { useBreakpoint } from '../../shared/hooks/useBreakpoint'
import { SegmentedTabs } from './components/SegmentedTabs'
import { PlaceCard } from './components/PlaceCard'
import { AddPlaceSheet } from './components/AddPlaceSheet'
import { ItineraryTab } from './components/ItineraryTab'
import { TripMap } from './components/TripMap'
import './trips-tokens.css'
import './TripDetailPage.css'

export function TripDetailPage() {
  const { tripId = '' } = useParams()
  const dispatch = useAppDispatch()
  const tab = useAppSelector((s) => s.trips.activeTab)
  const placesView = useAppSelector((s) => s.trips.placesView)
  const addOpen = useAppSelector((s) => s.trips.addPlaceOpen)
  const bp = useBreakpoint()

  const { data: trip, isLoading: tripLoading, isError: tripError } = useGetTripQuery(tripId, { skip: !tripId })
  const { data: places } = useListTripPlacesQuery(tripId, { skip: !tripId })

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

  // ── Desktop split: two-pane grid ──────────────────────────────────────────
  if (isDesktop) {
    return (
      <section className="trip-detail desktop">
        {/* Left column — itinerary / places panel */}
        <div className="trip-detail-col-left">
          <header className="trip-detail-header">
            <div className="trip-detail-name">{trip?.name ?? '…'}</div>
            <div className="trip-detail-meta">
              {trip?.destination}
              {trip?.destination ? ' · ' : ''}
              {trip?.dayCount != null ? `${trip.dayCount} วัน` : ''}
            </div>
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
                <Button
                  color={Color.Primary}
                  variant={Variant.Filled}
                  onClick={() => dispatch(setAddPlaceOpen(true))}
                >
                  + เพิ่มสถานที่
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

          {addOpen && (
            <AddPlaceSheet
              tripId={tripId}
              onClose={() => dispatch(setAddPlaceOpen(false))}
            />
          )}
        </div>

        {/* Right column — persistent map */}
        <div className="trip-detail-col-right">
          <TripMap places={places ?? []} />
        </div>
      </section>
    )
  }

  // ── Mobile / tablet: single column, tabbed ────────────────────────────────
  return (
    <section className="trip-detail">
      <header className="trip-detail-header">
        <div className="trip-detail-name">{trip?.name ?? '…'}</div>
        <div className="trip-detail-meta">
          {trip?.destination}
          {trip?.destination ? ' · ' : ''}
          {trip?.dayCount != null ? `${trip.dayCount} วัน` : ''}
        </div>
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
              variant={Variant.Filled}
              onClick={() => dispatch(setAddPlaceOpen(true))}
            >
              + เพิ่มสถานที่
            </Button>
          </div>

          {placesView === 'map' ? (
            <TripMap places={places ?? []} />
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

      {tab === 'itinerary' && <ItineraryTab tripId={tripId} />}

      {addOpen && (
        <AddPlaceSheet
          tripId={tripId}
          onClose={() => dispatch(setAddPlaceOpen(false))}
        />
      )}
    </section>
  )
}
