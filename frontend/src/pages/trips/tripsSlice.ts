// frontend/src/pages/trips/tripsSlice.ts
import {createSlice} from '@reduxjs/toolkit'
import type {PayloadAction} from '@reduxjs/toolkit'
import type {PlaceCategory} from '../../shared/api/api'

interface TripsState {
  activeDayId: string | null
  placeCategoryFilter: PlaceCategory | 'all'
  /**
   * The one **Selected Stop** (menunest-238). Read by BOTH the map and the Plan sheet /
   * panel — tapping a pin and tapping a card are the same selection, so there is exactly
   * one place for it to live. (This is the field formerly called `activeStopId`, which was
   * declared with a reducer and read by nothing.)
   */
  selectedStopId: string | null
  createTripOpen: boolean
  /**
   * Capture mode is armed on the ONE map the screen already shows (menunest-240). There is
   * no separate "which Day am I adding to" flag any more: the trip screen has a single map
   * and a single active Day, so the Day is implicit in `activeDayId`.
   */
  addMode: boolean
  stopEditorStopId: string | null
  placeEditorPlaceId: string | null
  viewerLocation: {lat: number; lng: number} | null
}

const initialState: TripsState = {
  activeDayId: null,
  placeCategoryFilter: 'all',
  selectedStopId: null,
  createTripOpen: false,
  addMode: false,
  stopEditorStopId: null,
  placeEditorPlaceId: null,
  viewerLocation: null,
}

const tripsSlice = createSlice({
  name: 'trips',
  initialState,
  reducers: {
    // Switching Day drops the selection: a Stop on the Day you just left is not a thing the
    // new Day's map or list can show, and a stale id would leave the sheet on a dead card.
    setActiveDay(s, a: PayloadAction<string | null>) {
      s.activeDayId = a.payload
      s.selectedStopId = null
    },
    setPlaceCategoryFilter(s, a: PayloadAction<PlaceCategory | 'all'>) { s.placeCategoryFilter = a.payload },
    setSelectedStop(s, a: PayloadAction<string | null>) { s.selectedStopId = a.payload },
    setCreateTripOpen(s, a: PayloadAction<boolean>) { s.createTripOpen = a.payload },
    // Arming takes over every map tap (ADR-163), so an open selection would be a dead
    // surface behind the capture banner — clear it as Discover's `arm` does.
    setAddMode(s, a: PayloadAction<boolean>) {
      s.addMode = a.payload
      if (a.payload) s.selectedStopId = null
    },
    setStopEditor(s, a: PayloadAction<string | null>) { s.stopEditorStopId = a.payload },
    setPlaceEditor(s, a: PayloadAction<string | null>) { s.placeEditorPlaceId = a.payload },
    setViewerLocation(s, a: PayloadAction<{lat: number; lng: number} | null>) { s.viewerLocation = a.payload },
  },
})

export const {
  setActiveDay, setPlaceCategoryFilter,
  setSelectedStop, setCreateTripOpen, setAddMode, setStopEditor,
  setViewerLocation,
  setPlaceEditor,
} = tripsSlice.actions
export default tripsSlice.reducer
