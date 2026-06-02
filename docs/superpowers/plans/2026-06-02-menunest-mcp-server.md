# MenuNest MCP Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Model Context Protocol server to MenuNest so Claude mobile and Claude.ai web can manage recipes, meal plans, stock, shopping lists, and budget through natural conversation without opening the web app.

**Architecture:** `MenuNest.McpServer` is a new class library containing 49 MCP tool methods in 6 domain classes. Each method is a one-line `IMediator.Send()` call that reuses all existing Application handlers — no new business logic. The library is referenced by `MenuNest.WebApi`, which maps `/mcp` (auth-required, Entra ID JWT) and `/.well-known/oauth-authorization-server` (anonymous, points Claude to Entra ID's OAuth endpoints).

**Tech Stack:** .NET 10, ModelContextProtocol.AspNetCore 0.3.x, martinothamar/Mediator 3.x (already in codebase — `IMediator.Send` returns `ValueTask<T>`), xUnit 2.9, Moq 4.20, FluentAssertions 8.x

---

## File Map

### New files

| File | Purpose |
|---|---|
| `backend/src/MenuNest.McpServer/MenuNest.McpServer.csproj` | Class library; references Application + MCP NuGet |
| `backend/src/MenuNest.McpServer/GlobalUsings.cs` | Shared global using directives for all tool files |
| `backend/src/MenuNest.McpServer/McpServerRegistration.cs` | `AddMenuNestMcpServer()` extension: registers all 6 tool types + HTTP transport |
| `backend/src/MenuNest.McpServer/Tools/RecipeTools.cs` | 5 tools: list/get/create/update/delete recipes |
| `backend/src/MenuNest.McpServer/Tools/IngredientTools.cs` | 4 tools: list/create/update/delete ingredients |
| `backend/src/MenuNest.McpServer/Tools/MealPlanTools.cs` | 7 tools: CRUD + stock-check + cook-batch |
| `backend/src/MenuNest.McpServer/Tools/StockTools.cs` | 3 tools: list/upsert/delete stock |
| `backend/src/MenuNest.McpServer/Tools/ShoppingListTools.cs` | 10 tools: list lifecycle + item buy/unbuy/delete |
| `backend/src/MenuNest.McpServer/Tools/BudgetTools.cs` | 20 tools: accounts, groups, categories, transactions, assigned amounts |
| `backend/tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj` | xUnit test project |
| `backend/tests/MenuNest.McpServer.UnitTests/Tools/RecipeToolsTests.cs` | Verify RecipeTools dispatches correct messages |
| `backend/tests/MenuNest.McpServer.UnitTests/Tools/IngredientToolsTests.cs` | Verify IngredientTools dispatches correct messages |
| `backend/tests/MenuNest.McpServer.UnitTests/Tools/MealPlanToolsTests.cs` | Verify MealPlanTools dispatches correct messages |
| `backend/tests/MenuNest.McpServer.UnitTests/Tools/StockToolsTests.cs` | Verify StockTools dispatches correct messages |
| `backend/tests/MenuNest.McpServer.UnitTests/Tools/ShoppingListToolsTests.cs` | Verify ShoppingListTools dispatches correct messages |
| `backend/tests/MenuNest.McpServer.UnitTests/Tools/BudgetToolsTests.cs` | Verify BudgetTools dispatches correct messages |

### Modified files

| File | Change |
|---|---|
| `backend/MenuNest.sln` | Register McpServer and McpServer.UnitTests projects |
| `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj` | Add ProjectReference to McpServer + ModelContextProtocol.AspNetCore NuGet |
| `backend/src/MenuNest.WebApi/Program.cs` | Call `AddMenuNestMcpServer()`, `MapMcp("/mcp")`, add OAuth discovery endpoint |

---

### Task 1: Scaffold MenuNest.McpServer project

**Files:**
- Create: `backend/src/MenuNest.McpServer/MenuNest.McpServer.csproj`
- Create: `backend/src/MenuNest.McpServer/GlobalUsings.cs`
- Create: `backend/src/MenuNest.McpServer/McpServerRegistration.cs`
- Create: `backend/tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj`
- Modify: `backend/MenuNest.sln`

- [ ] **Step 1: Create the McpServer class library project file**

```xml
<!-- backend/src/MenuNest.McpServer/MenuNest.McpServer.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="0.3.*" />
    <PackageReference Include="Mediator.Abstractions" Version="3.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MenuNest.Application\MenuNest.Application.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create GlobalUsings.cs**

```csharp
// backend/src/MenuNest.McpServer/GlobalUsings.cs
global using System.ComponentModel;
global using Mediator;
global using ModelContextProtocol.Server;
```

- [ ] **Step 3: Create McpServerRegistration.cs (stub — tools added in Tasks 3–8)**

```csharp
// backend/src/MenuNest.McpServer/McpServerRegistration.cs
using Microsoft.Extensions.DependencyInjection;

namespace MenuNest.McpServer;

public static class McpServerRegistration
{
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport();
    // Tool registrations (.WithTools<T>()) are added incrementally in Tasks 3–8.
}
```

- [ ] **Step 4: Create the unit test project file**

```xml
<!-- backend/tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="FluentAssertions" Version="8.9.0" />
    <PackageReference Include="Mediator.Abstractions" Version="3.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MenuNest.McpServer\MenuNest.McpServer.csproj" />
    <ProjectReference Include="..\..\src\MenuNest.Application\MenuNest.Application.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Register both new projects in `backend/MenuNest.sln`**

Generate two new project GUIDs in PowerShell:
```powershell
[System.Guid]::NewGuid().ToString().ToUpper()  # run twice
```

Open `backend/MenuNest.sln` and add these two `Project(...)` blocks. Place them after the existing `MenuNest.Infrastructure` and `MenuNest.Infrastructure.IntegrationTests` entries respectively. Replace `{NEW-GUID-SRC}` and `{NEW-GUID-TEST}` with your generated GUIDs:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MenuNest.McpServer", "src\MenuNest.McpServer\MenuNest.McpServer.csproj", "{NEW-GUID-SRC}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MenuNest.McpServer.UnitTests", "tests\MenuNest.McpServer.UnitTests\MenuNest.McpServer.UnitTests.csproj", "{NEW-GUID-TEST}"
EndProject
```

In `GlobalSection(NestedProjects)`, nest the src project under the `src` solution folder GUID and the test project under the `tests` solution folder GUID. The solution folder GUIDs are already in the file — look for the lines:
```
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", ...
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tests", ...
```
and add:
```
{NEW-GUID-SRC} = {SRC-FOLDER-GUID}
{NEW-GUID-TEST} = {TESTS-FOLDER-GUID}
```

- [ ] **Step 6: Verify the solution builds**

```powershell
cd backend; dotnet build MenuNest.sln
```

Expected output: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.McpServer/ backend/tests/MenuNest.McpServer.UnitTests/ backend/MenuNest.sln
git commit -m "feat(mcp): scaffold MenuNest.McpServer class library and test project"
```

---

### Task 2: Wire McpServer into WebApi and add OAuth discovery endpoint

**Files:**
- Modify: `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`
- Modify: `backend/src/MenuNest.WebApi/Program.cs`

- [ ] **Step 1: Add NuGet + ProjectReference to WebApi.csproj**

In `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`, add `ModelContextProtocol.AspNetCore` to the existing `<ItemGroup>` with PackageReferences:

```xml
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="0.3.*" />
```

Add a new `<ItemGroup>` for the McpServer project reference (after the existing ProjectReferences group):

```xml
<ItemGroup>
  <ProjectReference Include="..\MenuNest.McpServer\MenuNest.McpServer.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Add `using` and register MCP services in Program.cs**

At the top of `backend/src/MenuNest.WebApi/Program.cs`, add after the existing `using` directives:

```csharp
using MenuNest.McpServer;
```

After the `builder.Services.AddMediator(...)` block (line 22 — the closing `});` of the mediator options), add:

```csharp
// MCP Server — tools call IMediator; no new handlers or business logic
builder.Services.AddMenuNestMcpServer();
```

- [ ] **Step 3: Map the MCP route and OAuth discovery endpoint in Program.cs**

After `app.MapControllers();` (the last line before `app.Run()`), add:

```csharp
// MCP — Streamable HTTP; authentication is handled by the existing JwtBearer middleware
app.MapMcp("/mcp").RequireAuthorization();

