import {createApi, fetchBaseQuery} from '@reduxjs/toolkit/query/react'
import {InteractionRequiredAuthError} from '@azure/msal-browser'
import {apiScopes, msalInstance} from '../auth/msalConfig'
import {getGoogleToken} from '../auth/googleAuth'
import {handleAuthFailure} from '../auth/reauth'
import type {
    AttachedPhotoInfo,
    CreateCustomSymptomRequest,
    CreateCustomTriggerRequest,
    CreateDrugRequest,
    CreateShareLinkRequest,
    CreateShareLinkResultDto,
    DoctorReportDto,
    DrugDetailDto,
    DrugDto,
    EpisodeDetailDto,
    EpisodeDto,
    IntakeDto,
    ListEpisodesQueryArgs,
    LogIntakeRequest,
    LogNoDrugRequest,
    PhotoRefDto,
    RecordPingResponseRequest,
    RequestUploadSasRequest,
    ResolveEpisodeRequest,
    RetroCloseEpisodeRequest,
    ShareLinkSummaryDto,
    StartEpisodeRequest,
    SubscribeWebPushRequest,
    SubscribeWebPushResultDto,
    SymptomDto,
    TakeMedicationContextDto,
    TriggerDto,
    UnsubscribeWebPushRequest,
    UpdateDrugRequest,
    UpdateEpisodeRequest,
    UploadSasResponse,
    VapidPublicKeyDto,
} from './healthTypes'

/**
 * Single, app-wide RTK Query API. All endpoints for every feature
 * live here. Feature pages import generated hooks (e.g.,
 * `useListRecipesQuery`) directly from this module.
 *
 * Endpoints are stubbed for now and will be filled in as the backend
 * gains real handlers.
 */

/**
 * Set for the remainder of the turn once we've kicked off an MSAL
 * `acquireTokenRedirect`. That call begins a full-page navigation to
 * Microsoft to complete silent re-auth, but it does NOT halt JS
 * execution — so the in-flight fetch still goes out with no bearer and
 * comes back 401. Without this flag, `baseQueryWithReauth` would react
 * to that 401 by calling `handleAuthFailure()` → `window.location.assign('/login')`,
 * which races (and can clobber) the MSAL redirect, yanking the user out
 * of an otherwise-recoverable SSO and onto the login screen. The flag
 * lets the reauth wrapper defer to the already-queued Microsoft redirect.
 */
let msalRedirectInProgress = false

async function acquireAccessToken(): Promise<string | null> {
    // Try MSAL first (Microsoft)
    const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0]
    if (account && apiScopes.length > 0) {
        try {
            const result = await msalInstance.acquireTokenSilent({scopes: apiScopes, account})
            return result.accessToken
        } catch (err) {
            if (err instanceof InteractionRequiredAuthError) {
                // A full-page redirect to Microsoft is now being initiated to
                // silently re-establish the session. Flag it so the 401 the
                // current (token-less) request is about to get does NOT trigger
                // a competing assign('/login') in baseQueryWithReauth.
                // Stays true for the rest of the page on purpose: once ANY
                // redirect is underway, concurrent callers' acquireTokenRedirect
                // throw interaction_in_progress, and the page is navigating
                // anyway — so every later 401 must keep deferring to that
                // redirect instead of racing it with assign('/login').
                msalRedirectInProgress = true
                await msalInstance.acquireTokenRedirect({scopes: apiScopes, account})
                return null
            }
        }
    }

    // Try Google token
    const googleToken = getGoogleToken()
    if (googleToken) return googleToken

    return null
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
    authProvider: string
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
    ingredientId: string
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

export interface ShoppingListDto {
    id: string
    name: string
    status: 'Active' | 'Completed' | 'Archived'
    totalCount: number
    boughtCount: number
    createdAt: string
    completedAt: string | null
}

export interface ShoppingListItemDto {
    id: string
    ingredientId: string
    ingredientName: string
    unit: string
    quantity: number
    isBought: boolean
    boughtAt: string | null
    sourceMealPlanEntryIds: string[] | null
}

export interface ShoppingListDetailDto extends ShoppingListDto {
    items: ShoppingListItemDto[]
}

export interface FamilyMemberDto {
    userId: string
    displayName: string
    email: string
    joinedAt: string
    isCreator: boolean
    relationships: RelationshipLabelDto[]
}

export interface RelationshipLabelDto {
    relationshipId: string
    relationType: string
    label: string
}

export interface RelationshipDto {
    id: string
    fromUserId: string
    fromUserName: string
    toUserId: string
    toUserName: string
    relationType: string
}

export interface AddRelationshipRequest {
    fromUserId: string
    toUserId: string
    relationType: string
}

// Chat types
export interface ConversationSummaryDto {
    id: string
    title: string
    createdAt: string
    updatedAt: string | null
}

export interface ChatMessageDto {
    id: string
    role: string
    content: string
    structuredData: string | null
    createdAt: string
}

export interface SendMessageResponseDto {
    messageId: string
    role: string
    content: string
    structuredData: string | null
    createdAt: string
}

export interface SpeechTokenDto {
    token: string
    region: string
}

// ---------- Budget ----------
export type BudgetAccountType = 'Cash' | 'Credit' | 'Loan' | 'Closed'
export type BudgetTargetType = 'None' | 'MonthlyAmount' | 'ByDate' | 'MonthlySavingsBuilder'

export interface BudgetAccountDto {
    id: string
    name: string
    type: BudgetAccountType
    balance: number
    sortOrder: number
    isClosed: boolean
}

export interface CategoryGroupDto {
    id: string
    name: string
    sortOrder: number
    isHidden: boolean
}

export interface BudgetCategoryDto {
    id: string
    groupId: string
    name: string
    emoji: string | null
    sortOrder: number
    isHidden: boolean
    targetType: BudgetTargetType
    targetAmount: number | null
    targetDueDate: string | null
    targetDayOfMonth: number | null
}

export interface EnvelopeDto {
    categoryId: string
    name: string
    emoji: string | null
    sortOrder: number
    isHidden: boolean
    assigned: number
    activity: number
    available: number
    targetType: BudgetTargetType
    targetAmount: number | null
    targetDueDate: string | null
    targetDayOfMonth: number | null
    targetProgressFraction: number | null
    targetHint: string | null
}

export interface EnvelopeGroupDto {
    groupId: string
    name: string
    sortOrder: number
    isHidden: boolean
    totalAssigned: number
    totalActivity: number
    totalAvailable: number
    categories: EnvelopeDto[]
}

export interface MonthlySummaryDto {
    year: number
    month: number
    income: number
    totalAssigned: number
    totalActivity: number
    readyToAssign: number
    available: number
    groups: EnvelopeGroupDto[]
    accounts: BudgetAccountDto[]
}

export interface AccountSummaryDto {
    id: string
    name: string
    type: BudgetAccountType
    balance: number
    monthInflow: number
    monthOutflow: number
}

export interface AccountTransactionsPageDto {
    account: AccountSummaryDto
    items: BudgetTransactionDto[]
    hasMore: boolean
}

export interface BudgetTransactionDto {
    id: string
    accountId: string
    accountName: string
    categoryId: string | null
    categoryName: string | null
    categoryEmoji: string | null
    amount: number
    date: string
    notes: string | null
    createdByUserId: string
    createdByDisplayName: string
}

