# MenuNest — Implementation Plan

**App name:** MenuNest (family meal planning)

## Context

Greenfield app สำหรับวางแผนมื้ออาหารของครอบครัว — user สร้าง recipe library, ใส่ stock วัตถุดิบที่มีอยู่, planแต่ละวัน/มื้อ แล้วระบบเช็คว่า stock พอสำหรับเมนูที่เลือกหรือไม่. ใช้ Microsoft Entra ID login, group user เป็น family (1 user ต่อ 1 family). Deploy บน Azure.

**Stack (confirmed):**
- Frontend: React + TypeScript + Vite, Redux Toolkit (RTK + RTK Query), React Router, Syncfusion Community License
- Backend: ASP.NET 10 (LTS) Clean Architecture
- Database: Azure SQL Database (empty — fresh schema via EF Core migrations)
- Storage: Azure Blob Storage (รูปอาหาร)
- Auth: Microsoft Entra ID — multi-tenant + personal accounts (`common` authority)
- Hosting: Azure App Service (backend), Azure Static Web Apps หรือ App Service (frontend)

## Scope (MVP)

**In scope:**
1. Entra ID login (multi-tenant + personal)
2. Family management — สร้าง family, invite code, join, เลิก, list สมาชิก, ตั้ง relationships (metadata)
3. Ingredient master per-family — autocomplete + on-the-fly creation, 1 ingredient = 1 unit ตายตัว
4. Recipe CRUD + ingredients + upload รูป (Blob Storage)
5. Stock — manual CRUD per ingredient (family-scoped)
6. Meal plan — วัน × มื้อ (breakfast/lunch/dinner) → เลือก recipe
7. Stock check — เช็คเมนูที่วางไว้ว่าวัตถุดิบพอ/ขาดอะไรบ้าง
8. **Cook action** — กด "ลงมือทำ" บน meal plan entry → หัก stock อัตโนมัติ (warn + หักเท่าที่มี ถ้าไม่พอ)
9. **Shopping list** — สร้าง/แก้ไข persistent shopping list ของ family, generate จาก meal plan range, mark bought → auto-increment stock

**Out of scope (MVP):** multi-unit conversion, permission roles, mobile app, notifications, multi-language (default ไทย), shopping list collaboration (real-time sync), meal plan drag-drop

## Data Model

Tables (EF Core entities). ทุก entity ที่เป็นของ family มี `FamilyId` foreign key และ query ทั้งหมด filter ด้วย `FamilyId` จาก claims.

```
Family
  Id (Guid, PK), Name, InviteCode (unique, regenerable), CreatedAt, CreatedByUserId

User
  Id (Guid, PK), ExternalId (Entra oid claim, unique), Email, DisplayName,
  FamilyId (FK, nullable — null = ยังไม่ join family), JoinedAt

UserRelationship                       -- metadata only (แสดงผล)
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
  CookNotes (string, nullable)          -- e.g., "stock ไม่พอสำหรับ ไข่ไก่ — ใส่ 1 แทน 2 ฟอง"
  UNIQUE (FamilyId, Date, MealSlot)     -- 1 recipe per slot per day

StockTransaction                        -- audit log ของทุกการเปลี่ยนแปลง stock
  Id, FamilyId, IngredientId (FK), Delta (decimal, +/-),
  Source (enum: Manual/Cook/ShoppingListBought/Correction),
  SourceRefId (Guid, nullable),         -- MealPlanEntryId หรือ ShoppingListItemId
  CreatedAt, CreatedByUserId, Notes
  -- Stock.Quantity เป็นผลสะสมของ transactions (update ใน handler + เก็บ audit ที่นี่)

ShoppingList
  Id, FamilyId, Name, Status (enum: Active/Completed/Archived),
  CreatedAt, CreatedByUserId, CompletedAt (nullable)

ShoppingListItem
  Id, ShoppingListId (FK), IngredientId (FK), Quantity (decimal),
  IsBought (bool, default false), BoughtAt (DateTime, nullable), BoughtByUserId (FK, nullable),
  SourceMealPlanEntryIds (json, nullable)    -- track ว่า item นี้ generate มาจาก meal plan ไหน
  UNIQUE (ShoppingListId, IngredientId)      -- aggregate 1 row per ingredient per list
```

## Backend — ASP.NET Clean Architecture

**Solution layout** (`backend/`):