// OAuth 2.0 discovery: Claude fetches this on first connect to learn where to authenticate
app.MapGet("/.well-known/oauth-authorization-server", () => Results.Ok(new
{
    issuer = "https://login.microsoftonline.com/common/v2.0",
    authorization_endpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
    token_endpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
    response_types_supported = new[] { "code" },
    grant_types_supported = new[] { "authorization_code", "refresh_token" },
    code_challenge_methods_supported = new[] { "S256" }
})).AllowAnonymous();
```

- [ ] **Step 4: Build to verify wiring compiles**

```powershell
cd backend; dotnet build MenuNest.sln
```

Expected: `Build succeeded. 0 Error(s).`

If `MapMcp` is not found, the `ModelContextProtocol.AspNetCore` package was not restored — run `dotnet restore` first.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj backend/src/MenuNest.WebApi/Program.cs
git commit -m "feat(mcp): wire McpServer into WebApi, map /mcp route and OAuth discovery"
```

---

### Task 3: RecipeTools (TDD)

**Background:** The Mediator library (martinothamar) is source-generator based. `IMediator.Send(query, ct)` returns `ValueTask<T>`. Tool methods are `async Task<T>` — `await`ing a `ValueTask<T>` is fine. Family scoping happens inside the Application handlers via `IUserProvisioner.RequireFamilyAsync()` — the tools pass no family ID.

Before writing the tools, read these files to confirm type names and constructor shapes:
- `backend/src/MenuNest.Application/UseCases/Recipes/ListRecipes/ListRecipesQuery.cs` — return type
- `backend/src/MenuNest.Application/UseCases/Recipes/GetRecipe/GetRecipeQuery.cs` — constructor `(Guid Id)`
- `backend/src/MenuNest.Application/UseCases/Recipes/CreateRecipe/CreateRecipeCommand.cs` — constructor args + `RecipeIngredientInput` type
- `backend/src/MenuNest.Application/UseCases/Recipes/UpdateRecipe/UpdateRecipeCommand.cs`
- `backend/src/MenuNest.Application/UseCases/Recipes/DeleteRecipe/DeleteRecipeCommand.cs`

**Files:**
- Create: `backend/tests/MenuNest.McpServer.UnitTests/Tools/RecipeToolsTests.cs`
- Create: `backend/src/MenuNest.McpServer/Tools/RecipeTools.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs`

- [ ] **Step 1: Write failing tests for RecipeTools**

```csharp
// backend/tests/MenuNest.McpServer.UnitTests/Tools/RecipeToolsTests.cs
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Recipes;
using MenuNest.Application.UseCases.Recipes.ListRecipes;
using MenuNest.Application.UseCases.Recipes.GetRecipe;
using MenuNest.Application.UseCases.Recipes.CreateRecipe;
using MenuNest.Application.UseCases.Recipes.DeleteRecipe;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class RecipeToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly RecipeTools _sut;

    public RecipeToolsTests() => _sut = new RecipeTools(_mediator.Object);

    [Fact]
    public async Task list_recipes_sends_ListRecipesQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListRecipesQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<RecipeSummaryDto>>(new List<RecipeSummaryDto>()));

        await _sut.list_recipes(CancellationToken.None);

        _mediator.Verify(m => m.Send(It.IsAny<ListRecipesQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task get_recipe_sends_GetRecipeQuery_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<GetRecipeQuery>(q => q.Id == id), It.IsAny<CancellationToken>()))
            .Returns<GetRecipeQuery, CancellationToken>((_, _) => new ValueTask<RecipeDetailDto>(default!));

        await _sut.get_recipe(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<GetRecipeQuery>(q => q.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_recipe_sends_CreateRecipeCommand_with_name()
    {
        const string name = "Carbonara";
        _mediator
            .Setup(m => m.Send(It.Is<CreateRecipeCommand>(c => c.Name == name), It.IsAny<CancellationToken>()))
            .Returns<CreateRecipeCommand, CancellationToken>((_, _) => new ValueTask<RecipeDetailDto>(default!));

        await _sut.create_recipe(name, null, Array.Empty<RecipeIngredientInput>(), CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<CreateRecipeCommand>(c => c.Name == name), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_recipe_sends_DeleteRecipeCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteRecipeCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        await _sut.delete_recipe(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<DeleteRecipeCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

**Note:** If `GetRecipeQuery.Id`, `CreateRecipeCommand.Name`, or `DeleteRecipeCommand.Id` don't match the actual property names, a compile error will tell you. Fix the test to match the record's property name.

- [ ] **Step 2: Run tests — expect compile failure**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj
```

Expected: compile error — `RecipeTools` does not exist yet. This confirms the test drives the implementation.

- [ ] **Step 3: Implement RecipeTools.cs**

First read `CreateRecipeCommand.cs` to get the exact `RecipeIngredientInput` constructor and `UpdateRecipeCommand` signature. Then write:

