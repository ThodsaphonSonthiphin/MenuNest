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
  description: string | null
  imageBlobPath: string | null
  ingredientCount: number
}

export interface RecipeIngredientInput {
  ingredientId: string
  quantity: number
}

export interface RecipeIngredientDto {
  ingredientId: string
  ingredientName: string
  unit: string
  quantity: number
}

export interface RecipeDetailDto {
  id: string
  name: string
  description: string | null
  imageBlobPath: string | null
  ingredients: RecipeIngredientDto[]
}

export interface RecipeUpsertRequest {
  name: string
  description: string | null
  ingredients: RecipeIngredientInput[]
}

export interface StockItemDto {
  id: string
  ingredientId: string
  ingredientName: string
  unit: string
  quantity: number
  updatedAt: string
  updatedByUserId: string
}

export interface UpsertStockRequest {
  ingredientId: string
  quantity: number
}

export type MealSlot = 'Breakfast' | 'Lunch' | 'Dinner'
export const MEAL_SLOTS: MealSlot[] = ['Breakfast', 'Lunch', 'Dinner']

export type MealEntryStatus = 'Planned' | 'Cooked' | 'Skipped'

export interface MealPlanEntryDto {
  id: string
  date: string
  mealSlot: MealSlot
  recipeId: string
  recipeName: string
  notes: string | null
  status: MealEntryStatus
  cookedAt: string | null
  cookNotes: string | null
}

export interface CreateMealPlanEntryRequest {
  date: string
  mealSlot: MealSlot
  recipeId: string
  notes: string | null
}

export interface UpdateMealPlanEntryRequest {
  recipeId: string
  notes: string | null
}

export interface StockCheckLineDto {
  ingredientId: string
  ingredientName: string
  unit: string
  required: number
  available: number
  missing: number
}

export interface StockCheckDto {
  mealPlanEntryId: string
  recipeId: string
  recipeName: string
  lines: StockCheckLineDto[]
  isSufficient: boolean
  missingCount: number
}

export interface StockCheckBatchDto {
  lines: StockCheckLineDto[]
  isSufficient: boolean
  missingCount: number
}

export interface CookDeducted {
  ingredientId: string
  ingredientName: string
  unit: string
  amount: number
}

export interface CookShortfall {
  ingredientId: string
  ingredientName: string
  unit: string
  required: number
  deducted: number
  missing: number
}