```
src/
  MenuNest.Domain/           -- entities, enums, domain exceptions (no deps)
  MenuNest.Application/      -- Use-case handlers (explicit, no mediator), DTOs, interfaces, validators (FluentValidation)
  MenuNest.Infrastructure/   -- EF Core DbContext, repositories, Blob client, CurrentUserService
  MenuNest.WebApi/           -- Controllers, middleware, DI wiring, Program.cs
tests/
  MenuNest.Application.UnitTests/
  MenuNest.Infrastructure.IntegrationTests/
```

**Key packages:**
- `Microsoft.Identity.Web` — JWT bearer validation (authority = `https://login.microsoftonline.com/common/v2.0`)
- `Microsoft.EntityFrameworkCore.SqlServer` + `EFCore.Tools`
- `Mediator` (martinothamar/Mediator — free MIT, source-generator based)
- `FluentValidation` (request validation)
- `Mapster` (DTO mapping — free + fast, source-gen)
- **CQRS + Mediator pattern** — commands/queries เป็น request class, handler แยกต่อ 1 use case:
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
      ValidationBehavior.cs            -- IPipelineBehavior → validate ก่อนเข้า handler
      LoggingBehavior.cs
      TransactionBehavior.cs           -- commit TX รอบ command
  ```
  Controller inject `IMediator` ตัวเดียว → `mediator.Send(command/query)`
- `Azure.Storage.Blobs`

**Cross-cutting:**
- `ICurrentUserService` — resolve `oid` claim → `User` record, cache per-request. Auto-provision user row on first login.
- `IFamilyContext` — all repositories query scoped to `CurrentUser.FamilyId`. Requests without family (new user) are rejected by filter attribute except for family-join endpoints.
- Global exception middleware → ProblemDetails response
- Serilog → Azure Application Insights

**API endpoints (grouped):**

| Route | Purpose |
|---|---|
| `GET  /api/me` | current user + family info |
| `POST /api/families` | create new family (current user becomes creator) |
| `POST /api/families/join` | body: `{ inviteCode }` |
| `POST /api/families/leave` | leave current family |
| `GET  /api/families/me/members` | list members + relationships |
| `POST /api/families/me/invite-codes/rotate` | regenerate code |
| `POST /api/families/me/relationships` | add relationship metadata |
| `GET/POST/PUT/DELETE /api/ingredients` | family-scoped ingredient master |
| `GET/POST/PUT/DELETE /api/recipes` | recipes + nested `RecipeIngredient` |
| `POST /api/recipes/{id}/image` | multipart upload → Blob → save path |
| `GET/POST/PUT/DELETE /api/stock` | stock items |
| `GET/POST/PUT/DELETE /api/meal-plan?from=&to=` | meal plan entries by date range |
| `GET  /api/meal-plan/{id}/stock-check` | returns `{ required[], available[], missing[] }` |
| `POST /api/meal-plan/{id}/cook` | หัก stock (เท่าที่มี) + mark Status=Cooked + บันทึก StockTransaction. Response: `{ deducted[], partial[], missingItems[] }` |
| `POST /api/meal-plan/{id}/uncook` | undo cook → บวก stock กลับจาก StockTransaction + reset Status=Planned |
| `GET  /api/shopping-lists` | list ทั้งหมดของ family (filter by status) |
| `POST /api/shopping-lists` | body: `{ name, fromMealPlan?: { fromDate, toDate } }`. ถ้ามี fromMealPlan → auto-populate items จาก missing ingredients |
| `GET /api/shopping-lists/{id}` | detail + items |
| `PUT /api/shopping-lists/{id}` | update name/status |
| `DELETE /api/shopping-lists/{id}` | |
| `POST /api/shopping-lists/{id}/items` | add item manually |
| `PATCH /api/shopping-lists/{id}/items/{itemId}` | mark bought → POST StockTransaction (+quantity, Source=ShoppingListBought) |
| `POST /api/shopping-lists/{id}/regenerate` | re-compute items จาก meal plan range (แต่เก็บ IsBought สำหรับ item ที่ซื้อไปแล้ว) |

**Authentication:**
- `AddMicrosoftIdentityWebApi()` with `TenantId = "common"` และ `AllowWebApiToBeAuthorizedByACL = false`
- `ValidateIssuer = false` custom validator — accept `login.microsoftonline.com/{anyTenantId}/v2.0` + `sts.windows.net/9188040d-...` (personal MSA tenant)
- ทุก controller ใส่ `[Authorize]` ยกเว้น health check

**Image upload flow (MVP — proxy through API):**
1. Frontend `POST /api/recipes/{id}/image` multipart
2. Backend validate (MIME jpeg/png/webp, size ≤ 5 MB)
3. Upload ไป Blob container `recipe-images` path `{familyId}/{recipeId}/{guid}.{ext}`
4. บันทึก path ใน `Recipe.ImageBlobPath`
5. Frontend อ่านผ่าน `GET /api/recipes/{id}/image` (backend stream จาก Blob พร้อม cache headers) — ไม่ expose Blob URL ตรง ๆ

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
    ... (CRUD page สำหรับ master list)
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
  index.ts                -- configureStore, combine page slices + baseApi reducer
router.tsx                -- createBrowserRouter, lazy-load pages
App.tsx main.tsx
```