```csharp
// backend/src/MenuNest.McpServer/Tools/RecipeTools.cs
using MenuNest.Application.UseCases.Recipes;
using MenuNest.Application.UseCases.Recipes.ListRecipes;
using MenuNest.Application.UseCases.Recipes.GetRecipe;
using MenuNest.Application.UseCases.Recipes.CreateRecipe;
using MenuNest.Application.UseCases.Recipes.UpdateRecipe;
using MenuNest.Application.UseCases.Recipes.DeleteRecipe;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class RecipeTools(IMediator mediator)
{
    [McpServerTool, Description("List all recipes in the family")]
    public async Task<IReadOnlyList<RecipeSummaryDto>> list_recipes(CancellationToken ct)
        => await mediator.Send(new ListRecipesQuery(), ct);

    [McpServerTool, Description("Get full details of a recipe by ID")]
    public async Task<RecipeDetailDto> get_recipe(
        [Description("Recipe ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new GetRecipeQuery(id), ct);

    [McpServerTool, Description("Create a new recipe with ingredients")]
    public async Task<RecipeDetailDto> create_recipe(
        [Description("Recipe name")] string name,
        [Description("Optional description")] string? description,
        [Description("Ingredient list — each item needs ingredientId and quantity")] RecipeIngredientInput[] ingredients,
        CancellationToken ct)
        => await mediator.Send(new CreateRecipeCommand(name, description, ingredients), ct);

    [McpServerTool, Description("Update an existing recipe")]
    public async Task<RecipeDetailDto> update_recipe(
        [Description("Recipe ID")] Guid id,
        [Description("New name")] string name,
        [Description("New description (optional)")] string? description,
        [Description("Updated ingredient list")] RecipeIngredientInput[] ingredients,
        CancellationToken ct)
        => await mediator.Send(new UpdateRecipeCommand(id, name, description, ingredients), ct);

    [McpServerTool, Description("Delete a recipe by ID")]
    public async Task delete_recipe(
        [Description("Recipe ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteRecipeCommand(id), ct);
}
```

**Note:** If `RecipeIngredientInput` is not in the `CreateRecipe` namespace, check all files under `backend/src/MenuNest.Application/UseCases/Recipes/` for the type. The `dotnet build` step will pinpoint any mismatches.

- [ ] **Step 4: Register RecipeTools in McpServerRegistration.cs**

Add `.WithTools<RecipeTools>()` to the builder chain:

```csharp
using MenuNest.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace MenuNest.McpServer;

public static class McpServerRegistration
{
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<RecipeTools>();
}
```

- [ ] **Step 5: Run tests — expect PASS**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~RecipeToolsTests"
```

Expected: `4 passed, 0 failed`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/RecipeTools.cs backend/src/MenuNest.McpServer/McpServerRegistration.cs backend/tests/MenuNest.McpServer.UnitTests/Tools/RecipeToolsTests.cs
git commit -m "feat(mcp): add RecipeTools (5 tools)"
```

---

### Task 4: IngredientTools (TDD)

Before writing, read:
- `backend/src/MenuNest.Application/UseCases/Ingredients/CreateIngredient/CreateIngredientCommand.cs` — confirm property names
- `backend/src/MenuNest.Application/UseCases/Ingredients/UpdateIngredient/UpdateIngredientCommand.cs`

**Files:**
- Create: `backend/tests/MenuNest.McpServer.UnitTests/Tools/IngredientToolsTests.cs`
- Create: `backend/src/MenuNest.McpServer/Tools/IngredientTools.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// backend/tests/MenuNest.McpServer.UnitTests/Tools/IngredientToolsTests.cs
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Ingredients;
using MenuNest.Application.UseCases.Ingredients.ListIngredients;
using MenuNest.Application.UseCases.Ingredients.CreateIngredient;
using MenuNest.Application.UseCases.Ingredients.DeleteIngredient;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class IngredientToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly IngredientTools _sut;

    public IngredientToolsTests() => _sut = new IngredientTools(_mediator.Object);

    [Fact]
    public async Task list_ingredients_sends_ListIngredientsQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListIngredientsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<IngredientDto>>(new List<IngredientDto>()));

        await _sut.list_ingredients(CancellationToken.None);

        _mediator.Verify(m => m.Send(It.IsAny<ListIngredientsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_ingredient_sends_CreateIngredientCommand_with_name_and_unit()
    {
        const string name = "Flour";
        const string unit = "g";
        _mediator
            .Setup(m => m.Send(It.Is<CreateIngredientCommand>(c => c.Name == name && c.Unit == unit), It.IsAny<CancellationToken>()))
            .Returns<CreateIngredientCommand, CancellationToken>((_, _) => new ValueTask<IngredientDto>(default!));

        await _sut.create_ingredient(name, unit, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<CreateIngredientCommand>(c => c.Name == name && c.Unit == unit), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_ingredient_sends_DeleteIngredientCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteIngredientCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        await _sut.delete_ingredient(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<DeleteIngredientCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure** (IngredientTools missing)

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~IngredientToolsTests"
```

- [ ] **Step 3: Implement IngredientTools.cs**

```csharp
// backend/src/MenuNest.McpServer/Tools/IngredientTools.cs
using MenuNest.Application.UseCases.Ingredients;
using MenuNest.Application.UseCases.Ingredients.ListIngredients;
using MenuNest.Application.UseCases.Ingredients.CreateIngredient;
using MenuNest.Application.UseCases.Ingredients.UpdateIngredient;
using MenuNest.Application.UseCases.Ingredients.DeleteIngredient;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class IngredientTools(IMediator mediator)
{
    [McpServerTool, Description("List all ingredients in the family")]
    public async Task<IReadOnlyList<IngredientDto>> list_ingredients(CancellationToken ct)
        => await mediator.Send(new ListIngredientsQuery(), ct);

    [McpServerTool, Description("Create a new ingredient")]
    public async Task<IngredientDto> create_ingredient(
        [Description("Ingredient name")] string name,
        [Description("Unit of measurement (e.g. g, ml, pcs)")] string unit,
        CancellationToken ct)
        => await mediator.Send(new CreateIngredientCommand(name, unit), ct);

    [McpServerTool, Description("Update an ingredient's name or unit")]
    public async Task<IngredientDto> update_ingredient(
        [Description("Ingredient ID")] Guid id,
        [Description("New name")] string name,
        [Description("New unit")] string unit,
        CancellationToken ct)
        => await mediator.Send(new UpdateIngredientCommand(id, name, unit), ct);

    [McpServerTool, Description("Delete an ingredient by ID")]
    public async Task delete_ingredient(
        [Description("Ingredient ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteIngredientCommand(id), ct);
}
```

- [ ] **Step 4: Add `.WithTools<IngredientTools>()` to McpServerRegistration.cs**

```csharp
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<RecipeTools>()
            .WithTools<IngredientTools>();
```

- [ ] **Step 5: Run tests — expect PASS**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~IngredientToolsTests"
```

Expected: `3 passed, 0 failed`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/IngredientTools.cs backend/src/MenuNest.McpServer/McpServerRegistration.cs backend/tests/MenuNest.McpServer.UnitTests/Tools/IngredientToolsTests.cs
git commit -m "feat(mcp): add IngredientTools (4 tools)"
```

---

### Task 5: MealPlanTools (TDD)

Before writing, read these files to confirm constructor shapes:
- `backend/src/MenuNest.Application/UseCases/MealPlan/CreateMealPlanEntry/CreateMealPlanEntryCommand.cs`
- `backend/src/MenuNest.Application/UseCases/MealPlan/StockCheck/StockCheckQuery.cs` — confirm return type (`StockCheckDto`)
- `backend/src/MenuNest.Application/UseCases/MealPlan/StockCheckBatch/StockCheckBatchQuery.cs` — confirm return type
- `backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchCommand.cs` — confirm return type (`CookBatchResult`)

