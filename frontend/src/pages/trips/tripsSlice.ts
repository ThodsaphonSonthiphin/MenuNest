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
  addMode: boolean
  itineraryMapCollapsed: boolean
  stopEditorStopId: string | null
  viewerLocation: {lat: number; lng: number} | null
}

const initialState: TripsState = {
  activeDayId: null, activeTab: 'itinerary', placesView: 'map',
  placeCategoryFilter: 'all', activeStopId: null,
  createTripOpen: false, addMode: false, itineraryMapCollapsed: false,
  stopEditorStopId: null,
  viewerLocation: null,
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
    setAddMode(s, a: PayloadAction<boolean>) { s.addMode = a.payload },
    setItineraryMapCollapsed(s, a: PayloadAction<boolean>) { s.itineraryMapCollapsed = a.payload },
    setStopEditor(s, a: PayloadAction<string | null>) { s.stopEditorStopId = a.payload },
    setViewerLocation(s, a: PayloadAction<{lat: number; lng: number} | null>) { s.viewerLocation = a.payload },
  },
})

export const {
  setActiveDay, setActiveTab, setPlacesView, setPlaceCategoryFilter,
  setActiveStop, setCreateTripOpen, setAddMode, setItineraryMapCollapsed, setStopEditor,
  setViewerLocation,
} = tripsSlice.actions
export default tripsSlice.reducer
