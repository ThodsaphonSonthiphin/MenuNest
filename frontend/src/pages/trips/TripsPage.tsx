// frontend/src/pages/trips/TripsPage.tsx
import {useNavigate, useSearchParams} from 'react-router-dom'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {Grid, Columns, Column} from '@syncfusion/react-grid'
import {useListTripsQuery} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store/index'
import {setCreateTripOpen} from './tripsSlice'
import {CreateTripDialog} from './components/CreateTripDialog'
import {SuitcaseIcon} from './components/TripFormIcons'
import {getErrorMessage} from '../../shared/utils/getErrorMessage'
import './trips-tokens.css'
import './TripsPage.css'

export function TripsPage() {
  const nav = useNavigate()
  const dispatch = useAppDispatch()
  const [searchParams, setSearchParams] = useSearchParams()
  const open = useAppSelector(s => s.trips.createTripOpen)

  const skip = parseInt(searchParams.get('skip') || '0', 10)
  const take = parseInt(searchParams.get('take') || '10', 10)
  const search = searchParams.get('search') || ''
  const sortColumn = searchParams.get('sortColumn') || ''
  const sortDirection = searchParams.get('sortDirection') || ''

  const {data, error} = useListTripsQuery({
    skip, take, search, sortColumn, sortDirection
  })

  const handleDataRequest = (event: any) => {
    const p = new URLSearchParams(searchParams)
    
    if (event.skip !== undefined) p.set('skip', event.skip.toString())
    if (event.take !== undefined) p.set('take', event.take.toString())
    
    if (event.search && event.search.length > 0 && event.search[0].value) {
      p.set('search', event.search[0].value)
      // Reset to first page on search
      if (p.get('search') !== search) {
         p.set('skip', '0')
      }
    } else {
      p.delete('search')
    }

    if (event.sort && event.sort.length > 0) {
      p.set('sortColumn', event.sort[0].name || event.sort[0].field)
      p.set('sortDirection', event.sort[0].direction)
    } else {
      p.delete('sortColumn')
      p.delete('sortDirection')
    }

    setSearchParams(p)
  }

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

      {error && <p className="trips-field-error">{getErrorMessage(error)}</p>}
      
      {!error && (
        <div className="trips-grid-container" style={{ marginTop: '24px' }}>
          <Grid 
            dataSource={{ result: data?.result || [], count: data?.count || 0 }}
            sortSettings={{enabled: true}}
            filterSettings={{enabled: false}}
            searchSettings={{enabled: true}}
            toolbar={['Search']}
            pageSettings={{ enabled: true, pageSize: take, currentPage: (skip / take) + 1, totalRecordsCount: data?.count || 0 }}
            onDataRequest={handleDataRequest}
            onRowSelect={(args: any) => {
              if (args.data && args.data.id) {
                nav(`/trips/${args.data.id}`)
              }
            }}
            enableHover={true}
          >
            <Columns>
              <Column field="name" headerText="Trip Name" width="200" />
              <Column field="destination" headerText="Destination" width="150" />
              <Column field="startDate" headerText="Date" width="120" format="yMd" type="date" />
              <Column field="dayCount" headerText="Days" width="80" textAlign="Right" />
            </Columns>
          </Grid>
        </div>
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