The `MealSlot` enum is in the Domain or Application layer — search for `enum MealSlot` in `backend/src/` to find its namespace.

**Files:**
- Create: `backend/tests/MenuNest.McpServer.UnitTests/Tools/MealPlanToolsTests.cs`
- Create: `backend/src/MenuNest.McpServer/Tools/MealPlanTools.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs`

- [ ] **Step 1: Find `MealSlot` enum namespace**

```powershell
Select-String -Path "backend\src\**\*.cs" -Pattern "enum MealSlot" -Recurse | Select-Object -First 1
```

Note the namespace — you need it in the `using` directives of `MealPlanTools.cs`.

- [ ] **Step 2: Write failing tests**

```csharp
// backend/tests/MenuNest.McpServer.UnitTests/Tools/MealPlanToolsTests.cs
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.MealPlan;
using MenuNest.Application.UseCases.MealPlan.ListMealPlan;
using MenuNest.Application.UseCases.MealPlan.CreateMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.DeleteMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.StockCheck;
using MenuNest.Application.UseCases.MealPlan.CookBatch;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class MealPlanToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly MealPlanTools _sut;

    public MealPlanToolsTests() => _sut = new MealPlanTools(_mediator.Object);

    [Fact]
    public async Task list_meal_plan_sends_ListMealPlanQuery_with_date_range()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 7);
        _mediator
            .Setup(m => m.Send(It.Is<ListMealPlanQuery>(q => q.From == from && q.To == to), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<MealPlanEntryDto>>(new List<MealPlanEntryDto>()));

        await _sut.list_meal_plan(from, to, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<ListMealPlanQuery>(q => q.From == from && q.To == to), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task stock_check_sends_StockCheckQuery_with_entry_id()
    {
        var entryId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<StockCheckQuery>(q => q.EntryId == entryId), It.IsAny<CancellationToken>()))
            .Returns<StockCheckQuery, CancellationToken>((_, _) => new ValueTask<StockCheckDto>(default!));

        await _sut.stock_check(entryId, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<StockCheckQuery>(q => q.EntryId == entryId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task cook_batch_sends_CookBatchCommand_with_entry_ids()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _mediator
            .Setup(m => m.Send(It.Is<CookBatchCommand>(c => c.EntryIds.SequenceEqual(ids)), It.IsAny<CancellationToken>()))
            .Returns<CookBatchCommand, CancellationToken>((_, _) => new ValueTask<CookBatchResult>(default!));

        await _sut.cook_batch(ids, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<CookBatchCommand>(c => c.EntryIds.SequenceEqual(ids)), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

**Note:** If `ListMealPlanQuery` uses different property names (e.g., `FromDate` instead of `From`), compile errors will point you there. Fix by reading the query file.

- [ ] **Step 3: Run tests — expect compile failure** (MealPlanTools missing)

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~MealPlanToolsTests"
```

- [ ] **Step 4: Implement MealPlanTools.cs**

Replace `MenuNest.Domain.Enums` below with the actual namespace found in Step 1.

```csharp
// backend/src/MenuNest.McpServer/Tools/MealPlanTools.cs
using MenuNest.Application.UseCases.MealPlan;
using MenuNest.Application.UseCases.MealPlan.ListMealPlan;
using MenuNest.Application.UseCases.MealPlan.CreateMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.UpdateMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.DeleteMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.StockCheck;
using MenuNest.Application.UseCases.MealPlan.StockCheckBatch;
using MenuNest.Application.UseCases.MealPlan.CookBatch;
using MenuNest.Domain.Enums; // replace with actual MealSlot namespace if different

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class MealPlanTools(IMediator mediator)
{
    [McpServerTool, Description("List meal plan entries within a date range")]
    public async Task<IReadOnlyList<MealPlanEntryDto>> list_meal_plan(
        [Description("Start date (inclusive), e.g. 2026-06-01")] DateOnly from,
        [Description("End date (inclusive), e.g. 2026-06-07")] DateOnly to,
        CancellationToken ct)
        => await mediator.Send(new ListMealPlanQuery(from, to), ct);

    [McpServerTool, Description("Add a recipe to a meal slot on a specific date")]
    public async Task<MealPlanEntryDto> create_meal_plan_entry(
        [Description("Date for the meal, e.g. 2026-06-03")] DateOnly date,
        [Description("Meal slot: Breakfast, Lunch, Dinner, or Snack")] MealSlot mealSlot,
        [Description("Recipe ID")] Guid recipeId,
        [Description("Optional notes")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new CreateMealPlanEntryCommand(date, mealSlot, recipeId, notes), ct);

    [McpServerTool, Description("Update a meal plan entry's recipe or notes")]
    public async Task<MealPlanEntryDto> update_meal_plan_entry(
        [Description("Meal plan entry ID")] Guid id,
        [Description("New recipe ID")] Guid recipeId,
        [Description("New notes (optional)")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new UpdateMealPlanEntryCommand(id, recipeId, notes), ct);

    [McpServerTool, Description("Remove a meal plan entry")]
    public async Task delete_meal_plan_entry(
        [Description("Meal plan entry ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteMealPlanEntryCommand(id), ct);

    [McpServerTool, Description("Check whether stock covers the ingredients for a single meal plan entry")]
    public async Task<StockCheckDto> stock_check(
        [Description("Meal plan entry ID")] Guid entryId,
        CancellationToken ct)
        => await mediator.Send(new StockCheckQuery(entryId), ct);

    [McpServerTool, Description("Check stock for multiple meal plan entries at once")]
    public async Task<IReadOnlyList<StockCheckDto>> stock_check_batch(
        [Description("Array of meal plan entry IDs")] Guid[] entryIds,
        CancellationToken ct)
        => await mediator.Send(new StockCheckBatchQuery(entryIds), ct);

    [McpServerTool, Description("Mark a batch of meal plan entries as cooked and deduct stock")]
    public async Task<CookBatchResult> cook_batch(
        [Description("Array of meal plan entry IDs to cook")] Guid[] entryIds,
        CancellationToken ct)
        => await mediator.Send(new CookBatchCommand(entryIds), ct);
}
```

**Note:** `StockCheckBatchQuery` return type — check the query file. If it returns `StockCheckBatchResult` instead of `IReadOnlyList<StockCheckDto>`, update the signature accordingly.

- [ ] **Step 5: Add `.WithTools<MealPlanTools>()` to McpServerRegistration.cs**

```csharp
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<RecipeTools>()
            .WithTools<IngredientTools>()
            .WithTools<MealPlanTools>();
```

- [ ] **Step 6: Run tests — expect PASS**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~MealPlanToolsTests"
```

Expected: `3 passed, 0 failed`.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/MealPlanTools.cs backend/src/MenuNest.McpServer/McpServerRegistration.cs backend/tests/MenuNest.McpServer.UnitTests/Tools/MealPlanToolsTests.cs
git commit -m "feat(mcp): add MealPlanTools (7 tools)"
```

---

### Task 6: StockTools (TDD)

