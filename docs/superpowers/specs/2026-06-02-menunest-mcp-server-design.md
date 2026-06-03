# MenuNest MCP Server — Design Spec

**Date:** 2026-06-02
**Status:** Awaiting approval
**Scope:** Meal planning domain only (Recipes, Ingredients, MealPlan, Stock, ShoppingLists, Budget)

---

## Goals

Expose the MenuNest meal-planning API as a Model Context Protocol (MCP) server so
Claude (mobile and web) can manage recipes, stock, meal plans, and shopping lists
through natural conversation — without opening the web app.

---

## Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | C# new project `MenuNest.McpServer` (class library) in existing solution | Shares DTOs, MediatR handlers, and DI with no HTTP round-trip |
| 2 | MCP tools call `IMediator` directly | No HTTP overhead; same code path as controllers |
| 3 | HTTP/SSE transport (Streamable HTTP) | Required for Claude mobile and Claude.ai web |
| 4 | Hosted on the same Azure App Service as `MenuNest.WebApi` | No extra Azure resource; `/mcp` route added to existing app |
| 5 | Auth via Entra ID OAuth (Microsoft identity) | Delegates entirely to existing Entra ID tenant; reuses `"Microsoft"` JwtBearer handler already in `Program.cs`; see ADR-001 |
| 6 | Health / migraine features excluded | Out of scope |
| 7 | Chat (Gemini AI) tool excluded | Redundant — Claude is the AI |

---

## Architecture

```
Claude mobile / web
        │  HTTPS  OAuth token (Entra ID JWT)
        ▼
MenuNest.WebApi  (existing App Service — unchanged deployment)
  ├── /api/*          ← existing REST controllers (unchanged)
  ├── /mcp            ← NEW: MCP SSE endpoint (ModelContextProtocol.AspNetCore)
  │     validates Bearer token via existing "Microsoft" JwtBearer handler
  │     resolves user via ICurrentUserService
  │     calls IMediator → Application handlers → Infrastructure
  └── /.well-known/oauth-authorization-server  ← NEW: OAuth discovery (anonymous)
```

`MenuNest.McpServer` is a **class library** (no `Program.cs`).
`MenuNest.WebApi` adds a project reference, calls `AddMcpServer()`, and maps `/mcp`.

---

## Project Structure

### New: `backend/src/MenuNest.McpServer/`

```
MenuNest.McpServer/
├── MenuNest.McpServer.csproj
├── McpServerRegistration.cs        ← AddMcpServer() extension
└── Tools/
    ├── RecipeTools.cs
    ├── IngredientTools.cs
    ├── MealPlanTools.cs
    ├── StockTools.cs
    ├── ShoppingListTools.cs
    └── BudgetTools.cs
```

### Changes to `MenuNest.WebApi`

- Add `<ProjectReference>` to `MenuNest.McpServer`
- `Program.cs`: `builder.Services.AddMcpServer()`
- `Program.cs`: `app.MapMcp("/mcp").RequireAuthorization()`
- `Program.cs`: map `/.well-known/oauth-authorization-server` (anonymous, returns Entra ID metadata)

---

## NuGet (McpServer project)

```xml
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="0.3.*" />
<PackageReference Include="Mediator.Abstractions" Version="*" />
```

---

## OAuth Discovery Endpoint

Claude.ai fetches `/.well-known/oauth-authorization-server` on first connect.

Response — points entirely to Entra ID, no custom OAuth server needed:

```json
{
  "issuer": "https://login.microsoftonline.com/common/v2.0",
  "authorization_endpoint": "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
  "token_endpoint": "https://login.microsoftonline.com/common/oauth2/v2.0/token",
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"]
}
```

Tenant ID comes from `AzureAd:TenantId`. Endpoint is `[AllowAnonymous]`.

### Token flow

1. Claude fetches `/.well-known/oauth-authorization-server`
2. Claude opens Microsoft login → user authenticates with their Microsoft account
3. Claude receives Entra ID JWT; presents it as `Authorization: Bearer <token>` on every MCP call
4. Existing `"Microsoft"` JwtBearer handler validates it (already wired in `Program.cs`)
5. `ICurrentUserService` resolves user from `HttpContext.User` (no changes needed)

**Required scope in Claude MCP config:** `api://{AzureAd:ClientId}/access_as_user`

---

## MCP Tool Inventory — 49 tools total

### RecipeTools — 5 tools

| Tool | MediatR | Key inputs |
|---|---|---|
| `list_recipes` | `ListRecipesQuery` | — |
| `get_recipe` | `GetRecipeQuery` | `id: Guid` |
| `create_recipe` | `CreateRecipeCommand` | `name`, `description?`, `ingredients: RecipeIngredientInput[]` |
| `update_recipe` | `UpdateRecipeCommand` | `id`, `name`, `description?`, `ingredients` |
| `delete_recipe` | `DeleteRecipeCommand` | `id` |

### IngredientTools — 4 tools

| Tool | MediatR | Key inputs |
|---|---|---|
| `list_ingredients` | `ListIngredientsQuery` | — |
| `create_ingredient` | `CreateIngredientCommand` | `name`, `unit` |
| `update_ingredient` | `UpdateIngredientCommand` | `id`, `name`, `unit` |
| `delete_ingredient` | `DeleteIngredientCommand` | `id` |

### MealPlanTools — 7 tools

