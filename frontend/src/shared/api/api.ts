import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react'
import { InteractionRequiredAuthError } from '@azure/msal-browser'
import { apiScopes, msalInstance } from '../auth/msalConfig'

/**
 * Single, app-wide RTK Query API. All endpoints for every feature
 * live here. Feature pages import generated hooks (e.g.,
 * `useListRecipesQuery`) directly from this module.
 *
 * Endpoints are stubbed for now and will be filled in as the backend
 * gains real handlers.
 */

async function acquireAccessToken(): Promise<string | null> {
  const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0]
  if (!account || apiScopes.length === 0) return null

  try {
    const result = await msalInstance.acquireTokenSilent({ scopes: apiScopes, account })
    return result.accessToken
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      await msalInstance.acquireTokenRedirect({ scopes: apiScopes, account })
    }
    return null
  }
}

// ----------------------------------------------------------------------
// Shared DTO placeholders — replace with real shapes as the backend lands.
// ----------------------------------------------------------------------

export interface MeDto {
  userId: string
  email: string
  displayName: string
  familyId: string | null
  familyName: string | null
  familyInviteCode: string | null
}

export interface FamilyDto {
  id: string
  name: string
  inviteCode: string
  memberCount: number
}

export interface CreateFamilyRequest {
  name: string
}

export interface IngredientDto {
  id: string
  name: string
  unit: string
}

export interface RecipeSummaryDto {
  id: string
  name: string
  ingredientCount: number
  imageUrl: string | null
}

export interface StockItemDto {
  id: string
  ingredientId: string
  ingredientName: string
  unit: string
  quantity: number
  updatedAt: string
  updatedByDisplayName: string
}

export interface MealPlanEntryDto {
  id: string
  date: string
  mealSlot: 'Breakfast' | 'Lunch' | 'Dinner'
  recipeId: string
  recipeName: string
  status: 'Planned' | 'Cooked' | 'Skipped'
  cookedAt: string | null
  cookNotes: string | null
  stockStatus: 'Sufficient' | 'PartiallyShort' | 'ShortSome'
}

export interface ShoppingListSummaryDto {
  id: string
  name: string
  status: 'Active' | 'Completed' | 'Archived'
  itemCount: number
  boughtCount: number
  createdAt: string
}

// ----------------------------------------------------------------------

export const api = createApi({
  reducerPath: 'api',
  baseQuery: fetchBaseQuery({
    baseUrl: import.meta.env.VITE_API_BASE_URL || '/',
    prepareHeaders: async (headers) => {
      const token = await acquireAccessToken()
      if (token) headers.set('Authorization', `Bearer ${token}`)
      return headers
    },
  }),
  tagTypes: [
    'Me',
    'Family',
    'FamilyMembers',
    'Ingredients',
    'Recipes',
    'Stock',
    'MealPlan',
    'ShoppingLists',
    'ShoppingListDetail',
  ],
  endpoints: (build) => ({
    // -------------------- Me / Family --------------------
    getMe: build.query<MeDto, void>({
      query: () => '/api/me',
      providesTags: ['Me'],
    }),

    createFamily: build.mutation<FamilyDto, CreateFamilyRequest>({
      query: (body) => ({
        url: '/api/families',
        method: 'POST',
        body,
      }),
      // New family membership changes what /api/me returns, so evict
      // the cached Me entry and let the guard re-evaluate.
      invalidatesTags: ['Me', 'Family'],
    }),

    // -------------------- Ingredients --------------------
    listIngredients: build.query<IngredientDto[], void>({
      query: () => '/api/ingredients',
      providesTags: (result) =>
        result
          ? [
              ...result.map((i) => ({ type: 'Ingredients' as const, id: i.id })),
              { type: 'Ingredients', id: 'LIST' },
            ]
          : [{ type: 'Ingredients', id: 'LIST' }],
    }),

    createIngredient: build.mutation<IngredientDto, { name: string; unit: string }>({
      query: (body) => ({
        url: '/api/ingredients',
        method: 'POST',
        body,
      }),
      invalidatesTags: [{ type: 'Ingredients', id: 'LIST' }],
    }),

    updateIngredient: build.mutation<IngredientDto, { id: string; name: string; unit: string }>({
      query: ({ id, ...body }) => ({
        url: `/api/ingredients/${id}`,
        method: 'PUT',
        body,
      }),
      invalidatesTags: (_res, _err, arg) => [
        { type: 'Ingredients', id: arg.id },
        { type: 'Ingredients', id: 'LIST' },
      ],
    }),

    deleteIngredient: build.mutation<void, string>({
      query: (id) => ({
        url: `/api/ingredients/${id}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_res, _err, id) => [
        { type: 'Ingredients', id },
        { type: 'Ingredients', id: 'LIST' },
      ],
    }),

    // -------------------- Recipes --------------------
    listRecipes: build.query<RecipeSummaryDto[], void>({
      query: () => '/api/recipes',
      providesTags: ['Recipes'],
    }),

    // -------------------- Stock --------------------
    listStock: build.query<StockItemDto[], void>({
      query: () => '/api/stock',
      providesTags: ['Stock'],
    }),

    // -------------------- Meal Plan --------------------
    listMealPlan: build.query<MealPlanEntryDto[], { from: string; to: string }>({
      query: ({ from, to }) => `/api/meal-plan?from=${from}&to=${to}`,
      providesTags: ['MealPlan'],
    }),

    // -------------------- Shopping Lists --------------------
    listShoppingLists: build.query<ShoppingListSummaryDto[], void>({
      query: () => '/api/shopping-lists',
      providesTags: ['ShoppingLists'],
    }),
  }),
})

export const {
  useGetMeQuery,
  useCreateFamilyMutation,
  useListIngredientsQuery,
  useCreateIngredientMutation,
  useUpdateIngredientMutation,
  useDeleteIngredientMutation,
  useListRecipesQuery,
  useListStockQuery,
  useListMealPlanQuery,
  useListShoppingListsQuery,
} = api