export interface CookBatchResult {
  deducted: CookDeducted[]
  partial: CookShortfall[]
  cookedEntryIds: string[]
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
      providesTags: (result) =>
        result
          ? [
              ...result.map((r) => ({ type: 'Recipes' as const, id: r.id })),
              { type: 'Recipes', id: 'LIST' },
            ]
          : [{ type: 'Recipes', id: 'LIST' }],
    }),

    getRecipe: build.query<RecipeDetailDto, string>({
      query: (id) => `/api/recipes/${id}`,
      providesTags: (_res, _err, id) => [{ type: 'Recipes', id }],
    }),

    createRecipe: build.mutation<RecipeDetailDto, RecipeUpsertRequest>({
      query: (body) => ({
        url: '/api/recipes',
        method: 'POST',
        body,
      }),
      invalidatesTags: [{ type: 'Recipes', id: 'LIST' }],
    }),

    updateRecipe: build.mutation<RecipeDetailDto, { id: string } & RecipeUpsertRequest>({
      query: ({ id, ...body }) => ({
        url: `/api/recipes/${id}`,
        method: 'PUT',
        body,
      }),
      invalidatesTags: (_res, _err, arg) => [
        { type: 'Recipes', id: arg.id },
        { type: 'Recipes', id: 'LIST' },
      ],
    }),

    deleteRecipe: build.mutation<void, string>({
      query: (id) => ({
        url: `/api/recipes/${id}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_res, _err, id) => [
        { type: 'Recipes', id },
        { type: 'Recipes', id: 'LIST' },
      ],
    }),

    // -------------------- Stock --------------------
    listStock: build.query<StockItemDto[], void>({
      query: () => '/api/stock',
      providesTags: (result) =>
        result
          ? [
              ...result.map((s) => ({ type: 'Stock' as const, id: s.id })),
              { type: 'Stock', id: 'LIST' },
            ]
          : [{ type: 'Stock', id: 'LIST' }],
    }),

    upsertStock: build.mutation<StockItemDto, UpsertStockRequest>({
      query: (body) => ({
        url: '/api/stock',
        method: 'POST',
        body,
      }),
      invalidatesTags: [{ type: 'Stock', id: 'LIST' }],
    }),

    deleteStock: build.mutation<void, string>({
      query: (id) => ({
        url: `/api/stock/${id}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_res, _err, id) => [
        { type: 'Stock', id },
        { type: 'Stock', id: 'LIST' },
      ],
    }),

    // -------------------- Meal Plan --------------------
    listMealPlan: build.query<MealPlanEntryDto[], { from: string; to: string }>({
      query: ({ from, to }) => `/api/meal-plan?from=${from}&to=${to}`,
      providesTags: (result) =>
        result
          ? [
              ...result.map((m) => ({ type: 'MealPlan' as const, id: m.id })),
              { type: 'MealPlan', id: 'LIST' },
            ]
          : [{ type: 'MealPlan', id: 'LIST' }],
    }),

    createMealPlanEntry: build.mutation<MealPlanEntryDto, CreateMealPlanEntryRequest>({
      query: (body) => ({
        url: '/api/meal-plan',
        method: 'POST',
        body,
      }),
      invalidatesTags: [{ type: 'MealPlan', id: 'LIST' }],
    }),

    updateMealPlanEntry: build.mutation<
      MealPlanEntryDto,
      { id: string } & UpdateMealPlanEntryRequest
    >({
      query: ({ id, ...body }) => ({
        url: `/api/meal-plan/${id}`,
        method: 'PUT',
        body,
      }),
      invalidatesTags: (_res, _err, arg) => [
        { type: 'MealPlan', id: arg.id },
        { type: 'MealPlan', id: 'LIST' },
      ],
    }),

    deleteMealPlanEntry: build.mutation<void, string>({
      query: (id) => ({
        url: `/api/meal-plan/${id}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_res, _err, id) => [
        { type: 'MealPlan', id },
        { type: 'MealPlan', id: 'LIST' },
      ],
    }),

    cookMealPlanBatch: build.mutation<CookBatchResult, { entryIds: string[] }>({
      query: ({ entryIds }) => ({
        url: '/api/meal-plan/cook-batch',
        method: 'POST',
        body: { entryIds },
      }),
      invalidatesTags: (_res, _err, arg) => [
        { type: 'MealPlan', id: 'LIST' },
        ...arg.entryIds.map((id) => ({ type: 'MealPlan' as const, id })),
        { type: 'Stock', id: 'LIST' },
      ],
    }),

    getStockCheck: build.query<StockCheckDto, string>({
      query: (mealPlanEntryId) => `/api/meal-plan/${mealPlanEntryId}/stock-check`,
      providesTags: (_res, _err, id) => [
        { type: 'MealPlan', id: `stock-check-${id}` },
      ],
    }),

    stockCheckBatch: build.query<StockCheckBatchDto, { entryIds: string[] }>({
      // The cache key must be order-insensitive — the user's checkbox toggle
      // can produce the same logical set in any order. RTK Query derives the
      // cache key from the query arg (not the body), so we normalise the arg
      // via serializeQueryArgs and also send a sorted body for symmetry.
      query: ({ entryIds }) => ({
        url: '/api/meal-plan/stock-check-batch',
        method: 'POST',
        body: { entryIds: [...entryIds].sort() },
      }),
      serializeQueryArgs: ({ endpointName, queryArgs }) => ({
        endpointName,
        entryIds: [...queryArgs.entryIds].sort(),
      }),
      providesTags: (_res, _err, arg) =>
        [...arg.entryIds]
          .sort()
          .map((id) => ({ type: 'MealPlan' as const, id: `stock-check-batch-${id}` })),
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
  useGetRecipeQuery,
  useCreateRecipeMutation,
  useUpdateRecipeMutation,
  useDeleteRecipeMutation,
  useListStockQuery,
  useUpsertStockMutation,
  useDeleteStockMutation,
  useListMealPlanQuery,
  useCreateMealPlanEntryMutation,
  useUpdateMealPlanEntryMutation,
  useDeleteMealPlanEntryMutation,
  useGetStockCheckQuery,
  useStockCheckBatchQuery,
  useCookMealPlanBatchMutation,
  useListShoppingListsQuery,
} = api