**Convention enforced:** ทุก page folder ต้องมี `components/`, `hooks/`, `{page}Api.ts`, `{page}Slice.ts`, `{Page}Page.tsx`, `index.ts` (barrel). Logic ใน hooks, JSX ใน components (component + hook pattern).

**Key packages:**
- `@azure/msal-react`, `@azure/msal-browser` — authority `https://login.microsoftonline.com/common`
- `@reduxjs/toolkit`, `react-redux`
- `react-router-dom` v6
- `@syncfusion/ej2-react-schedule` (meal plan calendar), `-grids`, `-inputs`, `-navigations`, `-dropdowns`
- Syncfusion license registered ใน `main.tsx` ด้วย `registerLicense(import.meta.env.VITE_SYNCFUSION_KEY)`

**Auth flow:**
- `MsalProvider` ครอบ `App`
- `ProtectedRoute` — redirect → login ถ้าไม่ได้ login
- `FamilyRequiredRoute` — redirect → `/join-family` ถ้า login แล้วแต่ไม่มี family
- RTK Query `baseApi` ใส่ `Authorization: Bearer` header จาก `msalInstance.acquireTokenSilent()`

## Azure Deployment

**Resources (single resource group `rg-family-menu`):**
- Azure SQL Database (existing — empty)
- Azure Storage Account + container `recipe-images` (private)
- Azure App Service (Linux, .NET 10) — backend
- Azure Static Web Apps — frontend (หรือ App Service แยก)
- Azure App Registration — Entra ID app (single app, multi-tenant + personal, redirect URIs ทั้ง localhost dev และ prod)
- Application Insights

**Config (App Service application settings):**
- `ConnectionStrings__DefaultConnection` — Azure SQL (managed identity หรือ SQL auth)
- `AzureAd__ClientId`, `AzureAd__Audience`
- `AzureBlob__ConnectionString` หรือใช้ Managed Identity
- `Cors__AllowedOrigins` — frontend URL

**CI/CD (optional follow-up):** GitHub Actions — `dotnet publish` → deploy App Service, `npm run build` → Static Web Apps.

## Verification Plan

**Local dev:**
1. Backend: `dotnet run --project src/MenuNest.WebApi` → Swagger ที่ `https://localhost:5001/swagger`
2. Run EF migration: `dotnet ef database update --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi`
3. Frontend: `npm run dev` (Vite) → `http://localhost:5173`
4. Azurite (Blob emulator) สำหรับ local image upload

**End-to-end smoke test:**
1. Login with personal Microsoft account → auto-provision user → redirect to `/join-family`
2. สร้าง family ใหม่ → เห็น invite code
3. สร้าง ingredient "ไข่ไก่" unit "ฟอง"
4. สร้าง recipe "ไข่เจียว" + ใส่ ingredient + upload รูป → รูปแสดงผลถูกต้อง
5. เพิ่ม stock: ไข่ไก่ 3 ฟอง
6. สร้าง meal plan วันนี้ มื้อเช้า → เลือก "ไข่เจียว"
7. เปิด stock-check ของ entry นั้น → ระบบแสดงว่า stock พอ (recipe ใช้ 2 ฟอง, stock 3 ฟอง)
8. ลด stock เป็น 1 ฟอง → stock-check แจ้ง missing "ไข่ไก่ ขาด 1 ฟอง"
9. **Cook (partial):** กด "🍳 ลงมือทำ" → dialog แสดง warning "ไข่ไก่ ไม่พอ" → ยืนยัน → stock → 0, MealPlanEntry.Status=Cooked, CookNotes มี "ไข่ไก่ ขาด 1" + toast "📝 เพิ่มใน Shopping list"
10. **Shopping list generate:** สร้าง list ใหม่ "ของอาทิตย์นี้" + auto-generate จาก meal plan 7 วัน → ระบบ insert items จาก missing ingredients
11. **Mark bought:** คลิก checkbox ข้างไข่ไก่ (ต้องซื้อ 3 ฟอง) → stock เพิ่มเป็น 3, item ย้ายไป tab Bought, StockTransaction row ใหม่
12. **Uncook:** กด Undo ที่ meal plan entry ที่ cook ไปข้อ 9 → stock คืน, Status=Planned
13. Logout → login อีกบัญชี → join family ด้วย invite code → เห็นข้อมูลเดียวกัน

