import {createSlice} from '@reduxjs/toolkit'
import type {PayloadAction} from '@reduxjs/toolkit'

export type BudgetFilter = 'all' | 'overspent' | 'underfunded' | 'overfunded' | 'available' | 'snoozed'
export type BudgetLayout = 'desktop' | 'tablet' | 'mobile'

interface BudgetState {
  year: number
  month: number
  filter: BudgetFilter
  accountsOpen: boolean     // for mobile drawer
  summaryOpen: boolean      // for mobile drawer
  selectedCategoryId: string | null
  search: string
}

const now = new Date()
const initialState: BudgetState = {
  year: now.getFullYear(),
  month: now.getMonth() + 1,
  filter: 'all',
  accountsOpen: false,
  summaryOpen: false,
  selectedCategoryId: null,
  search: '',
}

const budgetSlice = createSlice({
  name: 'budget',
  initialState,
  reducers: {
    setMonth(s, a: PayloadAction<{year: number; month: number}>) {
      s.year = a.payload.year; s.month = a.payload.month
    },
    goPrevMonth(s) {
      const d = new Date(s.year, s.month - 2, 1)
      s.year = d.getFullYear(); s.month = d.getMonth() + 1
    },
    goNextMonth(s) {
      const d = new Date(s.year, s.month, 1)
      s.year = d.getFullYear(); s.month = d.getMonth() + 1
    },
    setFilter(s, a: PayloadAction<BudgetFilter>) { s.filter = a.payload },
    setAccountsOpen(s, a: PayloadAction<boolean>) { s.accountsOpen = a.payload },
    setSummaryOpen(s, a: PayloadAction<boolean>)  { s.summaryOpen = a.payload },
    setSelectedCategory(s, a: PayloadAction<string | null>) { s.selectedCategoryId = a.payload },
    setSearch(s, a: PayloadAction<string>) { s.search = a.payload },
  },
})

export const {
  setMonth, goPrevMonth, goNextMonth, setFilter,
  setAccountsOpen, setSummaryOpen, setSelectedCategory, setSearch,
} = budgetSlice.actions
export default budgetSlice.reducer