Before writing, read:
- `backend/src/MenuNest.Application/UseCases/Stock/UpsertStock/UpsertStockCommand.cs` — confirm property names
- `backend/src/MenuNest.Application/UseCases/Stock/DeleteStock/DeleteStockCommand.cs`

**Files:**
- Create: `backend/tests/MenuNest.McpServer.UnitTests/Tools/StockToolsTests.cs`
- Create: `backend/src/MenuNest.McpServer/Tools/StockTools.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// backend/tests/MenuNest.McpServer.UnitTests/Tools/StockToolsTests.cs
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Stock;
using MenuNest.Application.UseCases.Stock.ListStock;
using MenuNest.Application.UseCases.Stock.UpsertStock;
using MenuNest.Application.UseCases.Stock.DeleteStock;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class StockToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly StockTools _sut;

    public StockToolsTests() => _sut = new StockTools(_mediator.Object);

    [Fact]
    public async Task list_stock_sends_ListStockQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListStockQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<StockItemDto>>(new List<StockItemDto>()));

        await _sut.list_stock(CancellationToken.None);

        _mediator.Verify(m => m.Send(It.IsAny<ListStockQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task upsert_stock_sends_UpsertStockCommand_with_ingredient_and_quantity()
    {
        var ingredientId = Guid.NewGuid();
        const decimal quantity = 250m;
        _mediator
            .Setup(m => m.Send(It.Is<UpsertStockCommand>(c => c.IngredientId == ingredientId && c.Quantity == quantity), It.IsAny<CancellationToken>()))
            .Returns<UpsertStockCommand, CancellationToken>((_, _) => new ValueTask<StockItemDto>(default!));

        await _sut.upsert_stock(ingredientId, quantity, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<UpsertStockCommand>(c => c.IngredientId == ingredientId && c.Quantity == quantity), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_stock_sends_DeleteStockCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteStockCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        await _sut.delete_stock(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<DeleteStockCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~StockToolsTests"
```

- [ ] **Step 3: Implement StockTools.cs**

```csharp
// backend/src/MenuNest.McpServer/Tools/StockTools.cs
using MenuNest.Application.UseCases.Stock;
using MenuNest.Application.UseCases.Stock.ListStock;
using MenuNest.Application.UseCases.Stock.UpsertStock;
using MenuNest.Application.UseCases.Stock.DeleteStock;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class StockTools(IMediator mediator)
{
    [McpServerTool, Description("List all current stock items for the family")]
    public async Task<IReadOnlyList<StockItemDto>> list_stock(CancellationToken ct)
        => await mediator.Send(new ListStockQuery(), ct);

    [McpServerTool, Description("Set the stock quantity for an ingredient (creates or updates)")]
    public async Task<StockItemDto> upsert_stock(
        [Description("Ingredient ID")] Guid ingredientId,
        [Description("Quantity on hand")] decimal quantity,
        CancellationToken ct)
        => await mediator.Send(new UpsertStockCommand(ingredientId, quantity), ct);

    [McpServerTool, Description("Remove a stock entry by ID")]
    public async Task delete_stock(
        [Description("Stock entry ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteStockCommand(id), ct);
}
```

- [ ] **Step 4: Add `.WithTools<StockTools>()` to McpServerRegistration.cs**

```csharp
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<RecipeTools>()
            .WithTools<IngredientTools>()
            .WithTools<MealPlanTools>()
            .WithTools<StockTools>();
```

- [ ] **Step 5: Run tests — expect PASS**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~StockToolsTests"
```

Expected: `3 passed, 0 failed`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/StockTools.cs backend/src/MenuNest.McpServer/McpServerRegistration.cs backend/tests/MenuNest.McpServer.UnitTests/Tools/StockToolsTests.cs
git commit -m "feat(mcp): add StockTools (3 tools)"
```

---

### Task 7: ShoppingListTools (TDD)

Before writing, read these files to confirm constructor shapes and the `ShoppingListStatus` enum namespace:
- `backend/src/MenuNest.Application/UseCases/ShoppingList/ListShoppingLists/ListShoppingListsQuery.cs` — confirm optional `status` parameter
- `backend/src/MenuNest.Application/UseCases/ShoppingList/CreateShoppingList/CreateShoppingListCommand.cs`
- `backend/src/MenuNest.Application/UseCases/ShoppingList/AddShoppingListItem/AddShoppingListItemCommand.cs`

Search for `ShoppingListStatus` to find its namespace:
```powershell
Select-String -Path "backend\src\**\*.cs" -Pattern "enum ShoppingListStatus" -Recurse | Select-Object -First 1
```

**Files:**
- Create: `backend/tests/MenuNest.McpServer.UnitTests/Tools/ShoppingListToolsTests.cs`
- Create: `backend/src/MenuNest.McpServer/Tools/ShoppingListTools.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// backend/tests/MenuNest.McpServer.UnitTests/Tools/ShoppingListToolsTests.cs
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.ShoppingList;
using MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;
using MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;
using MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;
using MenuNest.Application.UseCases.ShoppingList.DeleteShoppingList;
using MenuNest.Application.UseCases.ShoppingList.AddShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class ShoppingListToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly ShoppingListTools _sut;

    public ShoppingListToolsTests() => _sut = new ShoppingListTools(_mediator.Object);

    [Fact]
    public async Task list_shopping_lists_sends_ListShoppingListsQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListShoppingListsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<ShoppingListDto>>(new List<ShoppingListDto>()));

        await _sut.list_shopping_lists(null, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.IsAny<ListShoppingListsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task get_shopping_list_sends_GetShoppingListDetailQuery_with_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<GetShoppingListDetailQuery>(q => q.Id == id), It.IsAny<CancellationToken>()))
            .Returns<GetShoppingListDetailQuery, CancellationToken>((_, _) => new ValueTask<ShoppingListDetailDto>(default!));

        await _sut.get_shopping_list(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<GetShoppingListDetailQuery>(q => q.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task add_shopping_list_item_sends_AddShoppingListItemCommand()
    {
        var listId = Guid.NewGuid();
        var ingredientId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<AddShoppingListItemCommand>(c => c.ListId == listId && c.IngredientId == ingredientId), It.IsAny<CancellationToken>()))
            .Returns<AddShoppingListItemCommand, CancellationToken>((_, _) => new ValueTask<ShoppingListDetailDto>(default!));

        await _sut.add_shopping_list_item(listId, ingredientId, 2m, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<AddShoppingListItemCommand>(c => c.ListId == listId && c.IngredientId == ingredientId), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~ShoppingListToolsTests"
```

- [ ] **Step 3: Implement ShoppingListTools.cs**

Replace `MenuNest.Domain.Enums` with the actual `ShoppingListStatus` namespace found above.