**Tests:**
- Unit tests: Application layer (command/query handlers) ด้วย xUnit + NSubstitute
- Integration tests: Infrastructure layer ด้วย `Microsoft.EntityFrameworkCore.InMemory` หรือ Testcontainers (SQL Server)
- Frontend: Vitest + React Testing Library — test hooks และ component หลัก (RecipeForm, StockCheckPanel)

## UI Specification (สำหรับ Google Stitch หรือ designer)

รายละเอียดแต่ละหน้า: purpose, layout, components, user interactions step-by-step, states, navigation. ตั้งใจเขียนให้ละเอียดพอที่ designer (คนหรือ AI) จะสร้าง mockup จริงได้โดยไม่ต้องเดา.

**Design system (ทุกหน้ายึดตามนี้):**
- Language: ไทย (default) + English labels สำหรับ technical term
- Theme: light mode เป็น default, Syncfusion Material หรือ Bootstrap 5 theme
- Primary color: warm orange/coral (สื่อความ "อาหาร/ครัว") — เช่น `#F57C00`
- Typography: Noto Sans Thai (body), Kanit (heading)
- Responsive: desktop-first (tablet รองรับ, mobile phase 2)
- Top navigation bar ทุกหน้า (ยกเว้น Login, Join Family): logo + 4 tabs หลัก (Recipes, Stock, Meal Plan, Shopping) + avatar menu (Ingredients, Family, Logout)

---

### 1. Login Page (`/login`)

**Purpose:** เริ่มต้น auth flow ผ่าน Entra ID

**Entry:** unauthenticated users (default landing), หรือ redirect จาก protected route

**Layout:** center-aligned, full viewport height, hero background (รูปครัว/อาหาร blurred หรือ solid color)

**Components:**
- App logo + ชื่อ "MenuNest"
- Tagline: "วางแผนมื้ออาหารกับครอบครัว"
- ปุ่ม `Sign in with Microsoft` (Microsoft official button style — icon 4 สี + ข้อความขาวบนพื้นดำ)
- Footer: "รองรับบัญชีทำงาน, โรงเรียน และ personal Microsoft account"

**Interactions:**
1. User คลิกปุ่ม → MSAL popup/redirect → Microsoft login screen
2. หลัง auth สำเร็จ → redirect `/` (router logic ตัดสินใจ)
   - ถ้ายังไม่มี user record ใน DB → backend auto-create → frontend redirect `/join-family`
   - ถ้ามี user แต่ยัง `FamilyId = null` → redirect `/join-family`
   - ถ้ามี family แล้ว → redirect `/` (Dashboard)

**States:** idle → loading (disabled button + spinner) → error (red banner "Sign-in failed — ลองอีกครั้ง")

---

### 2. Join Family Page (`/join-family`)

**Purpose:** หลัง login แต่ยังไม่มี family — ให้เลือก join หรือสร้างใหม่

**Entry:** redirect จาก login ถ้า `user.familyId == null`

**Layout:** center card (max-width 480px) บนพื้นเทาอ่อน

**Components:**
- Greeting "ยินดีต้อนรับ, {displayName}"
- ข้อความ "คุณยังไม่ได้เข้าร่วม family"
- 2 ทางเลือก คั่นด้วย divider "หรือ"
  - (บน) `มี invite code แล้ว?` — input field + ปุ่ม "เข้าร่วม"
  - (ล่าง) ปุ่ม `+ สร้าง Family ใหม่` (outlined secondary)

**Interactions:**
1. Path A — join:
   - User กรอก invite code → กด "เข้าร่วม"
   - POST `/api/families/join` → success → redirect `/`
   - Error: invalid code → banner "Invite code ไม่ถูกต้องหรือหมดอายุ"
2. Path B — create:
   - User กด "สร้าง Family ใหม่" → modal: กรอกชื่อ family
   - POST `/api/families` → success → แสดง invite code ทันที (copy button) → redirect `/`

