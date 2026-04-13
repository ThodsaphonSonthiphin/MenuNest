import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'
import type { MealSlot } from '../../shared/api/api'

export interface FocusedSlot {
  date: string  // ISO yyyy-mm-dd
  slot: MealSlot
}

interface MealPlanState {
  /** ISO date for the first day (Monday) of the week being viewed. */
  viewStartDate: string
  /** Slot currently expanded in the side dialog, or null. */
  focusedSlot: FocusedSlot | null
  /** Whether the recipe-picker dialog is open. */
  recipePickerOpen: boolean
  /** Whether the cook-confirm dialog is open. */
  cookDialogOpen: boolean
}

function startOfThisWeek(): string {
  const today = new Date()
  const dow = today.getDay()
  const monday = new Date(today)
  monday.setDate(today.getDate() - ((dow + 6) % 7))
  // Use local Y/M/D — toISOString shifts into UTC and can land on the
  // previous day east of Greenwich.
  const y = monday.getFullYear()
  const m = String(monday.getMonth() + 1).padStart(2, '0')
  const d = String(monday.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

const initialState: MealPlanState = {
  viewStartDate: startOfThisWeek(),
  focusedSlot: null,
  recipePickerOpen: false,
  cookDialogOpen: false,
}

const mealPlanSlice = createSlice({
  name: 'mealPlan',
  initialState,
  reducers: {
    setViewStartDate(state, action: PayloadAction<string>) {
      state.viewStartDate = action.payload
    },
    selectSlot(state, action: PayloadAction<FocusedSlot | null>) {
      state.focusedSlot = action.payload
    },
    openRecipePicker(state) {
      state.recipePickerOpen = true
    },
    closeRecipePicker(state) {
      state.recipePickerOpen = false
    },
    openCookDialog(state) {
      state.cookDialogOpen = true
    },
    closeCookDialog(state) {
      state.cookDialogOpen = false
    },
  },
})

export const {
  setViewStartDate,
  selectSlot,
  openRecipePicker,
  closeRecipePicker,
  openCookDialog,
  closeCookDialog,
} = mealPlanSlice.actions
export default mealPlanSlice.reducer