```csharp
// backend/src/MenuNest.McpServer/Tools/ShoppingListTools.cs
using MenuNest.Application.UseCases.ShoppingList;
using MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;
using MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;
using MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;
using MenuNest.Application.UseCases.ShoppingList.DeleteShoppingList;
using MenuNest.Application.UseCases.ShoppingList.CompleteShoppingList;
using MenuNest.Application.UseCases.ShoppingList.AddShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.DeleteShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.RegenerateShoppingList;
using MenuNest.Domain.Enums; // replace with actual ShoppingListStatus namespace

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class ShoppingListTools(IMediator mediator)
{
    [McpServerTool, Description("List shopping lists, optionally filtered by status (Active or Completed)")]
    public async Task<IReadOnlyList<ShoppingListDto>> list_shopping_lists(
        [Description("Filter by status: Active or Completed. Omit to list all.")] ShoppingListStatus? status,
        CancellationToken ct)
        => await mediator.Send(new ListShoppingListsQuery(status), ct);

    [McpServerTool, Description("Get a shopping list with all its items")]
    public async Task<ShoppingListDetailDto> get_shopping_list(
        [Description("Shopping list ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new GetShoppingListDetailQuery(id), ct);

    [McpServerTool, Description("Create a new shopping list, optionally from a meal plan date range")]
    public async Task<ShoppingListDetailDto> create_shopping_list(
        [Description("List name")] string name,
        [Description("Optional start date for meal plan generation")] DateOnly? fromDate,
        [Description("Optional end date for meal plan generation")] DateOnly? toDate,
        CancellationToken ct)
        => await mediator.Send(new CreateShoppingListCommand(name, fromDate, toDate), ct);

    [McpServerTool, Description("Delete a shopping list")]
    public async Task delete_shopping_list(
        [Description("Shopping list ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteShoppingListCommand(id), ct);

    [McpServerTool, Description("Mark a shopping list as completed")]
    public async Task complete_shopping_list(
        [Description("Shopping list ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new CompleteShoppingListCommand(id), ct);

    [McpServerTool, Description("Add an ingredient item to a shopping list")]
    public async Task<ShoppingListDetailDto> add_shopping_list_item(
        [Description("Shopping list ID")] Guid listId,
        [Description("Ingredient ID")] Guid ingredientId,
        [Description("Quantity needed")] decimal quantity,
        CancellationToken ct)
        => await mediator.Send(new AddShoppingListItemCommand(listId, ingredientId, quantity), ct);

    [McpServerTool, Description("Remove an item from a shopping list")]
    public async Task delete_shopping_list_item(
        [Description("Shopping list ID")] Guid listId,
        [Description("Item ID")] Guid itemId,
        CancellationToken ct)
        => await mediator.Send(new DeleteShoppingListItemCommand(listId, itemId), ct);

    [McpServerTool, Description("Mark a shopping list item as bought")]
    public async Task buy_shopping_list_item(
        [Description("Shopping list ID")] Guid listId,
        [Description("Item ID")] Guid itemId,
        CancellationToken ct)
        => await mediator.Send(new BuyShoppingListItemCommand(listId, itemId), ct);

    [McpServerTool, Description("Unmark a shopping list item (mark as not yet bought)")]
    public async Task unbuy_shopping_list_item(
        [Description("Shopping list ID")] Guid listId,
        [Description("Item ID")] Guid itemId,
        CancellationToken ct)
        => await mediator.Send(new UnbuyShoppingListItemCommand(listId, itemId), ct);

    [McpServerTool, Description("Regenerate a shopping list's items from the current meal plan")]
    public async Task regenerate_shopping_list(
        [Description("Shopping list ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new RegenerateShoppingListCommand(id), ct);
}
```

- [ ] **Step 4: Add `.WithTools<ShoppingListTools>()` to McpServerRegistration.cs**

```csharp
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<RecipeTools>()
            .WithTools<IngredientTools>()
            .WithTools<MealPlanTools>()
            .WithTools<StockTools>()
            .WithTools<ShoppingListTools>();
```

- [ ] **Step 5: Run tests — expect PASS**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~ShoppingListToolsTests"
```

Expected: `3 passed, 0 failed`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/ShoppingListTools.cs backend/src/MenuNest.McpServer/McpServerRegistration.cs backend/tests/MenuNest.McpServer.UnitTests/Tools/ShoppingListToolsTests.cs
git commit -m "feat(mcp): add ShoppingListTools (10 tools)"
```

---

### Task 8: BudgetTools (TDD)

BudgetTools is the largest domain (20 tools). Before writing, read these to confirm property names and enum namespaces:
- `backend/src/MenuNest.Application/UseCases/Budget/Accounts/CreateAccount/CreateAccountCommand.cs`
- `backend/src/MenuNest.Application/UseCases/Budget/Transactions/CreateTransaction/CreateTransactionCommand.cs`
- `backend/src/MenuNest.Application/UseCases/Budget/Categories/CreateCategory/CreateCategoryCommand.cs` — target fields
- `backend/src/MenuNest.Application/UseCases/Budget/Summary/GetMonthlySummary/GetMonthlySummaryQuery.cs`

Search for `BudgetAccountType` and `BudgetCategoryTargetType` (or equivalent) enum namespaces:
```powershell
Select-String -Path "backend\src\**\*.cs" -Pattern "enum BudgetAccountType" -Recurse | Select-Object -First 1
Select-String -Path "backend\src\**\*.cs" -Pattern "enum.*Target" -Recurse | Select-Object -First 3
```

**Files:**
- Create: `backend/tests/MenuNest.McpServer.UnitTests/Tools/BudgetToolsTests.cs`
- Create: `backend/src/MenuNest.McpServer/Tools/BudgetTools.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// backend/tests/MenuNest.McpServer.UnitTests/Tools/BudgetToolsTests.cs
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccounts;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Application.UseCases.Budget.Accounts.DeleteAccount;
using MenuNest.Application.UseCases.Budget.Transactions.ListTransactions;
using MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;
using MenuNest.Application.UseCases.Budget.Transactions.DeleteTransaction;
using MenuNest.Application.UseCases.Budget.Summary.GetMonthlySummary;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class BudgetToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly BudgetTools _sut;

    public BudgetToolsTests() => _sut = new BudgetTools(_mediator.Object);

    [Fact]
    public async Task get_budget_summary_sends_GetMonthlySummaryQuery_with_year_and_month()
    {
        _mediator
            .Setup(m => m.Send(It.Is<GetMonthlySummaryQuery>(q => q.Year == 2026 && q.Month == 6), It.IsAny<CancellationToken>()))
            .Returns<GetMonthlySummaryQuery, CancellationToken>((_, _) => new ValueTask<MonthlySummaryDto>(default!));

        await _sut.get_budget_summary(2026, 6, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<GetMonthlySummaryQuery>(q => q.Year == 2026 && q.Month == 6), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task list_budget_accounts_sends_ListAccountsQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListAccountsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<BudgetAccountDto>>(new List<BudgetAccountDto>()));

        await _sut.list_budget_accounts(CancellationToken.None);

        _mediator.Verify(m => m.Send(It.IsAny<ListAccountsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_transaction_sends_CreateTransactionCommand_with_required_fields()
    {
        var accountId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);
        _mediator
            .Setup(m => m.Send(
                It.Is<CreateTransactionCommand>(c => c.AccountId == accountId && c.CategoryId == categoryId && c.Amount == 99.99m),
                It.IsAny<CancellationToken>()))
            .Returns<CreateTransactionCommand, CancellationToken>((_, _) => new ValueTask<BudgetTransactionDto>(default!));

        await _sut.create_transaction(accountId, categoryId, 99.99m, date, null, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<CreateTransactionCommand>(c => c.AccountId == accountId && c.Amount == 99.99m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_transaction_sends_DeleteTransactionCommand_with_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteTransactionCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        await _sut.delete_transaction(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<DeleteTransactionCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~BudgetToolsTests"
```