**States:** idle → submitting (disabled form) → success (redirect) / error (inline message)

---

### 3. Dashboard / Home (`/`)

**Purpose:** สรุปเมนูที่วางไว้สำหรับวันนี้/พรุ่งนี้ + quick stats

**Entry:** default route หลัง auth + family ready

**Layout:** 2-column (desktop) หรือ stacked (tablet)
- ซ้าย: summary cards
- ขวา: upcoming meals

**Components:**
- Greeting header: "สวัสดี, ครอบครัว{familyName}" + current date (ไทย)
- Stats row (3 cards): recipe count · ingredient count · planned meals this week
- "วันนี้ (14 เม.ย.)" card:
  - 3 rows: Breakfast / Lunch / Dinner
  - แต่ละ row: recipe name + stock status badge (✅ พอ / ⚠️ ขาด X / ❌ ยังไม่ได้เลือก) + คลิกเปิด detail
- "พรุ่งนี้" card: รูปแบบเดียวกัน
- ปุ่ม "ดูแผนทั้งหมด" → `/meal-plan`

**Interactions:**
1. คลิก recipe name ใน card → เปิด modal แสดง ingredient list + stock check
2. คลิกช่องว่าง (empty slot) → navigate ไป `/meal-plan` โฟกัส slot นั้น

**States:** loading skeleton → loaded / empty state ("ยังไม่มีแผนวันนี้ — [เพิ่มเลย]")

---

### 4. Recipes List (`/recipes`)

**Purpose:** browse/ค้นหา recipe ของ family

**Entry:** top nav tab

**Layout:**
- Top: search bar + filter dropdown + "+ เพิ่มสูตรใหม่" button (primary)
- Grid: recipe cards (3-4 column desktop, 2 column tablet)

**Components (per card):**
- รูปอาหาร (16:9 aspect, placeholder ถ้าไม่มีรูป)
- ชื่อสูตร (bold, truncate 2 บรรทัด)
- Ingredient count badge (เช่น "5 วัตถุดิบ")
- Quick action: stock check icon (hover → tooltip "พร้อมทำเลย ✅" / "ขาด 2 อย่าง ⚠️")

**Interactions:**
1. พิมพ์ search → live filter (debounced 300ms, match ชื่อ recipe)
2. คลิก card → navigate `/recipes/{id}`
3. คลิก "+ เพิ่มสูตรใหม่" → navigate `/recipes/new` (same page component, edit mode, empty form)

**States:**
- Empty (no recipes): illustration + "ยังไม่มีสูตรในครอบครัว — [เพิ่มสูตรแรก]"
- Loading: skeleton cards
- Error: banner + retry

---

### 5. Recipe Detail / Edit (`/recipes/:id` และ `/recipes/new`)

**Purpose:** ดู/แก้ไข/สร้าง recipe

**Layout:** 2-column (desktop): ซ้ายรูป + meta, ขวา ingredients

**Components:**
- Header: back arrow + ชื่อ recipe (display) หรือ input (edit) + ปุ่ม ✏️ Edit / 🗑️ Delete / 💾 Save
- รูปอาหาร:
  - View mode: แสดงรูปเต็ม
  - Edit mode: drag-drop zone + ปุ่ม "Upload รูป" (MIME: jpg/png/webp, max 5 MB, preview ทันที)
- Description: textarea (edit) หรือ paragraph (view)
- Ingredient table (Syncfusion Grid หรือ custom):
  - Column: ชื่อ (autocomplete จาก master) · ปริมาณ · หน่วย (auto-fill จาก ingredient)
  - Edit mode: add row, delete row, inline edit
  - View mode: read-only list
- Stock check panel (view mode only): แสดงว่าตอนนี้ stock พอสำหรับ recipe นี้หรือไม่

**Interactions (edit mode):**
1. พิมพ์ในช่อง "ชื่อวัตถุดิบ" → autocomplete dropdown แสดง ingredient จาก master
   - ถ้าพิมพ์ชื่อใหม่ (ไม่มีใน master) → option ล่างสุด "+ เพิ่ม '{ชื่อที่พิมพ์}' ใหม่"
   - คลิก → popup mini-form: กรอก "หน่วย" → บันทึก → กลับมาที่ recipe form พร้อม ingredient ใหม่ใน master
