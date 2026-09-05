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
import {formatTripDate, normalizeSortDirection} from './lib/tripsGrid'
import './trips-tokens.css'
import './TripsPage.css'

// Order-insensitive comparison, so a rebuilt URLSearchParams with the same
// pairs in a different order is not mistaken for a change.
function canonical(params: URLSearchParams): string {
  const copy = new URLSearchParams(params)
  copy.sort()
  return copy.toString()
}

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

    // Syncfusion reports the search two ways, and neither matches the obvious guess:
    //   - the action is `requestType: 'Searching'` (capitalised) with the term on `value`
    //   - the standing search is a predicate array whose term lives on `key`, not `value`
    // A search action is authoritative even when its term is empty (that is the clear).
    let searchString = ''
    if (event.action && String(event.action.requestType).toLowerCase() === 'searching') {
      searchString = typeof event.action.value === 'string' ? event.action.value : ''
    } else if (Array.isArray(event.search) && event.search.length > 0) {
      searchString = typeof event.search[0].key === 'string' ? event.search[0].key : ''
    }

    if (searchString) {
      // A changed term restarts paging from the first page.
      if (searchString !== search) p.set('skip', '0')
      p.set('search', searchString)
    } else {
      p.delete('search')
    }

    if (event.sort && event.sort.length > 0) {
      p.set('sortColumn', event.sort[0].name || event.sort[0].field)
      // The adaptor lower-cases the direction; the Grid's header only draws its
      // indicator for the capitalised form, and without that indicator it can
      // never toggle to Descending. See normalizeSortDirection.
      p.set('sortDirection', normalizeSortDirection(event.sort[0].direction))
    } else {
      p.delete('sortColumn')
      p.delete('sortDirection')
    }

    // The Grid raises onDataRequest from inside its own render, so navigating
    // synchronously would update router state mid-render — React flags this and
    // it re-renders the Grid, which fires again, looping until the grid freezes
    // under its spinner. Defer past the render, and skip no-op navigations.
    if (canonical(p) === canonical(searchParams)) return
    queueMicrotask(() => setSearchParams(p))
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
            sortSettings={{
              enabled: true,
              // Seeded from the URL for the same reason as the search term: the
              // list arrives sorted, so the header must show which column did it.
              columns: sortColumn
                ? [{field: sortColumn, direction: normalizeSortDirection(sortDirection)}]
                : [],
            }}
            filterSettings={{enabled: false}}
            searchSettings={{enabled: true, fields: ['name', 'destination'], value: search}}
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
              {/* `startDate` arrives as a DateOnly string, which the Grid's own
                  date `format` never touches — format it ourselves. */}
              <Column
                field="startDate"
                headerText="Date"
                width="120"
                valueAccessor={(props: any) => formatTripDate(props?.data?.startDate)}
              />
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