export interface CreateTransactionRequest {
    accountId: string
    categoryId: string | null
    amount: number
    date: string
    notes: string | null
}

export interface UpdateTransactionRequest extends CreateTransactionRequest {}

export interface UpsertCategoryRequest {
    groupId: string
    name: string
    emoji: string | null
    sortOrder?: number
    targetType: BudgetTargetType
    targetAmount: number | null
    targetDueDate: string | null
    targetDayOfMonth: number | null
}

export interface UpsertGroupRequest {
    name: string
    sortOrder?: number
}

export interface CreateAccountRequest {
    name: string
    type: BudgetAccountType
    openingBalance: number
    sortOrder?: number
}

export interface UpdateAccountRequest {
    name: string
    sortOrder: number
    isClosed: boolean
    setBalance: number | null
}

export interface MoveMoneyRequest {
    fromCategoryId: string
    toCategoryId: string
    year: number
    month: number
    amount: number
}

export interface CoverOverspendingRequest {
    overspentCategoryId: string
    fromCategoryId: string
    year: number
    month: number
    amount: number
}

// -------------------- Trips --------------------
export type TravelMode = 'Drive' | 'Walk' | 'Transit'
export type RouteSource = 'Routed' | 'Estimated'
export type PlaceCategory = 'Stay' | 'Eat' | 'See' | 'Cafe' | 'Shop' | 'Other'

export interface TripDto { id: string; name: string; destination: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode }
export interface ReviewLink {
    url: string
    label: string | null
}
export interface ChecklistItem {
    id: string
    name: string
}
export interface PlaceChecklistEntry {
    id: string
    checklistItemId: string
    name: string
    isChecked: boolean
}
export interface TripPlaceDto {
    id: string; tripId: string; googlePlaceId: string | null; name: string; lat: number; lng: number
    address: string | null; category: PlaceCategory; priceLevel: number | null; photoUrl: string | null
    bestTimeStart: string | null; bestTimeEnd: string | null; openingHoursJson: string | null
    feeNote: string | null; notes: string | null
    reviewLinks: ReviewLink[]
    checklist: PlaceChecklistEntry[]
    hasProfile: boolean
}
export interface LegDto { seconds: number; meters: number; encodedPolyline: string | null; source: RouteSource }
export interface StopDto { id: string; tripPlaceId: string; sequence: number; dwellMinutes: number; travelModeToReach: TravelMode; legToReach: LegDto | null; isVisited: boolean }
export interface ItineraryDayDto { id: string; date: string; dayStartTime: string; useCurrentTimeAsStart: boolean; stops: StopDto[] }
export interface ResolvedPlaceDto { googlePlaceId: string | null; name: string; lat: number; lng: number; address: string | null; category: PlaceCategory; priceLevel: number | null; photoUrl: string | null; openingHoursJson: string | null }
export interface WeatherPointDto { stopId: string; lat: number; lng: number; arrivalIso?: string }
export interface WeatherReadingDto {
    stopId: string; hasData: boolean; conditionType: string | null; iconBaseUri: string | null
    tempC: number | null; rainPct: number | null; description: string | null
}

// ----------------------------------------------------------------------
const rawBaseQuery = fetchBaseQuery({
    baseUrl: import.meta.env.VITE_API_BASE_URL || '/',
    prepareHeaders: async (headers) => {
        const token = await acquireAccessToken()
        if (token) headers.set('Authorization', `Bearer ${token}`)
        return headers
    },
})

/**
 * Wrap the base query so a 401 (missing / expired / rejected bearer)
 * tears down the stale session and bounces to /login. MSAL refresh is
 * already attempted in prepareHeaders; a 401 past that means the
 * session is truly gone. The public doctor-report slice (`publicApi`
 * below) keeps a plain fetchBaseQuery and never reaches this wrapper.
 */
const baseQueryWithReauth: typeof rawBaseQuery = async (args, apiCtx, extraOptions) => {
    const result = await rawBaseQuery(args, apiCtx, extraOptions)
    if (result.error && result.error.status === 401) {
        // If MSAL has already begun a redirect to Microsoft to silently
        // re-auth, let that navigation proceed — firing assign('/login')
        // here would race it and could dump the user on the login screen
        // mid-SSO. The Microsoft redirect either restores the session or
        // lands the user on the login flow itself.
        if (!msalRedirectInProgress) {
            handleAuthFailure()
        }
    }
    return result
}