| Tool | MediatR | Key inputs |
|---|---|---|
| `list_meal_plan` | `ListMealPlanQuery` | `from: DateOnly`, `to: DateOnly` |
| `create_meal_plan_entry` | `CreateMealPlanEntryCommand` | `date`, `mealSlot: MealSlot`, `recipeId`, `notes?` |
| `update_meal_plan_entry` | `UpdateMealPlanEntryCommand` | `id`, `recipeId`, `notes?` |
| `delete_meal_plan_entry` | `DeleteMealPlanEntryCommand` | `id` |
| `stock_check` | `StockCheckQuery` | `entryId` |
| `stock_check_batch` | `StockCheckBatchQuery` | `entryIds: Guid[]` |
| `cook_batch` | `CookBatchCommand` | `entryIds: Guid[]` |

### StockTools — 3 tools

| Tool | MediatR | Key inputs |
|---|---|---|
| `list_stock` | `ListStockQuery` | — |
| `upsert_stock` | `UpsertStockCommand` | `ingredientId`, `quantity: decimal` |
| `delete_stock` | `DeleteStockCommand` | `id` |

### ShoppingListTools — 10 tools

| Tool | MediatR | Key inputs |
|---|---|---|
| `list_shopping_lists` | `ListShoppingListsQuery` | `status: ShoppingListStatus?` |
| `get_shopping_list` | `GetShoppingListDetailQuery` | `id` |
| `create_shopping_list` | `CreateShoppingListCommand` | `name`, `fromDate?`, `toDate?` |
| `delete_shopping_list` | `DeleteShoppingListCommand` | `id` |
| `complete_shopping_list` | `CompleteShoppingListCommand` | `id` |
| `add_shopping_list_item` | `AddShoppingListItemCommand` | `listId`, `ingredientId`, `quantity` |
| `delete_shopping_list_item` | `DeleteShoppingListItemCommand` | `listId`, `itemId` |
| `buy_shopping_list_item` | `BuyShoppingListItemCommand` | `listId`, `itemId` |
| `unbuy_shopping_list_item` | `UnbuyShoppingListItemCommand` | `listId`, `itemId` |
| `regenerate_shopping_list` | `RegenerateShoppingListCommand` | `id` |

### BudgetTools — 20 tools

| Tool | MediatR | Key inputs |
|---|---|---|
| `get_budget_summary` | `GetMonthlySummaryQuery` | `year`, `month` |
| `list_budget_accounts` | `ListAccountsQuery` | — |
| `create_budget_account` | `CreateAccountCommand` | `name`, `type: AccountType`, `openingBalance` |
| `update_budget_account` | `UpdateAccountCommand` | `id`, `name`, `sortOrder?`, `isClosed?`, `setBalance?` |
| `delete_budget_account` | `DeleteAccountCommand` | `id` |
| `list_account_transactions` | `ListAccountTransactionsQuery` | `accountId`, `year`, `month`, `skip`, `take` |
| `list_budget_groups` | `ListGroupsQuery` | — |
| `create_budget_group` | `CreateGroupCommand` | `name` |
| `update_budget_group` | `UpdateGroupCommand` | `id`, `name`, `sortOrder?` |
| `delete_budget_group` | `DeleteGroupCommand` | `id` |
| `create_budget_category` | `CreateCategoryCommand` | `groupId`, `name`, `emoji?`, target fields |
| `update_budget_category` | `UpdateCategoryCommand` | `id`, `groupId`, `name`, `emoji?`, `sortOrder?`, target fields |
| `delete_budget_category` | `DeleteCategoryCommand` | `id` |
| `set_assigned_amount` | `SetAssignedAmountCommand` | `categoryId`, `year`, `month`, `amount` |
| `move_money` | `MoveMoneyCommand` | `fromCategoryId`, `toCategoryId`, `year`, `month`, `amount` |
| `cover_overspending` | `CoverOverspendingCommand` | `overspentCategoryId`, `fromCategoryId`, `year`, `month`, `amount` |
| `list_transactions` | `ListTransactionsQuery` | `year`, `month`, `categoryId?` |
| `create_transaction` | `CreateTransactionCommand` | `accountId`, `categoryId`, `amount`, `date`, `notes?` |
| `update_transaction` | `UpdateTransactionCommand` | `id`, `accountId`, `categoryId`, `amount`, `date`, `notes?` |
| `delete_transaction` | `DeleteTransactionCommand` | `id` |

---

## Files Created / Modified

| File | Action |
|---|---|
| `backend/src/MenuNest.McpServer/MenuNest.McpServer.csproj` | Create |
| `backend/src/MenuNest.McpServer/McpServerRegistration.cs` | Create |
| `backend/src/MenuNest.McpServer/Tools/RecipeTools.cs` | Create |
| `backend/src/MenuNest.McpServer/Tools/IngredientTools.cs` | Create |
| `backend/src/MenuNest.McpServer/Tools/MealPlanTools.cs` | Create |
| `backend/src/MenuNest.McpServer/Tools/StockTools.cs` | Create |
| `backend/src/MenuNest.McpServer/Tools/ShoppingListTools.cs` | Create |
| `backend/src/MenuNest.McpServer/Tools/BudgetTools.cs` | Create |
| `backend/MenuNest.sln` | Add new project |
| `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj` | Add ProjectReference |
| `backend/src/MenuNest.WebApi/Program.cs` | Register services + map `/mcp` + map `/.well-known/oauth-authorization-server` |

---

## Claude MCP Configuration (after deployment)

In Claude Settings → Integrations → Add MCP Server:

```
URL:   https://menunest.azurewebsites.net/mcp
Auth:  OAuth / Microsoft
Scope: api://{AzureAd:ClientId}/access_as_user
```

---

## Out of Scope

- Health / migraine tracking (Episodes, Drugs, Intakes, FollowUps, Symptoms)
- Doctor report share links
- Push notification subscriptions
- Photo uploads (Blob SAS — not suitable for MCP tool responses)
- Chat / Gemini AI assistant
