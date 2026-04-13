import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'

interface MealPlanState {
  /** ISO date for the first day (Monday) of the week being viewed. */
  viewStartDate: string
  /** Entry currently expanded in the side dialog, or null. */
  focusedEntryId: string | null
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
  return monday.toISOString().slice(0, 10)
}

const initialState: MealPlanState = {
  viewStartDate: startOfThisWeek(),
  focusedEntryId: null,
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
    selectEntry(state, action: PayloadAction<string | null>) {
      state.focusedEntryId = action.payload
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
  selectEntry,
  openRecipePicker,
  closeRecipePicker,
  openCookDialog,
  closeCookDialog,
} = mealPlanSlice.actions
export default mealPlanSlice.reducer
