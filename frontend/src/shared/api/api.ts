import {createApi, fetchBaseQuery} from '@reduxjs/toolkit/query/react'
import {InteractionRequiredAuthError} from '@azure/msal-browser'
import {apiScopes, msalInstance} from '../auth/msalConfig'
import {getGoogleToken} from '../auth/googleAuth'

/**
 * Single, app-wide RTK Query API. All endpoints for every feature
 * live here. Feature pages import generated hooks (e.g.,
 * `useListRecipesQuery`) directly from this module.
 *
 * Endpoints are stubbed for now and will be filled in as the backend
 * gains real handlers.
 */

async function acquireAccessToken(): Promise<string | null> {
    // Try MSAL first (Microsoft)
    const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0]
    if (account && apiScopes.length > 0) {
        try {
            const result = await msalInstance.acquireTokenSilent({scopes: apiScopes, account})
            return result.accessToken
        } catch (err) {
            if (err instanceof InteractionRequiredAuthError) {
                await msalInstance.acquireTokenRedirect({scopes: apiScopes, account})
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
        'Relationships',
        'ShoppingLists',
        'ShoppingListDetail',
        'ChatConversations',
        'ChatMessages',
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
} = api
