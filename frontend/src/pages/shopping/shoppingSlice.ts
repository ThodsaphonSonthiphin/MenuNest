import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'

type Filter = 'active' | 'completed' | 'all'

interface ShoppingState {
  filter: Filter
  createDialogOpen: boolean
}

const initialState: ShoppingState = {
  filter: 'active',
  createDialogOpen: false,
}

const shoppingSlice = createSlice({
  name: 'shopping',
  initialState,
  reducers: {
    setFilter(state, action: PayloadAction<Filter>) {
      state.filter = action.payload
    },
    openCreateDialog(state) {
      state.createDialogOpen = true
    },
    closeCreateDialog(state) {
      state.createDialogOpen = false
    },
  },
})

export const { setFilter, openCreateDialog, closeCreateDialog } = shoppingSlice.actions
export default shoppingSlice.reducer