- [ ] **Step 3: Implement BudgetTools.cs**

Read `CreateCategoryCommand.cs` before writing to get the exact target-related parameter names. Replace `MenuNest.Domain.Enums` placeholders with actual namespaces found in the search above.

```csharp
// backend/src/MenuNest.McpServer/Tools/BudgetTools.cs
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccounts;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Application.UseCases.Budget.Accounts.UpdateAccount;
using MenuNest.Application.UseCases.Budget.Accounts.DeleteAccount;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccountTransactions;
using MenuNest.Application.UseCases.Budget.Groups.ListGroups;
using MenuNest.Application.UseCases.Budget.Groups.CreateGroup;
using MenuNest.Application.UseCases.Budget.Groups.UpdateGroup;
using MenuNest.Application.UseCases.Budget.Groups.DeleteGroup;
using MenuNest.Application.UseCases.Budget.Categories.CreateCategory;
using MenuNest.Application.UseCases.Budget.Categories.UpdateCategory;
using MenuNest.Application.UseCases.Budget.Categories.DeleteCategory;
using MenuNest.Application.UseCases.Budget.AssignedAmounts.SetAssignedAmount;
using MenuNest.Application.UseCases.Budget.AssignedAmounts.MoveMoney;
using MenuNest.Application.UseCases.Budget.AssignedAmounts.CoverOverspending;
using MenuNest.Application.UseCases.Budget.Transactions.ListTransactions;
using MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;
using MenuNest.Application.UseCases.Budget.Transactions.UpdateTransaction;
using MenuNest.Application.UseCases.Budget.Transactions.DeleteTransaction;
using MenuNest.Application.UseCases.Budget.Summary.GetMonthlySummary;
using MenuNest.Domain.Enums; // replace with actual BudgetAccountType namespace

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class BudgetTools(IMediator mediator)
{
    // ── Summary ─────────────────────────────────────────────────────────────

    [McpServerTool, Description("Get the monthly budget summary (income, assigned, spent, available by category)")]
    public async Task<MonthlySummaryDto> get_budget_summary(
        [Description("Year, e.g. 2026")] int year,
        [Description("Month 1–12")] int month,
        CancellationToken ct)
        => await mediator.Send(new GetMonthlySummaryQuery(year, month), ct);

    // ── Accounts ────────────────────────────────────────────────────────────

    [McpServerTool, Description("List all budget accounts (Checking, Savings, CreditCard, etc.)")]
    public async Task<IReadOnlyList<BudgetAccountDto>> list_budget_accounts(CancellationToken ct)
        => await mediator.Send(new ListAccountsQuery(), ct);

    [McpServerTool, Description("Create a new budget account")]
    public async Task<BudgetAccountDto> create_budget_account(
        [Description("Account name")] string name,
        [Description("Account type: Checking, Savings, CreditCard, Cash, Investment, or Loan")] BudgetAccountType type,
        [Description("Opening balance")] decimal openingBalance,
        CancellationToken ct)
        => await mediator.Send(new CreateAccountCommand(name, type, openingBalance), ct);

    [McpServerTool, Description("Rename, reorder, close, or adjust balance for an account")]
    public async Task<BudgetAccountDto> update_budget_account(
        [Description("Account ID")] Guid id,
        [Description("New name")] string name,
        [Description("New sort order (optional)")] int? sortOrder,
        [Description("Set to true to close the account")] bool? isClosed,
        [Description("Override the account balance (optional)")] decimal? setBalance,
        CancellationToken ct)
        => await mediator.Send(new UpdateAccountCommand(id, name, sortOrder, isClosed, setBalance), ct);

    [McpServerTool, Description("Delete a budget account")]
    public async Task delete_budget_account(
        [Description("Account ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteAccountCommand(id), ct);

    [McpServerTool, Description("List transactions for a specific account in a given month")]
    public async Task<IReadOnlyList<BudgetTransactionDto>> list_account_transactions(
        [Description("Account ID")] Guid accountId,
        [Description("Year")] int year,
        [Description("Month 1–12")] int month,
        [Description("Pagination offset")] int skip,
        [Description("Page size")] int take,
        CancellationToken ct)
        => await mediator.Send(new ListAccountTransactionsQuery(accountId, year, month, skip, take), ct);

    // ── Groups ───────────────────────────────────────────────────────────────

    [McpServerTool, Description("List all budget groups (e.g. Housing, Food, Transport)")]
    public async Task<IReadOnlyList<BudgetGroupDto>> list_budget_groups(CancellationToken ct)
        => await mediator.Send(new ListGroupsQuery(), ct);

    [McpServerTool, Description("Create a new budget group")]
    public async Task<BudgetGroupDto> create_budget_group(
        [Description("Group name")] string name,
        CancellationToken ct)
        => await mediator.Send(new CreateGroupCommand(name), ct);

    [McpServerTool, Description("Rename or reorder a budget group")]
    public async Task<BudgetGroupDto> update_budget_group(
        [Description("Group ID")] Guid id,
        [Description("New name")] string name,
        [Description("New sort order (optional)")] int? sortOrder,
        CancellationToken ct)
        => await mediator.Send(new UpdateGroupCommand(id, name, sortOrder), ct);

    [McpServerTool, Description("Delete a budget group")]
    public async Task delete_budget_group(
        [Description("Group ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteGroupCommand(id), ct);

    // ── Categories ───────────────────────────────────────────────────────────

    [McpServerTool, Description("Create a budget category within a group")]
    public async Task<BudgetCategoryDto> create_budget_category(
        [Description("Group ID the category belongs to")] Guid groupId,
        [Description("Category name")] string name,
        [Description("Optional emoji (e.g. 🛒)")] string? emoji,
        [Description("Target amount (optional)")] decimal? targetAmount,
        [Description("Target type (optional), e.g. Monthly or Weekly")] string? targetType,
        CancellationToken ct)
        => await mediator.Send(new CreateCategoryCommand(groupId, name, emoji, targetAmount, targetType), ct);

    [McpServerTool, Description("Update a budget category")]
    public async Task<BudgetCategoryDto> update_budget_category(
        [Description("Category ID")] Guid id,
        [Description("Group ID")] Guid groupId,
        [Description("New name")] string name,
        [Description("New emoji (optional)")] string? emoji,
        [Description("New sort order (optional)")] int? sortOrder,
        [Description("New target amount (optional)")] decimal? targetAmount,
        [Description("New target type (optional)")] string? targetType,
        CancellationToken ct)
        => await mediator.Send(new UpdateCategoryCommand(id, groupId, name, emoji, sortOrder, targetAmount, targetType), ct);

    [McpServerTool, Description("Delete a budget category")]
    public async Task delete_budget_category(
        [Description("Category ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteCategoryCommand(id), ct);

    // ── Assigned amounts ─────────────────────────────────────────────────────

    [McpServerTool, Description("Set how much is budgeted for a category in a given month")]
    public async Task set_assigned_amount(
        [Description("Category ID")] Guid categoryId,
        [Description("Year")] int year,
        [Description("Month 1–12")] int month,
        [Description("Amount to budget")] decimal amount,
        CancellationToken ct)
        => await mediator.Send(new SetAssignedAmountCommand(categoryId, year, month, amount), ct);

    [McpServerTool, Description("Move assigned money from one category to another")]
    public async Task move_money(
        [Description("Source category ID")] Guid fromCategoryId,
        [Description("Destination category ID")] Guid toCategoryId,
        [Description("Year")] int year,
        [Description("Month 1–12")] int month,
        [Description("Amount to move")] decimal amount,
        CancellationToken ct)
        => await mediator.Send(new MoveMoneyCommand(fromCategoryId, toCategoryId, year, month, amount), ct);

    [McpServerTool, Description("Cover an overspent category by pulling from another category")]
    public async Task cover_overspending(
        [Description("Overspent category ID")] Guid overspentCategoryId,
        [Description("Category to pull funds from")] Guid fromCategoryId,
        [Description("Year")] int year,
        [Description("Month 1–12")] int month,
        [Description("Amount to cover")] decimal amount,
        CancellationToken ct)
        => await mediator.Send(new CoverOverspendingCommand(overspentCategoryId, fromCategoryId, year, month, amount), ct);

    // ── Transactions ─────────────────────────────────────────────────────────

    [McpServerTool, Description("List transactions for a month, optionally filtered by category")]
    public async Task<IReadOnlyList<BudgetTransactionDto>> list_transactions(
        [Description("Year")] int year,
        [Description("Month 1–12")] int month,
        [Description("Filter by category ID (optional)")] Guid? categoryId,
        CancellationToken ct)
        => await mediator.Send(new ListTransactionsQuery(year, month, categoryId), ct);

    [McpServerTool, Description("Record a new budget transaction")]
    public async Task<BudgetTransactionDto> create_transaction(
        [Description("Account ID")] Guid accountId,
        [Description("Category ID")] Guid categoryId,
        [Description("Amount (positive = income, negative = expense)")] decimal amount,
        [Description("Transaction date")] DateOnly date,
        [Description("Optional notes")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new CreateTransactionCommand(accountId, categoryId, amount, date, notes), ct);

    [McpServerTool, Description("Update an existing transaction")]
    public async Task<BudgetTransactionDto> update_transaction(
        [Description("Transaction ID")] Guid id,
        [Description("Account ID")] Guid accountId,
        [Description("Category ID")] Guid categoryId,
        [Description("Amount")] decimal amount,
        [Description("Date")] DateOnly date,
        [Description("Notes (optional)")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new UpdateTransactionCommand(id, accountId, categoryId, amount, date, notes), ct);

    [McpServerTool, Description("Delete a transaction by ID")]
    public async Task delete_transaction(
        [Description("Transaction ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteTransactionCommand(id), ct);
}
```