export const api = createApi({
    reducerPath: 'api',
    baseQuery: baseQueryWithReauth,
    tagTypes: [
        'Me',
        'Family',
        'FamilyMembers',
        'Ingredients',
        'Recipes',
        'Stock',
        'MealPlan',
        'Relationships',
        'ShoppingLists',
        'ShoppingListDetail',
        'ChatConversations',
        'ChatMessages',
        'BudgetSummary',
        'BudgetAccounts',
        'BudgetGroups',
        'BudgetTransactions',
        'BudgetAccountDetail',
        'Drug',
        'Symptom',
        'Trigger',
        'Episode',
        'ActiveEpisode',
        'ShareLink',
        'PushSubscription',
        'Trips',
        'TripDetail',
        'TripPlaces',
        'TripItinerary',
        'ChecklistItems',
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
        joinFamily: build.mutation<FamilyDto, { inviteCode: string }>({
            query: (body) => ({url: '/api/families/join', method: 'POST', body}),
            invalidatesTags: ['Me', 'Family'],
        }),
        listFamilyMembers: build.query<FamilyMemberDto[], void>({
            query: () => '/api/families/me/members',
            providesTags: ['FamilyMembers'],
        }),
        rotateInviteCode: build.mutation<{ inviteCode: string }, void>({
            query: () => ({url: '/api/families/me/invite-codes/rotate', method: 'POST'}),
            invalidatesTags: ['Me'],
        }),
        leaveFamily: build.mutation<void, void>({
            query: () => ({url: '/api/families/leave', method: 'POST'}),
            invalidatesTags: ['Me', 'Family', 'FamilyMembers'],
        }),
        // -------------------- Relationships --------------------
        listRelationships: build.query<RelationshipDto[], void>({
            query: () => '/api/families/me/relationships',
            providesTags: ['Relationships'],
        }),
        addRelationship: build.mutation<RelationshipDto, AddRelationshipRequest>({
            query: (body) => ({url: '/api/families/me/relationships', method: 'POST', body}),
            invalidatesTags: ['Relationships', 'FamilyMembers'],
        }),
        deleteRelationship: build.mutation<void, string>({
            query: (id) => ({url: `/api/families/me/relationships/${id}`, method: 'DELETE'}),
            invalidatesTags: ['Relationships', 'FamilyMembers'],
        }),
        // -------------------- Ingredients --------------------
        listIngredients: build.query<IngredientDto[], void>({
            query: () => '/api/ingredients',
            providesTags: (result) =>
                result
                    ? [
                        ...result.map((i) => ({type: 'Ingredients' as const, id: i.ingredientId})),
                        {type: 'Ingredients', id: 'LIST'},
                    ]
                    : [{type: 'Ingredients', id: 'LIST'}],
        }),
        createIngredient: build.mutation<IngredientDto, { name: string; unit: string }>({
            query: (body) => ({
                url: '/api/ingredients',
                method: 'POST',
                body,
            }),
            invalidatesTags: [{type: 'Ingredients', id: 'LIST'}],
        }),
        updateIngredient: build.mutation<IngredientDto, { id: string; name: string; unit: string }>({
            query: ({id, ...body}) => ({
                url: `/api/ingredients/${id}`,
                method: 'PUT',
                body,
            }),
            invalidatesTags: (_res, _err, arg) => [
                {type: 'Ingredients', id: arg.id},
                {type: 'Ingredients', id: 'LIST'},
            ],
        }),
        deleteIngredient: build.mutation<void, string>({
            query: (id) => ({
                url: `/api/ingredients/${id}`,
                method: 'DELETE',
            }),
            invalidatesTags: (_res, _err, id) => [
                {type: 'Ingredients', id},
                {type: 'Ingredients', id: 'LIST'},
            ],
        }),
        // -------------------- Recipes --------------------
        listRecipes: build.query<RecipeSummaryDto[], void>({
            query: () => '/api/recipes',
            providesTags: (result) =>
                result
                    ? [
                        ...result.map((r) => ({type: 'Recipes' as const, id: r.id})),
                        {type: 'Recipes', id: 'LIST'},
                    ]
                    : [{type: 'Recipes', id: 'LIST'}],
        }),
        getRecipe: build.query<RecipeDetailDto, string>({
            query: (id) => `/api/recipes/${id}`,
            providesTags: (_res, _err, id) => [{type: 'Recipes', id}],
        }),
        createRecipe: build.mutation<RecipeDetailDto, RecipeUpsertRequest>({
            query: (body) => ({
                url: '/api/recipes',
                method: 'POST',
                body,
            }),
            invalidatesTags: [{type: 'Recipes', id: 'LIST'}],
        }),
        updateRecipe: build.mutation<RecipeDetailDto, { id: string } & RecipeUpsertRequest>({
            query: ({id, ...body}) => ({
                url: `/api/recipes/${id}`,
                method: 'PUT',
                body,
            }),
            invalidatesTags: (_res, _err, arg) => [
                {type: 'Recipes', id: arg.id},
                {type: 'Recipes', id: 'LIST'},
            ],
        }),
        deleteRecipe: build.mutation<void, string>({
            query: (id) => ({
                url: `/api/recipes/${id}`,
                method: 'DELETE',
            }),
            invalidatesTags: (_res, _err, id) => [
                {type: 'Recipes', id},
                {type: 'Recipes', id: 'LIST'},
            ],
        }),
        // -------------------- Stock --------------------
        listStock: build.query<StockItemDto[], void>({
            query: () => '/api/stock',
            providesTags: (result) =>
                result
                    ? [
                        ...result.map((s) => ({type: 'Stock' as const, id: s.id})),
                        {type: 'Stock', id: 'LIST'},
                    ]
                    : [{type: 'Stock', id: 'LIST'}],
        }),
        upsertStock: build.mutation<StockItemDto, UpsertStockRequest>({
            query: (body) => ({
                url: '/api/stock',
                method: 'POST',
                body,
            }),
            invalidatesTags: [{type: 'Stock', id: 'LIST'}],
        }),
        deleteStock: build.mutation<void, string>({
            query: (id) => ({
                url: `/api/stock/${id}`,
                method: 'DELETE',
            }),
            invalidatesTags: (_res, _err, id) => [
                {type: 'Stock', id},
                {type: 'Stock', id: 'LIST'},
            ],
        }),
        // -------------------- Meal Plan --------------------
        listMealPlan: build.query<MealPlanEntryDto[], { from: string; to: string }>({
            query: ({from, to}) => `/api/meal-plan?from=${from}&to=${to}`,
            providesTags: (result) =>
                result
                    ? [
                        ...result.map((m) => ({type: 'MealPlan' as const, id: m.id})),
                        {type: 'MealPlan', id: 'LIST'},
                    ]
                    : [{type: 'MealPlan', id: 'LIST'}],
        }),
        createMealPlanEntry: build.mutation<MealPlanEntryDto, CreateMealPlanEntryRequest>({
            query: (body) => ({
                url: '/api/meal-plan',
                method: 'POST',
                body,
            }),
            invalidatesTags: [{type: 'MealPlan', id: 'LIST'}],
        }),
        updateMealPlanEntry: build.mutation<
            MealPlanEntryDto,
            { id: string } & UpdateMealPlanEntryRequest
        >({
            query: ({id, ...body}) => ({
                url: `/api/meal-plan/${id}`,
                method: 'PUT',
                body,
            }),
            invalidatesTags: (_res, _err, arg) => [
                {type: 'MealPlan', id: arg.id},
                {type: 'MealPlan', id: 'LIST'},
            ],
        }),
        deleteMealPlanEntry: build.mutation<void, string>({
            query: (id) => ({
                url: `/api/meal-plan/${id}`,
                method: 'DELETE',
            }),
            invalidatesTags: (_res, _err, id) => [
                {type: 'MealPlan', id},
                {type: 'MealPlan', id: 'LIST'},
            ],
        }),
        cookMealPlanBatch: build.mutation<CookBatchResult, { entryIds: string[] }>({
            query: ({entryIds}) => ({
                url: '/api/meal-plan/cook-batch',
                method: 'POST',
                body: {entryIds},
            }),
            invalidatesTags: (_res, _err, arg) => [
                {type: 'MealPlan', id: 'LIST'},
                ...arg.entryIds.map((id) => ({type: 'MealPlan' as const, id})),
                {type: 'Stock', id: 'LIST'},
            ],
        }),
        getStockCheck: build.query<StockCheckDto, string>({
            query: (mealPlanEntryId) => `/api/meal-plan/${mealPlanEntryId}/stock-check`,
            providesTags: (_res, _err, id) => [
                {type: 'MealPlan', id: `stock-check-${id}`},
            ],
        }),
        stockCheckBatch: build.query<StockCheckBatchDto, { entryIds: string[] }>({
            // The cache key must be order-insensitive — the user's checkbox toggle
            // can produce the same logical set in any order. RTK Query derives the
            // cache key from the query arg (not the body), so we normalise the arg
            // via serializeQueryArgs and also send a sorted body for symmetry.
            query: ({entryIds}) => ({
                url: '/api/meal-plan/stock-check-batch',
                method: 'POST',
                body: {entryIds: [...entryIds].sort()},
            }),
            serializeQueryArgs: ({endpointName, queryArgs}) => ({
                endpointName,
                entryIds: [...queryArgs.entryIds].sort(),
            }),
            providesTags: (_res, _err, arg) =>
                [...arg.entryIds]
                    .sort()
                    .map((id) => ({type: 'MealPlan' as const, id: `stock-check-batch-${id}`})),
        }),
        // -------------------- Shopping Lists --------------------
        listShoppingLists: build.query<ShoppingListDto[], { status?: string }>({
            query: ({status} = {}) =>
                `/api/shopping-lists${status ? `?status=${status}` : ''}`,
            providesTags: [{type: 'ShoppingLists', id: 'LIST'}],
        }),
        getShoppingListDetail: build.query<ShoppingListDetailDto, string>({
            query: (id) => `/api/shopping-lists/${id}`,
            providesTags: (_res, _err, id) => [{type: 'ShoppingListDetail', id}],
        }),
        createShoppingList: build.mutation<ShoppingListDto, { name: string; fromDate?: string; toDate?: string }>({
            query: (body) => ({url: '/api/shopping-lists', method: 'POST', body}),
            invalidatesTags: [{type: 'ShoppingLists', id: 'LIST'}],
        }),
        deleteShoppingList: build.mutation<void, string>({
            query: (id) => ({url: `/api/shopping-lists/${id}`, method: 'DELETE'}),
            invalidatesTags: [{type: 'ShoppingLists', id: 'LIST'}],
        }),
        completeShoppingList: build.mutation<ShoppingListDto, string>({
            query: (id) => ({url: `/api/shopping-lists/${id}/complete`, method: 'POST'}),
            invalidatesTags: (_res, _err, id) => [
                {type: 'ShoppingLists', id: 'LIST'},
                {type: 'ShoppingListDetail', id},
            ],
        }),
        addShoppingListItem: build.mutation<ShoppingListItemDto, {
            listId: string;
            ingredientId: string;
            quantity: number
        }>({
            query: ({listId, ...body}) => ({
                url: `/api/shopping-lists/${listId}/items`,
                method: 'POST',
                body,
            }),
            invalidatesTags: (_res, _err, {listId}) => [
                {type: 'ShoppingListDetail', id: listId},
                {type: 'ShoppingLists', id: 'LIST'},
            ],
        }),
        deleteShoppingListItem: build.mutation<void, { listId: string; itemId: string }>({
            query: ({listId, itemId}) => ({
                url: `/api/shopping-lists/${listId}/items/${itemId}`,
                method: 'DELETE',
            }),
            invalidatesTags: (_res, _err, {listId}) => [
                {type: 'ShoppingListDetail', id: listId},
                {type: 'ShoppingLists', id: 'LIST'},
            ],
        }),
        buyShoppingListItem: build.mutation<ShoppingListItemDto, { listId: string; itemId: string }>({
            query: ({listId, itemId}) => ({
                url: `/api/shopping-lists/${listId}/items/${itemId}/buy`,
                method: 'POST',
            }),
            invalidatesTags: (_res, _err, {listId}) => [
                {type: 'ShoppingListDetail', id: listId},
                {type: 'ShoppingLists', id: 'LIST'},
                {type: 'Stock', id: 'LIST'},
            ],
        }),
        unbuyShoppingListItem: build.mutation<ShoppingListItemDto, { listId: string; itemId: string }>({
            query: ({listId, itemId}) => ({
                url: `/api/shopping-lists/${listId}/items/${itemId}/unbuy`,
                method: 'POST',
            }),
            invalidatesTags: (_res, _err, {listId}) => [
                {type: 'ShoppingListDetail', id: listId},
                {type: 'ShoppingLists', id: 'LIST'},
                {type: 'Stock', id: 'LIST'},
            ],
        }),
        regenerateShoppingList: build.mutation<ShoppingListDetailDto, string>({
            query: (id) => ({url: `/api/shopping-lists/${id}/regenerate`, method: 'POST'}),
            invalidatesTags: (_res, _err, id) => [
                {type: 'ShoppingListDetail', id},
                {type: 'ShoppingLists', id: 'LIST'},
            ],
        }),
        // -------------------- Chat - Conversations --------------------
        listConversations: build.query<ConversationSummaryDto[], void>({
            query: () => '/api/chat/conversations',
            providesTags: ['ChatConversations'],
        }),
        createConversation: build.mutation<ConversationSummaryDto, void>({
            query: () => ({url: '/api/chat/conversations', method: 'POST'}),
            invalidatesTags: ['ChatConversations'],
        }),
        deleteConversation: build.mutation<void, string>({
            query: (id) => ({url: `/api/chat/conversations/${id}`, method: 'DELETE'}),
            invalidatesTags: ['ChatConversations'],
        }),
        // -------------------- Chat - Messages --------------------
        getChatMessages: build.query<ChatMessageDto[], string>({
            query: (conversationId) => `/api/chat/conversations/${conversationId}/messages`,
            providesTags: (_result, _err, id) => [{type: 'ChatMessages', id}],
        }),
        sendChatMessage: build.mutation<SendMessageResponseDto, {conversationId: string; content: string}>({
            query: ({conversationId, content}) => ({
                url: `/api/chat/conversations/${conversationId}/messages`,
                method: 'POST',
                body: {content},
            }),
            invalidatesTags: (_result, _err, {conversationId}) => [
                {type: 'ChatMessages', id: conversationId},
                'ChatConversations',
            ],
        }),
        // -------------------- Chat - Speech --------------------
        getSpeechToken: build.query<SpeechTokenDto, void>({
            query: () => '/api/chat/speech-token',
        }),
        // -------------------- Budget --------------------
        getBudgetSummary: build.query<MonthlySummaryDto, {year: number; month: number}>({
            query: ({year, month}) => `/api/budget/summary?year=${year}&month=${month}`,
            providesTags: (_r, _e, a) => [{type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
        }),
        listBudgetAccounts: build.query<BudgetAccountDto[], void>({
            query: () => '/api/budget/accounts',
            providesTags: ['BudgetAccounts'],
        }),
        createBudgetAccount: build.mutation<BudgetAccountDto, CreateAccountRequest>({
            query: (b) => ({url: '/api/budget/accounts', method: 'POST', body: b}),
            invalidatesTags: ['BudgetAccounts', {type: 'BudgetSummary', id: 'LIST'}],
        }),
        updateBudgetAccount: build.mutation<BudgetAccountDto, {id: string} & UpdateAccountRequest>({
            query: ({id, ...b}) => ({url: `/api/budget/accounts/${id}`, method: 'PUT', body: b}),
            invalidatesTags: ['BudgetAccounts', 'BudgetAccountDetail'],
        }),
        deleteBudgetAccount: build.mutation<void, string>({
            query: (id) => ({url: `/api/budget/accounts/${id}`, method: 'DELETE'}),
            invalidatesTags: ['BudgetAccounts', 'BudgetAccountDetail'],
        }),
        listBudgetGroups: build.query<CategoryGroupDto[], void>({
            query: () => '/api/budget/groups',
            providesTags: ['BudgetGroups'],
        }),
        createBudgetGroup: build.mutation<CategoryGroupDto, UpsertGroupRequest>({
            query: (b) => ({url: '/api/budget/groups', method: 'POST', body: b}),
            invalidatesTags: ['BudgetGroups', 'BudgetSummary'],
        }),
        updateBudgetGroup: build.mutation<CategoryGroupDto, {id: string} & UpsertGroupRequest>({
            query: ({id, ...b}) => ({url: `/api/budget/groups/${id}`, method: 'PUT', body: b}),
            invalidatesTags: ['BudgetGroups'],
        }),
        deleteBudgetGroup: build.mutation<void, string>({
            query: (id) => ({url: `/api/budget/groups/${id}`, method: 'DELETE'}),
            invalidatesTags: ['BudgetGroups'],
        }),
        createBudgetCategory: build.mutation<BudgetCategoryDto, UpsertCategoryRequest>({
            query: (b) => ({url: '/api/budget/categories', method: 'POST', body: b}),
            invalidatesTags: ['BudgetGroups', 'BudgetSummary'],
        }),
        updateBudgetCategory: build.mutation<BudgetCategoryDto, {id: string} & UpsertCategoryRequest>({
            query: ({id, ...b}) => ({url: `/api/budget/categories/${id}`, method: 'PUT', body: b}),
            invalidatesTags: ['BudgetGroups'],
        }),
        deleteBudgetCategory: build.mutation<void, string>({
            query: (id) => ({url: `/api/budget/categories/${id}`, method: 'DELETE'}),
            invalidatesTags: ['BudgetGroups'],
        }),
        setAssignedAmount: build.mutation<void, {categoryId: string; year: number; month: number; amount: number}>({
            query: (b) => ({url: '/api/budget/monthly/assigned', method: 'PUT', body: b}),
            invalidatesTags: (_r, _e, a) => [{type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
        }),
        moveMoney: build.mutation<void, MoveMoneyRequest>({
            query: (b) => ({url: '/api/budget/monthly/move', method: 'POST', body: b}),
            invalidatesTags: (_r, _e, a) => [{type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
        }),
        coverOverspending: build.mutation<void, CoverOverspendingRequest>({
            query: (b) => ({url: '/api/budget/monthly/cover', method: 'POST', body: b}),
            invalidatesTags: (_r, _e, a) => [{type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
        }),
        listBudgetAccountTransactions: build.query<
            AccountTransactionsPageDto,
            {accountId: string; year: number; month: number; skip?: number; take?: number}
        >({
            query: ({accountId, year, month, skip = 0, take = 50}) =>
                `/api/budget/accounts/${accountId}/transactions?year=${year}&month=${month}&skip=${skip}&take=${take}`,
            providesTags: (_r, _e, a) => [{type: 'BudgetAccountDetail', id: a.accountId}],
        }),
        listBudgetTransactions: build.query<BudgetTransactionDto[], {year: number; month: number; categoryId?: string}>({
            query: ({year, month, categoryId}) =>
                `/api/budget/transactions?year=${year}&month=${month}${categoryId ? `&categoryId=${categoryId}` : ''}`,
            providesTags: ['BudgetTransactions'],
        }),
        createBudgetTransaction: build.mutation<BudgetTransactionDto, CreateTransactionRequest & {year: number; month: number}>({
            query: ({year: _y, month: _m, ...b}) => ({url: '/api/budget/transactions', method: 'POST', body: b}),
            invalidatesTags: (_r, _e, a) => ['BudgetTransactions', 'BudgetAccounts', 'BudgetAccountDetail',
                {type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
        }),
        updateBudgetTransaction: build.mutation<BudgetTransactionDto, {id: string; year: number; month: number} & UpdateTransactionRequest>({
            query: ({id, year: _y, month: _m, ...b}) => ({url: `/api/budget/transactions/${id}`, method: 'PUT', body: b}),
            invalidatesTags: (_r, _e, a) => ['BudgetTransactions', 'BudgetAccounts',
                {type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
        }),
        deleteBudgetTransaction: build.mutation<void, {id: string; year: number; month: number}>({
            query: ({id}) => ({url: `/api/budget/transactions/${id}`, method: 'DELETE'}),
            invalidatesTags: (_r, _e, a) => ['BudgetTransactions', 'BudgetAccounts',
                {type: 'BudgetSummary', id: `${a.year}-${a.month}`}],
        }),
        // -------------------- Health: Drugs --------------------
        listDrugs: build.query<DrugDto[], {symptomId?: string} | void>({
            query: (arg) => {
                const symptomId = arg && 'symptomId' in arg ? arg.symptomId : undefined
                return symptomId
                    ? `/api/drugs?symptomId=${symptomId}`
                    : '/api/drugs'
            },
            providesTags: (result) =>
                result
                    ? [
                        ...result.map((d) => ({type: 'Drug' as const, id: d.id})),
                        {type: 'Drug', id: 'LIST'},
                    ]
                    : [{type: 'Drug', id: 'LIST'}],
        }),
        getDrug: build.query<DrugDetailDto, string>({
            query: (id) => `/api/drugs/${id}`,
            providesTags: (_r, _e, id) => [{type: 'Drug', id}],
        }),
        createDrug: build.mutation<DrugDetailDto, CreateDrugRequest>({
            query: (body) => ({url: '/api/drugs', method: 'POST', body}),
            invalidatesTags: [{type: 'Drug', id: 'LIST'}],
        }),
        updateDrug: build.mutation<DrugDetailDto, {id: string} & UpdateDrugRequest>({
            query: ({id, ...body}) => ({
                url: `/api/drugs/${id}`,
                method: 'PUT',
                body,
            }),
            invalidatesTags: (_r, _e, a) => [
                {type: 'Drug', id: a.id},
                {type: 'Drug', id: 'LIST'},
            ],
        }),
        deleteDrug: build.mutation<void, string>({
            query: (id) => ({url: `/api/drugs/${id}`, method: 'DELETE'}),
            invalidatesTags: (_r, _e, id) => [
                {type: 'Drug', id},
                {type: 'Drug', id: 'LIST'},
            ],
        }),
        attachDrugPhotos: build.mutation<PhotoRefDto[], {drugId: string; photos: AttachedPhotoInfo[]}>({
            query: ({drugId, photos}) => ({
                url: `/api/drugs/${drugId}/photos`,
                method: 'POST',
                body: {photos},
            }),
            invalidatesTags: (_r, _e, a) => [
                {type: 'Drug', id: a.drugId},
                {type: 'Drug', id: 'LIST'},
            ],
        }),
        // -------------------- Health: Symptoms + Triggers --------------------
        listSymptoms: build.query<SymptomDto[], void>({
            query: () => '/api/symptoms',
            providesTags: [{type: 'Symptom', id: 'LIST'}],
        }),
        createCustomSymptom: build.mutation<SymptomDto, CreateCustomSymptomRequest>({
            query: (body) => ({url: '/api/symptoms', method: 'POST', body}),
            invalidatesTags: [{type: 'Symptom', id: 'LIST'}],
        }),
        listTriggers: build.query<TriggerDto[], void>({
            query: () => '/api/triggers',
            providesTags: [{type: 'Trigger', id: 'LIST'}],
        }),
        createCustomTrigger: build.mutation<TriggerDto, CreateCustomTriggerRequest>({
            query: (body) => ({url: '/api/triggers', method: 'POST', body}),
            invalidatesTags: [{type: 'Trigger', id: 'LIST'}],
        }),
        // -------------------- Health: Episodes --------------------
        listEpisodes: build.query<EpisodeDto[], ListEpisodesQueryArgs | void>({
            query: (arg) => {
                const a = arg ?? {}
                const params = new URLSearchParams()
                if (a.from) params.set('from', a.from)
                if (a.to) params.set('to', a.to)
                if (a.symptomId) params.set('symptomId', a.symptomId)
                if (a.onlyResolved != null) params.set('onlyResolved', String(a.onlyResolved))
                if (a.onlyFailed != null) params.set('onlyFailed', String(a.onlyFailed))
                const qs = params.toString()
                return qs ? `/api/episodes?${qs}` : '/api/episodes'
            },
            providesTags: (result) =>
                result
                    ? [
                        ...result.map((e) => ({type: 'Episode' as const, id: e.id})),
                        {type: 'Episode', id: 'LIST'},
                    ]
                    : [{type: 'Episode', id: 'LIST'}],
        }),
        getActiveEpisodes: build.query<EpisodeDto[], void>({
            query: () => '/api/episodes/active',
            providesTags: [{type: 'ActiveEpisode', id: 'LIST'}],
        }),
        getEpisode: build.query<EpisodeDetailDto, string>({
            query: (id) => `/api/episodes/${id}`,
            providesTags: (_r, _e, id) => [{type: 'Episode', id}],
        }),
        startEpisode: build.mutation<EpisodeDto, StartEpisodeRequest>({
            query: (body) => ({url: '/api/episodes', method: 'POST', body}),
            invalidatesTags: [
                {type: 'Episode', id: 'LIST'},
                {type: 'ActiveEpisode', id: 'LIST'},
            ],
        }),
        updateEpisode: build.mutation<EpisodeDetailDto, {id: string} & UpdateEpisodeRequest>({
            query: ({id, ...body}) => ({
                url: `/api/episodes/${id}`,
                method: 'PUT',
                body,
            }),
            invalidatesTags: (_r, _e, a) => [
                {type: 'Episode', id: a.id},
                {type: 'Episode', id: 'LIST'},
                {type: 'ActiveEpisode', id: 'LIST'},
            ],
        }),
        resolveEpisode: build.mutation<EpisodeDetailDto, {id: string} & ResolveEpisodeRequest>({
            query: ({id, ...body}) => ({
                url: `/api/episodes/${id}/resolve`,
                method: 'POST',
                body,
            }),
            invalidatesTags: (_r, _e, a) => [
                {type: 'Episode', id: a.id},
                {type: 'Episode', id: 'LIST'},
                {type: 'ActiveEpisode', id: 'LIST'},
            ],
        }),
        deleteEpisode: build.mutation<void, string>({
            query: (id) => ({url: `/api/episodes/${id}`, method: 'DELETE'}),
            invalidatesTags: (_r, _e, id) => [
                {type: 'Episode', id},
                {type: 'Episode', id: 'LIST'},
                {type: 'ActiveEpisode', id: 'LIST'},
            ],
        }),
        attachEpisodePhotos: build.mutation<PhotoRefDto[], {episodeId: string; photos: AttachedPhotoInfo[]}>({
            query: ({episodeId, photos}) => ({
                url: `/api/episodes/${episodeId}/photos`,
                method: 'POST',
                body: {photos},
            }),
            invalidatesTags: (_r, _e, a) => [
                {type: 'Episode', id: a.episodeId},
                {type: 'Episode', id: 'LIST'},
            ],
        }),
        logNoDrug: build.mutation<void, {episodeId: string} & LogNoDrugRequest>({
            query: ({episodeId, ...body}) => ({
                url: `/api/episodes/${episodeId}/no-drug`,
                method: 'POST',
                body,
            }),
            invalidatesTags: (_r, _e, a) => [
                {type: 'Episode', id: a.episodeId},
                {type: 'Episode', id: 'LIST'},
                {type: 'ActiveEpisode', id: 'LIST'},
            ],
        }),
        // -------------------- Health: Take Medication context --------------------
        getTakeMedicationContext: build.query<TakeMedicationContextDto, string>({
            query: (episodeId) => `/api/episodes/${episodeId}/take-medication-context`,
            providesTags: (_r, _e, episodeId) => [
                {type: 'Episode', id: `take-med-${episodeId}`},
            ],
        }),
        // -------------------- Health: Intakes --------------------
        logIntake: build.mutation<IntakeDto, LogIntakeRequest>({
            query: (body) => ({url: '/api/intakes', method: 'POST', body}),
            invalidatesTags: (_r, _e, a) => {
                const tags: Array<{type: 'Episode' | 'ActiveEpisode' | 'Drug'; id: string}> = [
                    {type: 'Drug', id: a.drugId},
                    {type: 'Drug', id: 'LIST'},
                ]
                if (a.symptomEpisodeId) {
                    tags.push({type: 'Episode', id: a.symptomEpisodeId})
                    tags.push({type: 'Episode', id: `take-med-${a.symptomEpisodeId}`})
                }
                tags.push({type: 'Episode', id: 'LIST'})
                tags.push({type: 'ActiveEpisode', id: 'LIST'})
                return tags
            },
        }),
        // -------------------- Health: Follow-ups --------------------
        recordPingResponse: build.mutation<void, {pingId: string} & RecordPingResponseRequest>({
            query: ({pingId, ...body}) => ({
                url: `/api/followups/${pingId}/respond`,
                method: 'POST',
                body,
            }),
            invalidatesTags: [
                {type: 'Episode', id: 'LIST'},
                {type: 'ActiveEpisode', id: 'LIST'},
            ],
        }),
        retroCloseEpisode: build.mutation<void, {episodeId: string} & RetroCloseEpisodeRequest>({
            query: ({episodeId, ...body}) => ({
                url: `/api/episodes/${episodeId}/retro-close`,
                method: 'POST',
                body,
            }),
            invalidatesTags: (_r, _e, a) => [
                {type: 'Episode', id: a.episodeId},
                {type: 'Episode', id: 'LIST'},
                {type: 'ActiveEpisode', id: 'LIST'},
            ],
        }),
        // -------------------- Health: Photos (SAS upload) --------------------
        requestUploadSas: build.mutation<UploadSasResponse, RequestUploadSasRequest>({
            query: (body) => ({url: '/api/photos/upload-sas', method: 'POST', body}),
        }),
        // -------------------- Health: Push Subscriptions --------------------
        subscribeWebPush: build.mutation<SubscribeWebPushResultDto, SubscribeWebPushRequest>({
            query: (body) => ({url: '/api/push-subscriptions', method: 'POST', body}),
            invalidatesTags: [{type: 'PushSubscription', id: 'LIST'}],
        }),
        unsubscribeWebPush: build.mutation<void, UnsubscribeWebPushRequest>({
            query: (body) => ({url: '/api/push-subscriptions', method: 'DELETE', body}),
            invalidatesTags: [{type: 'PushSubscription', id: 'LIST'}],
        }),
        getVapidPublicKey: build.query<VapidPublicKeyDto, void>({
            query: () => '/api/push-subscriptions/vapid-public-key',
        }),
        // -------------------- Health: Share Links --------------------
        createShareLink: build.mutation<CreateShareLinkResultDto, CreateShareLinkRequest>({
            query: (body) => ({url: '/api/share-links', method: 'POST', body}),
            invalidatesTags: [{type: 'ShareLink', id: 'LIST'}],
        }),
        listMyShareLinks: build.query<ShareLinkSummaryDto[], void>({
            query: () => '/api/share-links',
            providesTags: [{type: 'ShareLink', id: 'LIST'}],
        }),
        revokeShareLink: build.mutation<void, string>({
            query: (id) => ({url: `/api/share-links/${id}`, method: 'DELETE'}),
            invalidatesTags: [{type: 'ShareLink', id: 'LIST'}],
        }),
        // -------------------- Trips --------------------
        listTrips: build.query<TripDto[], void>({
            query: () => '/api/trips',
            providesTags: ['Trips'],
        }),
        getTrip: build.query<TripDto, string>({
            query: (id) => `/api/trips/${id}`,
            providesTags: (_r, _e, id) => [{type: 'TripDetail', id}],
        }),
        createTrip: build.mutation<TripDto, {name: string; destination?: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode}>({
            query: (b) => ({url: '/api/trips', method: 'POST', body: b}),
            invalidatesTags: ['Trips'],
        }),
        updateTrip: build.mutation<TripDto, {id: string; name: string; destination?: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode}>({
            query: ({id, ...b}) => ({url: `/api/trips/${id}`, method: 'PUT', body: b}),
            invalidatesTags: (_r, _e, a) => ['Trips', {type: 'TripDetail', id: a.id}, {type: 'TripItinerary', id: a.id}],
        }),
        deleteTrip: build.mutation<void, string>({
            query: (id) => ({url: `/api/trips/${id}`, method: 'DELETE'}),
            invalidatesTags: (_r, _e, id) => ['Trips', {type: 'TripDetail', id}, {type: 'TripItinerary', id}],
        }),
        resolvePlace: build.mutation<ResolvedPlaceDto, {url: string}>({
            query: (b) => ({url: '/api/trips/resolve-place', method: 'POST', body: b}),
        }),
        listTripPlaces: build.query<TripPlaceDto[], string>({
            query: (tripId) => `/api/trips/${tripId}/places`,
            providesTags: (_r, _e, id) => [{type: 'TripPlaces', id}],
        }),
        addTripPlace: build.mutation<TripPlaceDto, {tripId: string} & Omit<TripPlaceDto, 'id' | 'tripId' | 'bestTimeStart' | 'bestTimeEnd' | 'feeNote' | 'notes' | 'hasProfile'>>({
            query: ({tripId, ...b}) => ({url: `/api/trips/${tripId}/places`, method: 'POST', body: b}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'TripItinerary', id: a.tripId}],
        }),
        updateTripPlace: build.mutation<TripPlaceDto, {tripId: string; placeId: string; name: string; category: PlaceCategory; address?: string | null; feeNote?: string | null; notes?: string | null; bestTimeStart?: string | null; bestTimeEnd?: string | null; reviewLinks: ReviewLink[]}>({
            query: ({tripId, placeId, ...b}) => ({url: `/api/trips/${tripId}/places/${placeId}`, method: 'PUT', body: b}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'TripItinerary', id: a.tripId}],
        }),
        deleteTripPlace: build.mutation<void, {tripId: string; placeId: string}>({
            query: ({tripId, placeId}) => ({url: `/api/trips/${tripId}/places/${placeId}`, method: 'DELETE'}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'TripItinerary', id: a.tripId}],
        }),
        pushPlaceProfile: build.mutation<TripPlaceDto, {tripId: string; placeId: string}>({
            query: ({tripId, placeId}) => ({url: `/api/trips/${tripId}/places/${placeId}/push-to-profile`, method: 'POST'}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}],
        }),
        listChecklistItems: build.query<ChecklistItem[], void>({
            query: () => `/api/checklist-items`,
            providesTags: [{type: 'ChecklistItems', id: 'LIST'}],
        }),
        attachChecklistItem: build.mutation<PlaceChecklistEntry, {tripId: string; placeId: string; name: string}>({
            query: ({tripId, placeId, name}) => ({url: `/api/trips/${tripId}/places/${placeId}/checklist`, method: 'POST', body: {name}}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}, {type: 'ChecklistItems', id: 'LIST'}],
        }),
        detachChecklistItem: build.mutation<void, {tripId: string; placeId: string; entryId: string}>({
            query: ({tripId, placeId, entryId}) => ({url: `/api/trips/${tripId}/places/${placeId}/checklist/${entryId}`, method: 'DELETE'}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripPlaces', id: a.tripId}],
        }),
        setChecklistEntryChecked: build.mutation<void, {tripId: string; placeId: string; entryId: string; isChecked: boolean}>({
            query: ({tripId, placeId, entryId, isChecked}) => ({
                url: `/api/trips/${tripId}/places/${placeId}/checklist/${entryId}`, method: 'PATCH', body: {isChecked},
            }),
            // Optimistic, NON-invalidating (ADR-042 / ADR-060). placesById is fed by listTripPlaces,
            // keyed by the plain tripId string -- a single-entry patch (simpler than setStopVisited's fan-out).
            onQueryStarted: async ({tripId, placeId, entryId, isChecked}, {dispatch, queryFulfilled}) => {
                const patch = dispatch(
                    api.util.updateQueryData('listTripPlaces', tripId, (draft) => {
                        const place = draft.find((p) => p.id === placeId)
                        const entry = place?.checklist.find((e) => e.id === entryId)
                        if (entry) entry.isChecked = isChecked
                    }),
                )
                try {
                    await queryFulfilled
                } catch {
                    patch.undo()
                }
            },
        }),
        // `tz` is optional here to mirror the backend contract (the API requires it only for a
        // day flagged current-time-start), but the SPA always passes it — see getViewerTimeZone.
        getItinerary: build.query<ItineraryDayDto[], {tripId: string; tz?: string; lat?: number; lng?: number}>({
            query: ({tripId, tz, lat, lng}) => {
                const params = new URLSearchParams()
                if (tz) params.set('tz', tz)
                if (lat != null && lng != null) {
                    params.set('lat', String(lat))
                    params.set('lng', String(lng))
                }
                const qs = params.toString()
                return `/api/trips/${tripId}/itinerary${qs ? `?${qs}` : ''}`
            },
            providesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
        }),
        addStop: build.mutation<StopDto, {tripId: string; dayId: string; tripPlaceId: string; dwellMinutes: number; travelModeToReach: TravelMode}>({
            query: ({tripId, dayId, ...b}) => ({url: `/api/trips/${tripId}/days/${dayId}/stops`, method: 'POST', body: b}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
        }),
        updateStop: build.mutation<void, {tripId: string; stopId: string; dwellMinutes?: number | null; travelModeToReach?: TravelMode | null}>({
            query: ({tripId, stopId, ...b}) => ({url: `/api/trips/${tripId}/stops/${stopId}`, method: 'PATCH', body: b}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
        }),
        setStopVisited: build.mutation<void, {tripId: string; stopId: string; isVisited: boolean}>({
            query: ({tripId, stopId, isVisited}) => ({
                url: `/api/trips/${tripId}/stops/${stopId}`, method: 'PATCH', body: {isVisited},
            }),
            // Display-only toggle (ADR-039/042): NO invalidatesTags, so getItinerary never
            // refetches (a refetch re-bills the Google Routes API + re-fetches Weather).
            // Optimistically patch every live getItinerary cache entry for this trip
            // (keyed by {tripId,tz,lat,lng} → possibly several), undo on failure.
            onQueryStarted: async ({tripId, stopId, isVisited}, {dispatch, queryFulfilled, getState}) => {
                const entries = api.util.selectInvalidatedBy(getState(), [{type: 'TripItinerary', id: tripId}])
                const patches = entries
                    .filter((e) => e.endpointName === 'getItinerary')
                    .map((e) =>
                        dispatch(
                            api.util.updateQueryData(
                                'getItinerary',
                                e.originalArgs as {tripId: string; tz?: string; lat?: number; lng?: number},
                                (draft) => {
                                    for (const day of draft) {
                                        const s = day.stops.find((x) => x.id === stopId)
                                        if (s) s.isVisited = isVisited
                                    }
                                },
                            ),
                        ),
                    )
                try {
                    await queryFulfilled
                } catch {
                    patches.forEach((p) => p.undo()) // revert; ItineraryTab surfaces the error (Task 5)
                }
            },
        }),
        removeStop: build.mutation<void, {tripId: string; stopId: string}>({
            query: ({tripId, stopId}) => ({url: `/api/trips/${tripId}/stops/${stopId}`, method: 'DELETE'}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
        }),
        reorderStops: build.mutation<void, {tripId: string; dayId: string; orderedStopIds: string[]}>({
            query: ({tripId, dayId, orderedStopIds}) => ({url: `/api/trips/${tripId}/days/${dayId}/reorder`, method: 'POST', body: {orderedStopIds}}),
            // No invalidatesTags: the itinerary view refetches explicitly after a reorder so its
            // full-view loader can span the recompute deterministically (see ItineraryTab handleDragEnd).
        }),
        setDayStartTime: build.mutation<void, {tripId: string; dayId: string; startTime: string}>({
            query: ({tripId, dayId, startTime}) => ({url: `/api/trips/${tripId}/days/${dayId}`, method: 'PATCH', body: {startTime}}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
        }),
        setDayUseCurrentTime: build.mutation<void, {tripId: string; dayId: string; useCurrentTime: boolean}>({
            query: ({tripId, dayId, useCurrentTime}) => ({url: `/api/trips/${tripId}/days/${dayId}/use-current-time`, method: 'PATCH', body: {useCurrentTime}}),
            invalidatesTags: (_r, _e, a) => [{type: 'TripItinerary', id: a.tripId}],
        }),
        // -------------------- Trip weather --------------------
        getStopWeather: build.query<
            WeatherReadingDto[],
            {kind: 'Now' | 'OnArrival'; points: WeatherPointDto[]}
        >({
            query: ({kind, points}) => ({
                url: '/api/trips/weather',
                method: 'POST',
                body: {kind, points: [...points].sort((a, b) => a.stopId.localeCompare(b.stopId))},
            }),
            // Cache key must not depend on point order (the schedule can produce the same set
            // in any order). Weather is ephemeral, so no providesTags and a short retention.
            serializeQueryArgs: ({endpointName, queryArgs}) => ({
                endpointName,
                kind: queryArgs.kind,
                points: [...queryArgs.points].sort((a, b) => a.stopId.localeCompare(b.stopId)),
            }),
            keepUnusedDataFor: 300,
        }),
    }),
})

/**
 * Anonymous-only API slice for the public doctor-report endpoint. The
 * main `api` slice attaches an MSAL/Google bearer to every request via
 * `prepareHeaders`; the share-token endpoint must NOT carry that header
 * (it's authenticated via the `?t=` query string instead and may be hit
 * from a browser that has no signed-in user at all — e.g., a doctor on
 * their own laptop). Splitting into a second slice with a clean
 * `fetchBaseQuery` is the simplest way to opt out of the interceptor.
 */
export const publicApi = createApi({
    reducerPath: 'publicApi',
    baseQuery: fetchBaseQuery({
        baseUrl: import.meta.env.VITE_API_BASE_URL || '/',
    }),
    tagTypes: ['PublicReport'],
    endpoints: (build) => ({
        getDoctorReport: build.query<DoctorReportDto, string>({
            query: (token) => `/api/public/report?t=${encodeURIComponent(token)}`,
            providesTags: (_r, _e, token) => [{type: 'PublicReport', id: token}],
        }),
    }),
})
export const {
    useGetMeQuery,
    useCreateFamilyMutation,
    useJoinFamilyMutation,
    useListFamilyMembersQuery,
    useRotateInviteCodeMutation,
    useLeaveFamilyMutation,
    useListRelationshipsQuery,
    useAddRelationshipMutation,
    useDeleteRelationshipMutation,
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
    useGetShoppingListDetailQuery,
    useCreateShoppingListMutation,
    useDeleteShoppingListMutation,
    useCompleteShoppingListMutation,
    useAddShoppingListItemMutation,
    useDeleteShoppingListItemMutation,
    useBuyShoppingListItemMutation,
    useUnbuyShoppingListItemMutation,
    useRegenerateShoppingListMutation,
    useListConversationsQuery,
    useCreateConversationMutation,
    useDeleteConversationMutation,
    useGetChatMessagesQuery,
    useSendChatMessageMutation,
    useGetSpeechTokenQuery,
    useGetBudgetSummaryQuery,
    useListBudgetAccountsQuery,
    useCreateBudgetAccountMutation,
    useUpdateBudgetAccountMutation,
    useDeleteBudgetAccountMutation,
    useListBudgetGroupsQuery,
    useCreateBudgetGroupMutation,
    useUpdateBudgetGroupMutation,
    useDeleteBudgetGroupMutation,
    useCreateBudgetCategoryMutation,
    useUpdateBudgetCategoryMutation,
    useDeleteBudgetCategoryMutation,
    useSetAssignedAmountMutation,
    useMoveMoneyMutation,
    useCoverOverspendingMutation,
    useListBudgetAccountTransactionsQuery,
    useListBudgetTransactionsQuery,
    useCreateBudgetTransactionMutation,
    useUpdateBudgetTransactionMutation,
    useDeleteBudgetTransactionMutation,
    // -------- Health --------
    useListDrugsQuery,
    useGetDrugQuery,
    useCreateDrugMutation,
    useUpdateDrugMutation,
    useDeleteDrugMutation,
    useAttachDrugPhotosMutation,
    useListSymptomsQuery,
    useCreateCustomSymptomMutation,
    useListTriggersQuery,
    useCreateCustomTriggerMutation,
    useListEpisodesQuery,
    useGetActiveEpisodesQuery,
    useGetEpisodeQuery,
    useStartEpisodeMutation,
    useUpdateEpisodeMutation,
    useResolveEpisodeMutation,
    useDeleteEpisodeMutation,
    useAttachEpisodePhotosMutation,
    useLogNoDrugMutation,
    useGetTakeMedicationContextQuery,
    useLogIntakeMutation,
    useRecordPingResponseMutation,
    useRetroCloseEpisodeMutation,
    useRequestUploadSasMutation,
    useSubscribeWebPushMutation,
    useUnsubscribeWebPushMutation,
    useGetVapidPublicKeyQuery,
    useCreateShareLinkMutation,
    useListMyShareLinksQuery,
    useRevokeShareLinkMutation,
    // -------- Trips --------
    useListTripsQuery,
    useGetTripQuery,
    useCreateTripMutation,
    useUpdateTripMutation,
    useDeleteTripMutation,
    useResolvePlaceMutation,
    useListTripPlacesQuery,
    useAddTripPlaceMutation,
    useUpdateTripPlaceMutation,
    useDeleteTripPlaceMutation,
    usePushPlaceProfileMutation,
    useListChecklistItemsQuery,
    useAttachChecklistItemMutation,
    useDetachChecklistItemMutation,
    useSetChecklistEntryCheckedMutation,
    useGetItineraryQuery,
    useAddStopMutation,
    useUpdateStopMutation,
    useSetStopVisitedMutation,
    useRemoveStopMutation,
    useReorderStopsMutation,
    useSetDayStartTimeMutation,
    useSetDayUseCurrentTimeMutation,
    useGetStopWeatherQuery,
} = api

export const {
    useGetDoctorReportQuery,
} = publicApi
