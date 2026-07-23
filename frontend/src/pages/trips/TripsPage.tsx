// frontend/src/pages/trips/TripsPage.tsx
import {useNavigate} from 'react-router-dom'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {useListTripsQuery, type TripDto} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store/index'
import {setCreateTripOpen} from './tripsSlice'
import {CreateTripDialog} from './components/CreateTripDialog'
import {SuitcaseIcon, RepeatIcon} from './components/TripFormIcons'
import {getErrorMessage} from '../../shared/utils/getErrorMessage'
import './trips-tokens.css'
import './TripsPage.css'

export function TripsPage() {
  const nav = useNavigate()
  const dispatch = useAppDispatch()
  const open = useAppSelector(s => s.trips.createTripOpen)
  const {data: trips, isLoading, error} = useListTripsQuery()

  const daily = trips?.filter(t => t.isDaily) ?? []
  const regular = trips?.filter(t => !t.isDaily) ?? []

  const dailyCard = (t: TripDto) => (
    <button
      key={t.id}
      className="trip-card trip-card--daily"
      data-testid="trip-card"
      onClick={() => nav(`/trips/${t.id}`)}
    >
      <div className="trip-card-name">{t.name}</div>
      <span className="trip-badge-daily"><RepeatIcon /> ประจำวัน</span>
      <div className="trip-card-today"><span className="dot" /> วันนี้</div>
    </button>
  )

  const regularCard = (t: TripDto) => (
    <button
      key={t.id}
      className="trip-card"
      data-testid="trip-card"
      onClick={() => nav(`/trips/${t.id}`)}
    >
      <div className="trip-card-name">{t.name}</div>
      <div className="trip-card-meta">
        {t.destination ?? ''}{t.destination ? ' · ' : ''}{t.dayCount} วัน
      </div>
      <div className="trip-card-dates">{t.startDate}</div>
    </button>
  )

  return (
    <section className="trips-page">
      <header className="trips-header">
        <h1><SuitcaseIcon className="trips-title-ic" /> ทริปของฉัน</h1>
        <Button
          color={Color.Primary}
          variant={Variant.Filled}
          onClick={() => dispatch(setCreateTripOpen(true))}
        >
          + ทริปใหม่
        </Button>
      </header>

      {isLoading && <p className="trips-muted">กำลังโหลด…</p>}
      {error && <p className="trips-field-error">{getErrorMessage(error)}</p>}
      {!isLoading && !error && trips?.length === 0 && (
        <p className="trips-empty">ยังไม่มีทริป — สร้างทริปแรกของคุณ</p>
      )}

      {daily.length > 0 && (
        <section className="trips-section">
          <div className="trips-section-lab"><RepeatIcon /> ประจำวัน</div>
          <div className="trips-grid">{daily.map(dailyCard)}</div>
        </section>
      )}

      {regular.length > 0 && (
        <section className="trips-section">
          {daily.length > 0 && <div className="trips-section-lab">ทริป</div>}
          <div className="trips-grid">{regular.map(regularCard)}</div>
        </section>
      )}

      {open && (
        <CreateTripDialog
          onClose={() => dispatch(setCreateTripOpen(false))}
          onCreated={(id) => {
            dispatch(setCreateTripOpen(false))
            nav(`/trips/${id}`)
          }}
        />
      )}
    </section>
  )
}
