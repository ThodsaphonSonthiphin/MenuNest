import { configureStore } from '@reduxjs/toolkit'
import { setupListeners } from '@reduxjs/toolkit/query'
import { useDispatch, useSelector } from 'react-redux'
import type { TypedUseSelectorHook } from 'react-redux'
import { api } from '../shared/api/api'

// Per-page slices are imported here. Feature slices live in
// `pages/{feature}/{feature}Slice.ts` and follow the naming convention.
import recipesSlice from '../pages/recipes/recipesSlice'
import stockSlice from '../pages/stock/stockSlice'
import mealPlanSlice from '../pages/meal-plan/mealPlanSlice'
import shoppingSlice from '../pages/shopping/shoppingSlice'
import ingredientsSlice from '../pages/ingredients/ingredientsSlice'
import aiAssistantSlice from '../pages/ai-assistant/aiAssistantSlice'

export const store = configureStore({
  reducer: {
    [api.reducerPath]: api.reducer,
    recipes: recipesSlice,
    stock: stockSlice,
    mealPlan: mealPlanSlice,
    shopping: shoppingSlice,
    ingredients: ingredientsSlice,
    aiAssistant: aiAssistantSlice,
  },
  middleware: (getDefaultMiddleware) => getDefaultMiddleware().concat(api.middleware),
})

setupListeners(store.dispatch)

export type RootState = ReturnType<typeof store.getState>
export type AppDispatch = typeof store.dispatch

export const useAppDispatch: () => AppDispatch = useDispatch
export const useAppSelector: TypedUseSelectorHook<RootState> = useSelector
