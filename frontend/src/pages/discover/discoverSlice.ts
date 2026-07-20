import {createSlice} from '@reduxjs/toolkit'
import type {PayloadAction} from '@reduxjs/toolkit'
import type {PlaceCategory} from '../../shared/api/api'
import type {DiscoverToggles, ViewportBounds} from './lib/discoverFilter'

interface DiscoverState {
  anchor: {lat: number; lng: number} | null
  scope: ViewportBounds | null
  categoryFilter: PlaceCategory | 'all'
  toggles: DiscoverToggles
  selectedKey: string | null
}

export const initialState: DiscoverState = {
  anchor: null,
  scope: null,
  categoryFilter: 'all',
  toggles: {openNow: true, season: true, bestTime: false, hideVisited: true},
  selectedKey: null,
}

const discoverSlice = createSlice({
  name: 'discover',
  initialState,
  reducers: {
    setAnchor(s, a: PayloadAction<{lat: number; lng: number} | null>) { s.anchor = a.payload },
    setScope(s, a: PayloadAction<ViewportBounds | null>) { s.scope = a.payload },
    setCategoryFilter(s, a: PayloadAction<PlaceCategory | 'all'>) { s.categoryFilter = a.payload },
    toggleSignal(s, a: PayloadAction<keyof DiscoverToggles>) { s.toggles[a.payload] = !s.toggles[a.payload] },
    setSelectedKey(s, a: PayloadAction<string | null>) { s.selectedKey = a.payload },
  },
})

export const {setAnchor, setScope, setCategoryFilter, toggleSignal, setSelectedKey} = discoverSlice.actions
export default discoverSlice.reducer
