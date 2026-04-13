import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'

interface IngredientsState {
  searchTerm: string
}

const initialState: IngredientsState = {
  searchTerm: '',
}

const ingredientsSlice = createSlice({
  name: 'ingredients',
  initialState,
  reducers: {
    setSearchTerm(state, action: PayloadAction<string>) {
      state.searchTerm = action.payload
    },
  },
})

export const { setSearchTerm } = ingredientsSlice.actions
export default ingredientsSlice.reducer
