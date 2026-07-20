import {useCallback, useEffect, useMemo} from 'react'
import './DiscoverPage.css'
import '../trips/trips-tokens.css'
import {useListMyPlacesQuery} from '../../shared/api/api'
import {useAppDispatch, useAppSelector} from '../../store'
import {setAnchor, setScope, setCategoryFilter, toggleSignal, setSelectedKey} from './discoverSlice'
import {applyDiscover} from './lib/discoverFilter'
import {DiscoverMap} from './components/DiscoverMap'
import {FilterBar} from './components/FilterBar'
import {PlaceBottomSheet} from './components/PlaceBottomSheet'
import {PlaceSheet} from './components/PlaceSheet'

export function DiscoverPage() {
  const dispatch = useAppDispatch()
  const {data: places = [], isLoading} = useListMyPlacesQuery()
  const {anchor, scope, categoryFilter, toggles, selectedKey} = useAppSelector((s) => s.discover)

  // Live location → anchor (ADR-027 pattern). Denied/unsupported → stays null (fit-all).
  useEffect(() => {
    if (!('geolocation' in navigator)) return
    navigator.geolocation.getCurrentPosition(
      (pos) => dispatch(setAnchor({lat: Math.round(pos.coords.latitude * 1e4) / 1e4, lng: Math.round(pos.coords.longitude * 1e4) / 1e4})),
      () => dispatch(setAnchor(null)),
      {timeout: 8000},
    )
  }, [dispatch])

  const views = useMemo(
    () => applyDiscover(places, {anchor, viewport: scope, category: categoryFilter, toggles, now: new Date()}),
    [places, anchor, scope, categoryFilter, toggles],
  )
  const selected = views.find((v) => v.key === selectedKey) ?? null

  // Memoized so DiscoverMap's marker-building effect (keyed on this callback
  // identity) doesn't rebuild its markers/clusterer on every parent render.
  const onMapSelect = useCallback((k: string) => dispatch(setSelectedKey(k)), [dispatch])
  const onMapScopeChange = useCallback((b: {north: number; south: number; east: number; west: number}) => dispatch(setScope(b)), [dispatch])

  return (
    <div className="discover-page">
      <div className="disc-topbar">
        <div className="disc-title-row"><span className="disc-title">ไปไหนดี</span></div>
        <FilterBar
          category={categoryFilter}
          toggles={toggles}
          onCategory={(c) => dispatch(setCategoryFilter(c))}
          onToggle={(k) => dispatch(toggleSignal(k))}
        />
      </div>

      <DiscoverMap
        places={views}
        anchor={anchor}
        selectedKey={selectedKey}
        onSelect={onMapSelect}
        onScopeChange={onMapScopeChange}
      />

      {selected ? (
        <PlaceSheet
          place={selected}
          onClose={() => dispatch(setSelectedKey(null))}
          onAddToTrip={() => { /* Task 9 */ }}
          onCreateTrip={() => { /* Task 9 */ }}
        />
      ) : (
        <PlaceBottomSheet places={views} onSelect={(k) => dispatch(setSelectedKey(k))} />
      )}

      {isLoading && <div className="disc-loading">กำลังโหลด…</div>}
    </div>
  )
}
