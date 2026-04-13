# MenuNest — Implementation Plan

**App name:** MenuNest (family meal planning)

## Context

Greenfield app for family meal planning — users build a recipe library, track their ingredient stock, plan meals by day and slot, and the system tells them whether they have enough stock for the chosen recipes. Sign-in is via Microsoft Entra ID; users are grouped into a `Family` (one user belongs to exactly one family). Deployed on Azure.

**Stack (confirmed):**
- Frontend: React + TypeScript + Vite, Redux Toolkit (RTK + RTK Query), React Router, Syncfusion Community License
- Backend: ASP.NET 10 (LTS) Clean Architecture
- Database: Azure SQL Database (empty — fresh schema via EF Core migrations)
- Storage: Azure Blob Storage (recipe photos)
- Auth: Microsoft Entra ID — multi-tenant + personal accounts (`common` authority)
- Hosting: Azure App Service (backend), Azure Static Web Apps or App Service (frontend)

## Scope (MVP)

**In scope:**
1. Entra ID sign-in (multi-tenant + personal)
2. Family management — create family, invite code, join, leave, list members, set relationships (metadata)
3. Per-family ingredient master — autocomplete + on-the-fly creation, each ingredient has one fixed unit
4. Recipe CRUD + ingredients + photo upload (Blob Storage)
5. Stock — manual CRUD per ingredient (family-scoped)
6. Meal plan — day × slot (breakfast / lunch / dinner) → pick a recipe
7. Stock check — for a planned meal, report whether stock is sufficient and what's missing
8. **Cook action** — clicking "Cook now" on a meal plan entry deducts stock automatically (warn and deduct what's available if stock is insufficient)
9. **Shopping list** — create/edit persistent shopping lists per family, generate from a meal plan range, mark bought → auto-increment stock

**Out of scope (MVP):** multi-unit conversion, permission roles, mobile app, notifications, multi-language (Thai by default), real-time shopping list collaboration, meal plan drag-and-drop

## Data Model

Tables (EF Core entities). Every family-owned entity has a `FamilyId` foreign key, and all queries are filtered by `FamilyId` derived from the caller's claims.

```
Family
  Id (Guid, PK), Name, InviteCode (unique, regenerable), CreatedAt, CreatedByUserId

User
  Id (Guid, PK), ExternalId (Entra oid claim, unique), Email, DisplayName,
  FamilyId (FK, nullable — null means no family yet), JoinedAt

UserRelationship                       -- metadata only (display purposes)
  Id, FamilyId, FromUserId, ToUserId, RelationType (enum: Parent/Child/Spouse/Sibling/Other)

Ingredient                              -- family-scoped master
  Id, FamilyId, Name, Unit, CreatedAt
  UNIQUE (FamilyId, Name)

Recipe
  Id, FamilyId, Name, Description, ImageBlobPath (nullable),
  CreatedByUserId, CreatedAt, UpdatedAt

RecipeIngredient
  Id, RecipeId (FK), IngredientId (FK), Quantity (decimal)
  UNIQUE (RecipeId, IngredientId)

StockItem                               -- 1 row per ingredient per family
  Id, FamilyId, IngredientId (FK), Quantity (decimal), UpdatedAt, UpdatedByUserId
  UNIQUE (FamilyId, IngredientId)

MealPlanEntry
  Id, FamilyId, Date, MealSlot (enum: Breakfast/Lunch/Dinner),
  RecipeId (FK), Notes, CreatedByUserId,
  Status (enum: Planned/Cooked/Skipped, default Planned),
  CookedAt (DateTime, nullable),
  CookedByUserId (FK, nullable),
  CookNotes (string, nullable)          -- e.g., "stock short for eggs — used 1 instead of 2"
  UNIQUE (FamilyId, Date, MealSlot)     -- 1 recipe per slot per day

StockTransaction                        -- audit log of every stock change
  Id, FamilyId, IngredientId (FK), Delta (decimal, +/-),
  Source (enum: Manual/Cook/ShoppingListBought/Correction),
  SourceRefId (Guid, nullable),         -- MealPlanEntryId or ShoppingListItemId
  CreatedAt, CreatedByUserId, Notes
  -- StockItem.Quantity is the running total; updated in handlers and audited here

ShoppingList
  Id, FamilyId, Name, Status (enum: Active/Completed/Archived),
  CreatedAt, CreatedByUserId, CompletedAt (nullable)

ShoppingListItem
  Id, ShoppingListId (FK), IngredientId (FK), Quantity (decimal),
  IsBought (bool, default false), BoughtAt (DateTime, nullable), BoughtByUserId (FK, nullable),
  SourceMealPlanEntryIds (json, nullable)    -- tracks which meal plan entries produced this item
  UNIQUE (ShoppingListId, IngredientId)      -- aggregated: one row per ingredient per list
```

## Backend — ASP.NET Clean Architecture

**Solution layout** (`backend/`):

```
src/
  MenuNest.Domain/           -- entities, enums, domain exceptions (no external deps)
  MenuNest.Application/      -- Use-case handlers (CQRS), DTOs, interfaces, validators (FluentValidation)
  MenuNest.Infrastructure/   -- EF Core DbContext, repositories, Blob client, CurrentUserService
  MenuNest.WebApi/           -- Controllers, middleware, DI wiring, Program.cs
tests/
  MenuNest.Application.UnitTests/
  MenuNest.Infrastructure.IntegrationTests/
```

**Key packages:**
- `Microsoft.Identity.Web` — JWT bearer validation (authority = `https://login.microsoftonline.com/common/v2.0`)
- `Microsoft.EntityFrameworkCore.SqlServer` + `EFCore.Tools`
- `Mediator` (martinothamar/Mediator — MIT, source-generator based)
- `FluentValidation` (request validation)
- `Mapster` (DTO mapping — free, fast, source-gen)
- **CQRS + Mediator pattern** — commands and queries are request classes, one handler per use case:
  ```
  Application/
    UseCases/
      Recipes/
        CreateRecipe/
          CreateRecipeCommand.cs       -- implements ICommand<RecipeDto>
          CreateRecipeValidator.cs     -- FluentValidation
          CreateRecipeHandler.cs       -- ICommandHandler<CreateRecipeCommand, RecipeDto>
        GetRecipeById/
          GetRecipeByIdQuery.cs        -- implements IQuery<RecipeDto>
          GetRecipeByIdHandler.cs
  Common/
    Behaviors/
      ValidationBehavior.cs            -- IPipelineBehavior → validate before the handler runs
      LoggingBehavior.cs
      TransactionBehavior.cs           -- commits the TX around each command
  ```
  The controller injects a single `IMediator` and calls `mediator.Send(command/query)`.
- `Azure.Storage.Blobs`

**Cross-cutting concerns:**
- `ICurrentUserService` — resolves the `oid` claim to a `User` record, cached per request. Auto-provisions a user row on first login.
- `IFamilyContext` — repositories scope all queries to `CurrentUser.FamilyId`. Requests without a family (new user) are rejected by a filter attribute except for family-join endpoints.
- Global exception middleware → ProblemDetails response
- Serilog → Azure Application Insights

**API endpoints (grouped):**

| Route | Purpose |
|---|---|
| `GET  /api/me` | current user + family info |
| `POST /api/families` | create a new family (the current user becomes the creator) |
| `POST /api/families/join` | body: `{ inviteCode }` |
| `POST /api/families/leave` | leave the current family |
| `GET  /api/families/me/members` | list members + relationships |
| `POST /api/families/me/invite-codes/rotate` | regenerate code |
| `POST /api/families/me/relationships` | add relationship metadata |
| `GET/POST/PUT/DELETE /api/ingredients` | family-scoped ingredient master |
| `GET/POST/PUT/DELETE /api/recipes` | recipes + nested `RecipeIngredient` |
| `POST /api/recipes/{id}/image` | multipart upload → Blob → save path |
| `GET/POST/PUT/DELETE /api/stock` | stock items |
| `GET/POST/PUT/DELETE /api/meal-plan?from=&to=` | meal plan entries by date range |
| `GET  /api/meal-plan/{id}/stock-check` | returns `{ required[], available[], missing[] }` |
| `POST /api/meal-plan/{id}/cook` | deduct stock (clamped at 0) + mark Status=Cooked + record StockTransaction. Response: `{ deducted[], partial[], missingItems[] }` |
| `POST /api/meal-plan/{id}/uncook` | undo cook → add stock back from StockTransaction + reset Status=Planned |
| `GET  /api/shopping-lists` | list all family lists (filter by status) |
| `POST /api/shopping-lists` | body: `{ name, fromMealPlan?: { fromDate, toDate } }`. If `fromMealPlan` is set, auto-populates items from missing ingredients |
| `GET /api/shopping-lists/{id}` | detail + items |
| `PUT /api/shopping-lists/{id}` | update name/status |
| `DELETE /api/shopping-lists/{id}` | |
| `POST /api/shopping-lists/{id}/items` | add an item manually |
| `PATCH /api/shopping-lists/{id}/items/{itemId}` | mark bought → POST StockTransaction (+quantity, Source=ShoppingListBought) |
| `POST /api/shopping-lists/{id}/regenerate` | recompute items from the meal plan range (preserving IsBought=true rows) |

**Authentication:**
- `AddMicrosoftIdentityWebApi()` with `TenantId = "common"` and `AllowWebApiToBeAuthorizedByACL = false`
- `ValidateIssuer = false` with a custom validator — accepts `login.microsoftonline.com/{anyTenantId}/v2.0` plus `sts.windows.net/9188040d-...` (the personal MSA tenant)
- Every controller is `[Authorize]` except the health check

**Image upload flow (MVP — proxy through the API):**
1. Frontend sends `POST /api/recipes/{id}/image` multipart
2. Backend validates (MIME jpeg/png/webp, size ≤ 5 MB)
3. Uploads to the Blob container `recipe-images` at path `{familyId}/{recipeId}/{guid}.{ext}`
4. Saves the path in `Recipe.ImageBlobPath`
5. Frontend reads via `GET /api/recipes/{id}/image` (backend streams from Blob with cache headers) — the Blob URL is not exposed directly

## Frontend — React + Vite

**Structure** (`frontend/src/`):

```
pages/
  recipes/
    components/           RecipeCard.tsx, RecipeList.tsx, RecipeForm.tsx, IngredientPicker.tsx
    hooks/                useRecipes.ts, useRecipeForm.ts
    recipesApi.ts         -- RTK Query endpoints
    recipesSlice.ts       -- local UI state (filter, selection)
    RecipesPage.tsx       -- container
    RecipeDetailPage.tsx
    index.ts              -- barrel
  stock/
    components/ hooks/ stockApi.ts stockSlice.ts StockPage.tsx
  meal-plan/
    components/           MealPlanCalendar.tsx, MealSlotCard.tsx, StockCheckPanel.tsx, CookConfirmDialog.tsx
    hooks/                useMealPlan.ts, useStockCheck.ts, useCookMeal.ts
    mealPlanApi.ts mealPlanSlice.ts MealPlanPage.tsx
  shopping/
    components/           ShoppingListCard.tsx, ShoppingListDetail.tsx, ShoppingItemRow.tsx, GenerateFromPlanDialog.tsx
    hooks/                useShoppingLists.ts, useShoppingListDetail.ts
    shoppingApi.ts shoppingSlice.ts ShoppingListsPage.tsx ShoppingListDetailPage.tsx
  ingredients/
    ... (CRUD page for the master list)
  family/
    components/ hooks/ familyApi.ts familySlice.ts FamilyPage.tsx JoinFamilyPage.tsx
  auth/
    LoginPage.tsx, AuthCallback.tsx
shared/
  components/             AppLayout.tsx, NavBar.tsx, ProtectedRoute.tsx, FamilyRequiredRoute.tsx
  hooks/                  useCurrentUser.ts, useDebounce.ts
  api/                    baseApi.ts (RTK Query base with auth header injection)
  auth/                   msalConfig.ts, authProvider.tsx
  utils/                  formatters.ts
store/
  index.ts                -- configureStore, combines page slices + baseApi reducer
router.tsx                -- createBrowserRouter, lazy-loads pages
App.tsx main.tsx
```

**Convention (enforced):** every page folder must contain `components/`, `hooks/`, `{page}Api.ts`, `{page}Slice.ts`, `{Page}Page.tsx`, and `index.ts` (barrel). Logic lives in hooks, JSX in components (component + hook pattern).

**Key packages:**
- `@azure/msal-react`, `@azure/msal-browser` — authority `https://login.microsoftonline.com/common`
- `@reduxjs/toolkit`, `react-redux`
- `react-router-dom` v6
- `@syncfusion/ej2-react-schedule` (meal plan calendar), `-grids`, `-inputs`, `-navigations`, `-dropdowns`
- The Syncfusion license is registered in `main.tsx` via `registerLicense(import.meta.env.VITE_SYNCFUSION_KEY)`

**Auth flow:**
- `MsalProvider` wraps `App`
- `ProtectedRoute` — redirects to login if the user isn't signed in
- `FamilyRequiredRoute` — redirects to `/join-family` if the user is signed in but has no family
- The RTK Query `baseApi` injects `Authorization: Bearer` headers from `msalInstance.acquireTokenSilent()`

## Azure Deployment

**Resources (single resource group `rg-family-menu`):**
- Azure SQL Database (existing, empty)
- Azure Storage Account + container `recipe-images` (private)
- Azure App Service (Linux, .NET 10) — backend
- Azure Static Web Apps — frontend (or a separate App Service)
- Azure App Registration — the Entra ID app (single app, multi-tenant + personal, redirect URIs for both localhost dev and prod)
- Application Insights

**Config (App Service application settings):**
- `ConnectionStrings__DefaultConnection` — Azure SQL (managed identity or SQL auth)
- `AzureAd__ClientId`, `AzureAd__Audience`
- `AzureBlob__ConnectionString` or managed identity
- `Cors__AllowedOrigins` — the frontend URL

**CI/CD (optional follow-up):** GitHub Actions — `dotnet publish` → deploy App Service, `npm run build` → Static Web Apps.

## Verification Plan

**Local dev:**
1. Backend: `dotnet run --project src/MenuNest.WebApi` → Swagger at `https://localhost:5001/swagger`
2. Run the EF migration: `dotnet ef database update --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi`
3. Frontend: `npm run dev` (Vite) → `http://localhost:5173`
4. Azurite (Blob emulator) for local image upload

**End-to-end smoke test:**
1. Sign in with a personal Microsoft account → user auto-provisioned → redirect to `/join-family`
2. Create a new family → see the invite code
3. Create an ingredient "ไข่ไก่" with unit "ฟอง"
4. Create a recipe "ไข่เจียว" + add the ingredient + upload a photo → the photo renders correctly
5. Add stock: 3 eggs
6. Create a meal plan entry for today's breakfast → pick "ไข่เจียว"
7. Open the stock check for that entry → the system reports stock is sufficient (recipe needs 2, stock is 3)
8. Reduce stock to 1 → stock check reports missing "ไข่ไก่ short by 1"
9. **Cook (partial):** click "🍳 Cook" → dialog shows the warning "ไข่ไก่ is short" → confirm → stock clamps to 0, MealPlanEntry.Status=Cooked, CookNotes records "ไข่ไก่ short by 1" + toast offers "📝 Add missing to Shopping list"
10. **Shopping list generate:** create a new list "This week's shopping" with auto-generate from a 7-day meal plan range → the system inserts items from the missing ingredients
11. **Mark bought:** click the checkbox next to the egg row (quantity 3) → stock goes to 3, the item moves to the Bought tab, a new StockTransaction row is written
12. **Uncook:** click Undo on the meal plan entry cooked in step 9 → stock is restored, Status=Planned
13. Sign out → sign in with another account → join the family using the invite code → see the same data

**Tests:**
- Unit tests: Application layer (command/query handlers) with xUnit + Moq
- Integration tests: Infrastructure layer with `Microsoft.EntityFrameworkCore.InMemory` or Testcontainers (SQL Server)
- Frontend: Vitest + React Testing Library — test hooks and the key components (RecipeForm, StockCheckPanel)

## UI Specification (for Google Stitch or a designer)

Per-page detail: purpose, layout, components, step-by-step user interactions, states, navigation. Written in enough detail for a designer (human or AI) to build real mockups without guessing.

**Design system (applies to every page):**
- Language: Thai (default) + English labels for technical terms
- Theme: light mode default, Syncfusion Material or Bootstrap 5 theme
- Primary color: warm orange/coral (evokes food and kitchen) — e.g., `#F57C00`
- Typography: Noto Sans Thai (body), Kanit (headings)
- Responsive: desktop-first (tablet supported; phone in phase 2)
- Top navigation bar on every page (except Login and Join Family): logo + 4 main tabs (Recipes, Stock, Meal Plan, Shopping) + an avatar menu (Ingredients, Family, Logout)

---

### 1. Login Page (`/login`)

**Purpose:** start the auth flow via Entra ID

**Entry:** unauthenticated users (default landing), or redirect from a protected route

**Layout:** center-aligned, full viewport height, hero background (a blurred kitchen/food photo or solid color)

**Components:**
- App logo + the name "MenuNest"
- Tagline (Thai UI string): "วางแผนมื้ออาหารกับครอบครัว"
- `Sign in with Microsoft` button (Microsoft's official button style — four-color icon + white text on black)
- Footer: "Supports work, school, and personal Microsoft accounts"

**Interactions:**
1. The user clicks the button → MSAL popup/redirect → Microsoft login screen
2. On successful auth → redirect to `/` (router logic decides)
   - If there's no user record in the DB → backend auto-creates one → frontend redirects to `/join-family`
   - If the user exists but `FamilyId == null` → redirect to `/join-family`
   - If the user has a family → redirect to `/` (Dashboard)

**States:** idle → loading (disabled button + spinner) → error (red banner "Sign-in failed — please try again")

---

### 2. Join Family Page (`/join-family`)

**Purpose:** after sign-in, the user has no family yet — prompt them to join or create one

**Entry:** redirect from login when `user.familyId == null`

**Layout:** a centered card (max-width 480px) on a light gray background

**Components:**
- Greeting (Thai UI string): "ยินดีต้อนรับ, {displayName}"
- Copy (Thai UI string): "คุณยังไม่ได้เข้าร่วม family"
- Two paths separated by an "or" divider:
  - (Top) "Have an invite code?" — input field + "Join" button
  - (Bottom) "+ Create a new family" button (outlined secondary)

**Interactions:**
1. Path A — join:
   - User enters the invite code → clicks "Join"
   - POST `/api/families/join` → success → redirect to `/`
   - Error: invalid code → banner "Invite code is invalid or expired"
2. Path B — create:
   - User clicks "Create a new family" → modal: enter family name
   - POST `/api/families` → success → show the invite code immediately (with a copy button) → redirect to `/`

**States:** idle → submitting (disabled form) → success (redirect) / error (inline message)

---

### 3. Dashboard / Home (`/`)

**Purpose:** summarize today's and tomorrow's planned meals + quick stats

**Entry:** the default route after auth + family ready

**Layout:** 2-column (desktop) or stacked (tablet)
- Left: summary cards
- Right: upcoming meals

**Components:**
- Greeting header (Thai UI string): "สวัสดี, ครอบครัว{familyName}" + current date (Thai)
- Stats row (3 cards): recipe count · ingredient count · meals planned this week
- "Today (14 April)" card:
  - 3 rows: Breakfast / Lunch / Dinner
  - Each row: recipe name + stock status badge (✅ sufficient / ⚠️ short X / ❌ not chosen) + click opens detail
- "Tomorrow" card: same layout
- "View full plan" button → `/meal-plan`

**Interactions:**
1. Click a recipe name in a card → modal with ingredient list + stock check
2. Click an empty slot → navigate to `/meal-plan` with that slot focused

**States:** loading skeleton → loaded / empty state ("No plan for today — [Add one]")

---

### 4. Recipes List (`/recipes`)

**Purpose:** browse and search the family's recipes

**Entry:** top nav tab

**Layout:**
- Top: search bar + filter dropdown + "+ New recipe" primary button
- Grid: recipe cards (3-4 columns desktop, 2 columns tablet)

**Components (per card):**
- Food photo (16:9, placeholder if none)
- Recipe name (bold, truncated to 2 lines)
- Ingredient count badge (e.g., "5 ingredients")
- Quick action: stock check icon (hover → tooltip "Ready to cook ✅" / "Short 2 items ⚠️")

**Interactions:**
1. Type in search → live filter (debounced 300ms, matches recipe name)
2. Click a card → navigate to `/recipes/{id}`
3. Click "+ New recipe" → navigate to `/recipes/new` (same page component, edit mode, empty form)

**States:**
- Empty (no recipes): illustration + "No recipes yet — [Add your first]"
- Loading: skeleton cards
- Error: banner + retry

---

### 5. Recipe Detail / Edit (`/recipes/:id` and `/recipes/new`)

**Purpose:** view / edit / create a recipe

**Layout:** 2-column (desktop): left = photo + meta, right = ingredients

**Components:**
- Header: back arrow + recipe name (display mode) or input (edit mode) + buttons ✏️ Edit / 🗑️ Delete / 💾 Save
- Photo:
  - View mode: full image
  - Edit mode: drag-drop zone + "Upload photo" button (MIME: jpg/png/webp, max 5 MB, instant preview)
- Description: textarea (edit) or paragraph (view)
- Ingredient table (Syncfusion Grid or custom):
  - Columns: name (autocomplete from master) · quantity · unit (auto-filled from the ingredient)
  - Edit mode: add row, delete row, inline edit
  - View mode: read-only list
- Stock check panel (view mode only): shows whether current stock is enough for this recipe

**Interactions (edit mode):**
1. Type in the "Ingredient name" field → autocomplete dropdown shows ingredients from the master
   - If the typed name is new (not in the master) → last option is "+ Add '{typed name}' as new"
   - Click → popup mini-form: enter the unit → save → returns to the recipe form with the new ingredient already in the master
2. Enter a quantity → validation: must be a positive number
3. Click Save → POST/PUT → success toast → back to view mode
4. Upload a photo → progress bar → preview + path saved in state

**States:** view / edit / saving / error. Unsaved-changes warning when navigating away

---

### 6. Ingredients Master (`/ingredients`)

**Purpose:** manage the family's ingredient master list

**Entry:** user menu (avatar dropdown) → "Manage ingredients"

**Layout:** full-width Syncfusion Grid + an add form on top

**Components:**
- Intro text: "This master list powers autocomplete when you build recipes or stock entries — one ingredient = one unit"
- Quick-add row: name field + unit field + "+" button
- Grid columns: name · unit · recipe count · stock count · Edit/Delete

**Interactions:**
1. Add: type name + unit → click "+" → POST → row inserted optimistically
2. Edit (inline): click a cell → edit → blur = auto save
3. Delete: click 🗑️ → confirm dialog
   - If the ingredient is referenced by recipes/stock → error dialog: "This ingredient is used by 3 recipes and 1 stock entry — remove those references first"

**States:** empty state ("No ingredients yet — start adding") / loading / error

---

### 7. Stock (`/stock`)

**Purpose:** manage the family's ingredient inventory (manual)

**Entry:** top nav tab

**Layout:** table view + quick actions

**Components:**
- Top: search + sort dropdown (name / quantity / last updated) + "+ Add ingredient"
- Table rows:
  - Ingredient name
  - Quantity on hand (bold, number + unit) — quick −/+ buttons
  - Last updated (e.g., "Edited 2h ago by Somsri")
  - Delete icon
- Filter toggle: "Show only low stock (≤ 1)"

**Interactions:**
1. Add stock: "+ Add ingredient" → autocomplete modal (pick from master, or create inline if missing) + quantity → POST
2. Quick −/+: click to adjust by one unit → PATCH immediately (optimistic + toast "Updated")
3. Click the number directly → inline edit → Enter = save
4. Delete: confirm dialog → row removed

**States:**
- Row highlighted in light red when quantity = 0
- Empty: "No stock yet — [Add your first ingredient]"

---

### 8. Meal Plan (`/meal-plan`)

**Purpose:** plan meals by day × slot with stock check integration

**Entry:** top nav tab (default view = current week)

**Layout:** grid/calendar
- Desktop: 7 days × 3 slots (rows = days, columns = slots)
- Tablet: horizontal scroll or per-day accordion

**Components:**
- Header: date range picker + "◀ Today ▶" navigation buttons
- Legend: ✅ sufficient / ⚠️ short some / ❌ not chosen
- Each slot (cell):
  - Empty: light gray background + "+" icon
  - Planned: recipe name (truncated) + photo thumbnail + stock status icon (✅/⚠️)
  - Cooked: recipe name + checkmark + timestamp ("Cooked 18:30") — muted color
  - Click = expand or open the detail sidebar

**Interactions:**
1. Click an empty slot → modal/sidebar "Pick a recipe":
   - Search + list recipes (card layout)
   - Preview: ingredients + stock check preview (green = sufficient, red = short)
   - "Pick" button → POST → slot filled immediately
2. Click a filled (planned) slot → sidebar:
   - Recipe info + ingredient list
   - Stock check: table "Required / On hand / Short" + buttons "→ Add stock" or "Add to shopping list"
   - **"🍳 Cook now" button (primary)** — starts the Cook flow (see step 3)
   - Buttons "Change recipe" or "Remove"
3. **Cook flow** (pressing "Cook now"):
   - Opens `CookConfirmDialog`:
     - Shows the recipe + the ingredients that will be deducted (table: name / to deduct / remaining)
     - If stock is sufficient → title "Confirm cooking" + rows in green
     - If stock is short → title "⚠️ Not enough ingredients" + banner: "We'll deduct what's available (won't go negative) — you should note actual usage" + rows in orange
   - "✓ Confirm" button → POST `/api/meal-plan/{id}/cook`
     - Backend: TX — deduct stock (clamp at 0), insert StockTransaction rows, set MealPlanEntry.Status=Cooked, CookedAt, CookedByUserId, CookNotes (if any were short)
     - Response: `{ deducted: [...], partial: [...] }` → success toast + if `partial` is non-empty, include a "📝 Add missing to shopping list" button
   - "Cancel" button → close dialog
4. Click a cooked slot → sidebar:
   - Shows the recipe + ingredients that were deducted + CookNotes
   - "↩️ Undo (uncook)" button → confirm → POST `/api/meal-plan/{id}/uncook` → stock restored from StockTransaction rows
5. Drag-drop between slots (phase 2) — move/swap

**States:** loading (skeleton grid) / normal / error banner

**Stock check panel detail (in sidebar):**
```
┌──────────────────────────────────┐
│ 🥚 ไข่เจียว                       │
│ ─────────────────────────────────│
│ Required      On hand      Status │
│ ไข่ไก่ 2 ฟอง  3 ฟอง         ✅    │
│ น้ำมัน 1 ชต.  0 ชต.         ❌    │
│ เกลือ 1 หยิบ  200 ก.        ✅    │
│ ─────────────────────────────────│
│ Summary: 1 item short             │
│ [→ Add stock] [Change recipe]     │
└──────────────────────────────────┘
```

---

### 9. Family (`/family`)

**Purpose:** manage members and the invite code

**Entry:** user menu → "Manage family"

**Layout:** stacked sections

**Components:**
- Section 1 — Family info:
  - Family name (editable by admin/creator)
  - Member count
- Section 2 — Invite code:
  - Shows the current code (e.g., `ABCD-1234`) + "📋 Copy" and "🔄 Rotate (generate new)" buttons
  - Copy: "Send this code to new members — one code works for many joins"
- Section 3 — Members list:
  - Each row: avatar + name + email + relationship label (e.g., "Somsri's wife") + ✏️ edit relationships
- Section 4 — Relationships editor (modal/dialog):
  - Dropdown "Member A" + "is…" (Parent/Child/Spouse/Sibling/Other) + "of Member B"
  - List existing relationships → can delete
- Section 5 — Leave family (danger zone):
  - "Leave this family" button (outlined red) → confirm dialog

**Interactions:**
1. Rotate code: click 🔄 → confirm "The old code will stop working — continue?" → POST → shows the new code
2. Edit relationship: click ✏️ → modal editor → save → update list
3. Leave family: confirm → POST `/api/families/leave` → `user.familyId = null` → redirect to `/join-family`

**States:** loading / normal. No role enforcement in MVP — every family member has equal edit access (since relationships are metadata only)

---

### 10. Shopping Lists (`/shopping` + `/shopping/:id`)

**Purpose:** manage shopping lists — create manually or auto-generate from a meal plan; marking an item as bought auto-adds it to stock

**Entry:** top nav tab "Shopping" or from the Meal Plan sidebar ("Add to shopping list")

**Layout:** two screens

#### 10a. Shopping Lists — index (`/shopping`)

**Components:**
- Top: "+ New list" button + filter dropdown (Active / Completed / All)
- Grid/list of cards:
  - Each card: list name, item progress (e.g., "8 / 12 bought"), progress bar, status (🟢 active / ⚪ archived), created date
  - Click → `/shopping/{id}`
- Empty state: illustration + "No shopping lists yet — [Create your first]"

**Interactions:**
1. "+ New list" → modal:
   - List name input (default "This week's groceries")
   - Checkbox "📅 Auto-generate from meal plan" → toggles a date range picker (from / to)
   - "Create" button
   - POST `/api/shopping-lists` (if a date range is set → backend computes missing items and inserts them)
   - Redirect → `/shopping/{id}`

#### 10b. Shopping List Detail (`/shopping/:id`)

**Components:**
- Header: list name (editable) + "🔄 Regenerate" button (if the list was generated from a meal plan) + ⚙️ menu (Archive, Delete)
- Progress bar: "5 / 12 bought"
- Tabs/sections:
  - **Unbought** (default, shown first): items not yet bought
  - **Bought**: items already bought (sorted by BoughtAt desc)
- Each row (item):
  - Checkbox (Unbought) / checkmark (Bought)
  - Ingredient name
  - Quantity + unit (e.g., "ไข่ไก่ 6 ฟอง")
  - (Optional tag) "From: ไข่เจียว (14 Apr)"
  - Edit/Delete icons
- "+ Add item" button at the bottom (ingredient autocomplete + quantity)

**Interactions:**
1. Check the box → mark bought:
   - PATCH `/api/shopping-lists/{id}/items/{itemId}` with `{ isBought: true }`
   - Backend: update the item + insert StockTransaction (Delta=+quantity, Source=ShoppingListBought) + update StockItem.Quantity
   - UI: row animates into the "Bought" tab + toast "Added to stock: ไข่ไก่ 6 ฟอง"
2. Uncheck (in Bought tab): confirm → reverse the transaction → row returns to Unbought
3. "🔄 Regenerate" → confirm "Items will be recomputed from the meal plan — items already bought will be preserved. Continue?"
   - POST `/api/shopping-lists/{id}/regenerate` → update items (preserve IsBought=true rows)
4. "+ Add item" → modal: ingredient autocomplete (pick from master or create new) + quantity → POST
5. Edit quantity inline → blur = save (optimistic)
6. "Complete list" button (top right) → Status=Completed → redirect to `/shopping`

**States:**
- Loading skeleton / empty ("Empty list — [Add your first item]" or "Generate from meal plan")
- Row highlighted in light green after Mark bought (2s animation)
- Error (stock update failed): error toast + revert checkbox

---

### Cross-page patterns

- **Toast notifications** (top-right): success (green) / error (red) / info — auto-dismiss after 4 seconds
- **Confirmation dialogs**: for destructive actions (delete, leave family, rotate code)
- **Loading states**: skeleton screens (no full-page spinners)
- **Error boundary**: generic fallback "Something went wrong — [Try again]"
- **Empty states**: illustration + call-to-action button
- **Unsaved-changes guard**: when navigating away from an edit form with pending changes

## Critical Files to Create (first implementation milestone)

**Backend:**
- `backend/MenuNest.sln`
- `src/MenuNest.Domain/Entities/*.cs`
- `src/MenuNest.Infrastructure/Persistence/AppDbContext.cs` + Migrations/Initial
- `src/MenuNest.Application/DependencyInjection.cs`
- `src/MenuNest.WebApi/Program.cs`

**Frontend:**
- `frontend/package.json`, `vite.config.ts`, `tsconfig.json`
- `src/main.tsx`, `src/App.tsx`, `src/router.tsx`
- `src/shared/auth/msalConfig.ts`
- `src/store/index.ts`
- `src/shared/api/baseApi.ts`

**Infra:**
- `infra/main.bicep` (optional — provisions SQL / Storage / App Service) or manual portal setup
