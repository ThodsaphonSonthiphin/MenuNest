import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'

interface RecipesState {
  searchTerm: string
  selectedRecipeId: string | null
}

const initialState: RecipesState = {
  searchTerm: '',
  selectedRecipeId: null,
}

const recipesSlice = createSlice({
  name: 'recipes',
  initialState,
  reducers: {
    setSearchTerm(state, action: PayloadAction<string>) {
      state.searchTerm = action.payload
    },
    selectRecipe(state, action: PayloadAction<string | null>) {
      state.selectedRecipeId = action.payload
    },
  },
})

export const { setSearchTerm, selectRecipe } = recipesSlice.actions
export default recipesSlice.reducer