2. กรอก quantity → validation: ต้องเป็นเลขบวก
3. กด "Save" → POST/PUT → success toast → กลับไป view mode
4. อัปโหลดรูป → progress bar → preview + บันทึก path ลง state

**States:** view / edit / saving / error. Unsaved-changes warning ตอน navigate away

---

### 6. Ingredients Master (`/ingredients`)

**Purpose:** จัดการ master list ของวัตถุดิบ (family-scoped)

**Entry:** user menu (avatar dropdown) → "จัดการวัตถุดิบ"

**Layout:** full-width Syncfusion Grid + add form ด้านบน

**Components:**
- Intro text: "Master list นี้ใช้เป็น autocomplete เวลาสร้าง recipe และเพิ่ม stock — 1 ingredient = 1 หน่วย"
- Quick add row: ช่องชื่อ + ช่องหน่วย + ปุ่ม "+"
- Grid columns: ชื่อ · หน่วย · จำนวน recipe ที่ใช้ · จำนวน stock · Edit/Delete

**Interactions:**
1. Add: พิมพ์ชื่อ + หน่วย → ปุ่ม "+" → POST → row เพิ่มใน grid (optimistic)
2. Edit (inline): คลิกเซลล์ → แก้ไข → blur = auto save
3. Delete: คลิกไอคอน 🗑️ → confirm dialog
   - ถ้า ingredient มี reference ใน recipe/stock → error dialog: "ingredient นี้ใช้อยู่ใน 3 recipe และ 1 stock — ต้องลบ reference ก่อน"

**States:** empty state ("ยังไม่มี ingredient — เริ่มเพิ่มเลย") / loading / error

---

### 7. Stock (`/stock`)

**Purpose:** จัดการ inventory วัตถุดิบของ family (manual)

**Entry:** top nav tab

**Layout:** table view + quick actions

**Components:**
- Top: search + sort dropdown (ชื่อ / จำนวน / อัปเดตล่าสุด) + "+ เพิ่ม ingredient"
- Table rows:
  - ชื่อ ingredient
  - จำนวนคงเหลือ (bold, เลข + หน่วย) — quick -/+ buttons
  - last updated (เช่น "แก้ 2 ชม. ก่อน โดยสมศรี")
  - Delete icon
- Filter toggle: "แสดงเฉพาะที่ใกล้หมด (≤ 1)"

**Interactions:**
1. เพิ่ม stock: "+ เพิ่ม ingredient" → autocomplete modal (เลือกจาก master, ถ้าไม่มีสร้างใหม่ inline) + จำนวน → POST
2. Quick -/+: คลิกปุ่มลดหรือเพิ่มทีละ 1 หน่วย → PATCH ทันที (optimistic + toast "อัปเดตแล้ว")
3. คลิกตัวเลขโดยตรง → inline edit → Enter = save
4. Delete: confirm dialog → ลบ row ออก

**States:**
- Row เน้นสีแดงอ่อน ถ้า quantity = 0
- Empty: "ยังไม่มี stock — [เพิ่ม ingredient แรก]"

---

### 8. Meal Plan (`/meal-plan`)

**Purpose:** วางแผนเมนูตาม วัน × มื้อ พร้อม stock check

**Entry:** top nav tab (default view = สัปดาห์ปัจจุบัน)

**Layout:** grid/calendar
- Desktop: 7 วัน × 3 มื้อ (แถว = วัน, คอลัมน์ = มื้อ)
- Tablet: scroll horizontal หรือ accordion per day

**Components:**
- Header: date range picker + ปุ่ม "◀ วันนี้ ▶" navigation
- Legend: ✅ พอ / ⚠️ ขาดบาง / ❌ ยังไม่เลือก
- Each slot (cell):
  - Empty: พื้นเทาอ่อน + "+" icon
  - Planned: recipe name (truncate) + รูป thumbnail + stock status icon (✅/⚠️)
  - Cooked: recipe name + checkmark + timestamp ("ทำแล้ว 18:30") — สีเทาจางลง
  - คลิก = expand หรือเปิด sidebar detail

**Interactions:**
1. คลิก empty slot → modal/sidebar "เลือก recipe":
   - Search + list recipes (card layout)
   - Preview: ingredients + stock check preview (สีเขียว = มีพอ, แดง = ขาด)
   - ปุ่ม "เลือก" → POST → slot filled ทันที
