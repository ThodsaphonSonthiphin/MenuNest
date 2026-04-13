import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'

type SortBy = 'name' | 'quantity' | 'updatedAt'

interface StockState {
  searchTerm: string
  sortBy: SortBy
  lowStockOnly: boolean
}

const initialState: StockState = {
  searchTerm: '',
  sortBy: 'name',
  lowStockOnly: false,
}

const stockSlice = createSlice({
  name: 'stock',
  initialState,
  reducers: {
    setSearchTerm(state, action: PayloadAction<string>) {
      state.searchTerm = action.payload
    },
    setSortBy(state, action: PayloadAction<SortBy>) {
      state.sortBy = action.payload
    },
    toggleLowStockOnly(state) {
      state.lowStockOnly = !state.lowStockOnly
    },
  },
})

export const { setSearchTerm, setSortBy, toggleLowStockOnly } = stockSlice.actions
export default stockSlice.reducer