**Note on Category target fields:** Read `CreateCategoryCommand.cs` and `UpdateCategoryCommand.cs` to confirm the exact parameter names and types for target amounts. If `targetType` is an enum rather than a string, update the tool signature and find its namespace.

**Note on `BudgetGroupDto` and `BudgetCategoryDto`:** These types should be in `MenuNest.Application.UseCases.Budget` — check for them in the `ListGroups` or `CreateGroup` use-case files if the compiler can't find them. Check also that `UpdateAccountCommand`, `MoveMoneyCommand`, `CoverOverspendingCommand`, and `ListAccountTransactionsQuery` exist under the paths used above; use the IDE's "Go to definition" or `Select-String` if any are missing.

- [ ] **Step 4: Add `.WithTools<BudgetTools>()` to McpServerRegistration.cs (final state)**

```csharp
// backend/src/MenuNest.McpServer/McpServerRegistration.cs
using MenuNest.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace MenuNest.McpServer;

public static class McpServerRegistration
{
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<RecipeTools>()
            .WithTools<IngredientTools>()
            .WithTools<MealPlanTools>()
            .WithTools<StockTools>()
            .WithTools<ShoppingListTools>()
            .WithTools<BudgetTools>();
}
```

- [ ] **Step 5: Run tests — expect PASS**

```powershell
cd backend; dotnet test tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~BudgetToolsTests"
```

Expected: `4 passed, 0 failed`.

- [ ] **Step 6: Full solution build and all tests green**

```powershell
cd backend; dotnet build MenuNest.sln && dotnet test MenuNest.sln
```

Expected: `Build succeeded.` and all test projects pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/BudgetTools.cs backend/src/MenuNest.McpServer/McpServerRegistration.cs backend/tests/MenuNest.McpServer.UnitTests/Tools/BudgetToolsTests.cs
git commit -m "feat(mcp): add BudgetTools (20 tools) — MCP server complete"
```

---

## After Deployment: Connect Claude

In Claude Settings → Integrations → Add MCP Server:

```
URL:   https://menunest-api.azurewebsites.net/mcp
Auth:  OAuth / Microsoft
Scope: api://{AzureAd:ClientId}/access_as_user
```

Replace `{AzureAd:ClientId}` with the value from the App Service's `AzureAd:ClientId` app setting. Claude will open a Microsoft login prompt on first connect and cache the token automatically.

---

## Self-Review Checklist

**Spec coverage:**
- [x] RecipeTools — 5 tools ✓
- [x] IngredientTools — 4 tools ✓
- [x] MealPlanTools — 7 tools ✓ (list, create, update, delete, stock_check, stock_check_batch, cook_batch)
- [x] StockTools — 3 tools ✓
- [x] ShoppingListTools — 10 tools ✓
- [x] BudgetTools — 20 tools ✓ (summary, 5 account ops, 4 group ops, 3 category ops, 3 assigned-amount ops, 4 transaction ops + list_account_transactions)
- [x] OAuth discovery endpoint `/.well-known/oauth-authorization-server` ✓
- [x] `/mcp` route with `RequireAuthorization()` ✓
- [x] Same App Service — no new Azure resource ✓
- [x] Health/migraine out of scope — not included ✓

**Placeholder scan:** No "TBD", "TODO", or "similar to Task N" present. All command/query type names are concrete (compile-time verified by build steps). Uncertain types have explicit "read X to verify" steps with file paths.

**Type consistency:** `RecipeIngredientInput`, `MealSlot`, `ShoppingListStatus`, `BudgetAccountType` used consistently throughout tool signatures and test assertions. The builder chain in `McpServerRegistration.cs` grows incrementally and the final state in Task 8 is the authoritative version.
