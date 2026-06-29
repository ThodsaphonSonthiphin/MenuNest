// frontend/src/pages/trips/TripsPage.tsx
import {useNavigate} from 'react-router-dom'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {useListTripsQuery} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store/index'
import {setCreateTripOpen} from './tripsSlice'
import {CreateTripDialog} from './components/CreateTripDialog'
import {getErrorMessage} from '../../shared/utils/getErrorMessage'
import './TripsPage.css'

export function TripsPage() {
  const nav = useNavigate()
  const dispatch = useAppDispatch()
  const open = useAppSelector(s => s.trips.createTripOpen)
  const {data: trips, isLoading, error} = useListTripsQuery()

  return (
    <section className="trips-page">
      <header className="trips-header">
        <h1>🧳 ทริปของฉัน</h1>
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

      <div className="trips-grid">
        {trips?.map(t => (
          <button
            key={t.id}
            className="trip-card"
            onClick={() => nav(`/trips/${t.id}`)}
          >
            <div className="trip-card-name">{t.name}</div>
            <div className="trip-card-meta">
              {t.destination ?? ''}{t.destination ? ' · ' : ''}{t.dayCount} วัน
            </div>
            <div className="trip-card-dates">{t.startDate}</div>
          </button>
        ))}
      </div>

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
