// frontend/src/pages/trips/tripsSlice.ts
import {createSlice} from '@reduxjs/toolkit'
import type {PayloadAction} from '@reduxjs/toolkit'
import type {PlaceCategory} from '../../shared/api/api'

export type TripTab = 'places' | 'itinerary'
export type PlacesView = 'map' | 'list'

interface TripsState {
  activeDayId: string | null
  activeTab: TripTab
  placesView: PlacesView
  placeCategoryFilter: PlaceCategory | 'all'
  activeStopId: string | null
  createTripOpen: boolean
  addPlaceOpen: boolean
  stopEditorStopId: string | null
}

const initialState: TripsState = {
  activeDayId: null, activeTab: 'itinerary', placesView: 'map',
  placeCategoryFilter: 'all', activeStopId: null,
  createTripOpen: false, addPlaceOpen: false, stopEditorStopId: null,
}

const tripsSlice = createSlice({
  name: 'trips',
  initialState,
  reducers: {
    setActiveDay(s, a: PayloadAction<string | null>) { s.activeDayId = a.payload },
    setActiveTab(s, a: PayloadAction<TripTab>) { s.activeTab = a.payload },
    setPlacesView(s, a: PayloadAction<PlacesView>) { s.placesView = a.payload },
    setPlaceCategoryFilter(s, a: PayloadAction<PlaceCategory | 'all'>) { s.placeCategoryFilter = a.payload },
    setActiveStop(s, a: PayloadAction<string | null>) { s.activeStopId = a.payload },
    setCreateTripOpen(s, a: PayloadAction<boolean>) { s.createTripOpen = a.payload },
    setAddPlaceOpen(s, a: PayloadAction<boolean>) { s.addPlaceOpen = a.payload },
    setStopEditor(s, a: PayloadAction<string | null>) { s.stopEditorStopId = a.payload },
  },
})

export const {
  setActiveDay, setActiveTab, setPlacesView, setPlaceCategoryFilter,
  setActiveStop, setCreateTripOpen, setAddPlaceOpen, setStopEditor,
} = tripsSlice.actions
export default tripsSlice.reducer