2. คลิก filled (planned) slot → sidebar:
   - Recipe info + ingredient list
   - Stock check: table "ต้องการ / มี / ขาด" + ปุ่ม "→ เพิ่ม stock" หรือ "เพิ่มใน shopping list"
   - **ปุ่ม "🍳 ลงมือทำ" (primary)** — เริ่ม Cook flow (ดูข้อ 3)
   - ปุ่ม "เปลี่ยน recipe" หรือ "ลบ"
3. **Cook flow** (กด "ลงมือทำ"):
   - เปิด `CookConfirmDialog`:
     - แสดง recipe + ingredient ที่จะหัก (table: ชื่อ / จะหัก / เหลือ)
     - ถ้า stock พอทุกอย่าง → title "ยืนยันการทำอาหาร" + rows สีเขียว
     - ถ้า stock ไม่พอบางอย่าง → title "⚠️ วัตถุดิบไม่พอ" banner: "ระบบจะหักเท่าที่มี (ไม่ติดลบ) — คุณควรจดว่าของจริงใช้ไปเท่าไหร่" + rows สีส้ม
   - ปุ่ม "✓ ยืนยันทำ" → POST `/api/meal-plan/{id}/cook`
     - Backend: TX — หัก stock (clamp ที่ 0), insert StockTransaction rows, set MealPlanEntry.Status=Cooked, CookedAt, CookedByUserId, CookNotes (ถ้ามีของขาด)
     - Response: `{ deducted: [...], partial: [...] }` → toast success + ถ้า partial มีปุ่ม "📝 เพิ่มของที่ขาดใน Shopping list"
   - ปุ่ม "ยกเลิก" → close dialog
4. คลิก cooked slot → sidebar:
   - แสดง recipe + ingredient หักแล้ว + CookNotes
   - ปุ่ม "↩️ Undo (ยกเลิกการทำ)" → confirm → POST `/api/meal-plan/{id}/uncook` → คืน stock ตาม StockTransaction
5. Drag-drop ระหว่าง slot (optional phase 2) — ย้าย/swap

**States:** loading (skeleton grid) / normal / error banner

**Stock check panel detail (in sidebar):**
```
┌──────────────────────────────────┐
│ 🥚 ไข่เจียว                       │
│ ─────────────────────────────────│
│ ต้องการ      มีใน stock    สถานะ │
│ ไข่ไก่ 2ฟอง  3 ฟอง         ✅    │
│ น้ำมัน 1ชต.  0 ชต.         ❌ ขาด│
│ เกลือ 1หยิบ  200 ก.        ✅    │
│ ─────────────────────────────────│
│ สรุป: ขาด 1 อย่าง                │
│ [→ เพิ่ม stock] [เปลี่ยน recipe] │
└──────────────────────────────────┘
```

---

### 9. Family (`/family`)

**Purpose:** จัดการสมาชิกและ invite code

**Entry:** user menu → "จัดการ Family"

**Layout:** sections ต่อกันแนวตั้ง

**Components:**
- Section 1 — Family info:
  - ชื่อ family (editable โดย admin/creator)
  - จำนวนสมาชิก
- Section 2 — Invite code:
  - แสดง code ปัจจุบัน (เช่น `ABCD-1234`) + ปุ่ม "📋 Copy" + "🔄 Rotate (สร้างใหม่)"
  - คำอธิบาย: "ส่ง code นี้ให้สมาชิกใหม่ — code เดียวใช้ได้หลายครั้ง"
- Section 3 — Members list:
  - แต่ละ row: avatar + ชื่อ + email + relationship label (เช่น "ภรรยาของสมชาย") + ✏️ edit relationships
- Section 4 — Relationships editor (modal/dialog):
  - dropdown "สมาชิก A" + "เป็น…" (Parent/Child/Spouse/Sibling/Other) + "ของ สมาชิก B"
  - list relationships ที่มี → ลบได้
- Section 5 — Leave family (danger zone):
  - ปุ่ม "ออกจาก family นี้" (outlined red) → confirm dialog

**Interactions:**
1. Rotate code: คลิก 🔄 → confirm "จะทำให้ code เก่าใช้ไม่ได้ — แน่ใจ?" → POST → แสดง code ใหม่
2. Edit relationship: คลิก ✏️ → modal editor → บันทึก → update list
3. ออกจาก family: confirm → POST `/api/families/leave` → `user.familyId = null` → redirect `/join-family`

**States:** loading / normal. MVP นี้ไม่ enforce role — ทุกคนใน family edit ได้เท่ากัน (เพราะ relationships = metadata-only)

---

### 10. Shopping Lists (`/shopping` + `/shopping/:id`)

**Purpose:** จัดการรายการซื้อของ — สร้างเอง หรือ auto-generate จาก meal plan; mark bought → auto-เพิ่ม stock

**Entry:** top nav tab "Shopping" หรือจาก Meal Plan sidebar ("เพิ่มใน shopping list")

**Layout:** 2 หน้า

#### 10a. Shopping Lists — index (`/shopping`)

**Components:**
- Top: ปุ่ม "+ สร้าง list ใหม่" + filter dropdown (Active / Completed / ทั้งหมด)
- Grid/list ของ card:
  - แต่ละ card: ชื่อ list, จำนวน items (เช่น "8 / 12 ซื้อแล้ว"), progress bar, สถานะ (🟢 active / ⚪ archived), created date
  - คลิก → `/shopping/{id}`
- Empty state: illustration + "ยังไม่มี shopping list — [สร้างแรก]"

**Interactions:**
1. "+ สร้าง list ใหม่" → modal:
   - Input ชื่อ list (default "ของใช้ประจำสัปดาห์")
   - Checkbox "📅 Auto-generate จาก meal plan" → toggle เปิด date range picker (from / to)
   - ปุ่ม "สร้าง"
   - POST `/api/shopping-lists` (ถ้ามี date range → backend compute missing items และ insert)
   - Redirect → `/shopping/{id}`

#### 10b. Shopping List Detail (`/shopping/:id`)

**Components:**
- Header: ชื่อ list (editable) + ปุ่ม "🔄 Regenerate" (ถ้า list สร้างจาก meal plan) + ⚙️ menu (Archive, Delete)
- Progress bar: "ซื้อแล้ว 5 / 12 รายการ"
- Tab/section:
  - **Unbought** (default, แสดงก่อน): รายการที่ยังไม่ซื้อ
  - **Bought**: รายการที่ซื้อแล้ว (เรียงตาม BoughtAt DESC)
- แต่ละ row (item):
  - Checkbox (Unbought) / Checkmark (Bought)
  - ชื่อ ingredient
  - Quantity + unit (เช่น "ไข่ไก่ 6 ฟอง")
  - (Optional tag) "จาก: ไข่เจียว (14 เม.ย.)"
  - Edit/Delete icons
- ปุ่ม "+ เพิ่ม item" ด้านล่าง (autocomplete ingredient + quantity)

**Interactions:**
1. คลิก checkbox → mark bought:
   - PATCH `/api/shopping-lists/{id}/items/{itemId}` → `{ isBought: true }`
   - Backend: update item + insert StockTransaction (Delta=+quantity, Source=ShoppingListBought) + update Stock.Quantity
   - UI: row ย้ายไป tab "Bought" พร้อม animation + toast "เพิ่มใน stock: ไข่ไก่ 6 ฟอง"
2. Uncheck (in Bought tab): confirm → reverse transaction → row กลับไป Unbought
3. "🔄 Regenerate" → confirm "จะคำนวณ items จาก meal plan ใหม่ — items ที่ซื้อแล้วจะถูกเก็บไว้. ดำเนินการ?"
   - POST `/api/shopping-lists/{id}/regenerate` → update items (preserve IsBought=true rows)
4. "+ เพิ่ม item" → modal: autocomplete ingredient (เลือกจาก master หรือสร้างใหม่) + quantity → POST
5. Edit quantity inline → blur = save (optimistic)
6. ปุ่ม "Complete list" (top right) → เปลี่ยน Status=Completed → redirect `/shopping`

**States:**
- Loading skeleton / empty ("list ว่าง — [เพิ่ม item แรก]" or "generate from meal plan")
- Row highlight สีเขียวจาง หลัง mark bought (animation 2 วินาที)
- Error (stock update failed): toast error + revert checkbox

---

### Cross-page patterns

- **Toast notifications** (top-right): success (เขียว) / error (แดง) / info — auto dismiss 4 วินาที
- **Confirmation dialogs**: destructive actions (delete, leave family, rotate code)
- **Loading states**: skeleton screens (ไม่ใช้ full-page spinner)
- **Error boundary**: generic fallback "เกิดข้อผิดพลาด — [ลองใหม่]"
- **Empty states**: illustration + call-to-action button
- **Unsaved-changes guard**: ตอน navigate ออกจาก edit form ที่ยังไม่ save

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
- `infra/main.bicep` (optional — provision SQL/Storage/App Service) หรือ manual portal setup
