# MCP Duplicate-Identity Fix (Personal Microsoft Accounts) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the same human resolve to one `User` (one Entra `oid`) across the web SPA and the MCP OAuth proxy, and translate expected MCP tool exceptions into clean, actionable tool errors.

**Architecture:** The bug is identity divergence: in production the proxy's `EntraClient` authenticates against the organization tenant (`AzureAd__TenantId = subscription().tenantId`) while the SPA uses `/common`. A personal/guest Microsoft account therefore gets a *guest* `oid` on MCP and a *home* `oid` on the web, producing two `User` rows — the MCP one family-less. The fix points the proxy's sign-in authority at `/common` via a dedicated config key (ADR-004), cleans up the orphan row, and adds an MCP call-tool filter that mirrors the web's `ExceptionHandlingMiddleware` classification.

**Tech Stack:** .NET 10, ASP.NET Core, `ModelContextProtocol.AspNetCore` **1.0.0** (verified — the csproj float `0.3.*` currently resolves to 1.0.0), EF Core (Azure SQL), xUnit + FluentAssertions + Moq, Bicep.

---

## Background facts (verified this session)

- `EntraClient` ([backend/src/MenuNest.WebApi/Oauth/EntraClient.cs:12](../../../backend/src/MenuNest.WebApi/Oauth/EntraClient.cs#L12)) is the **only** reader of `AzureAd:TenantId`. The API `Microsoft` JWT scheme hardcodes `.../common/v2.0` separately ([Program.cs:79](../../../backend/src/MenuNest.WebApi/Program.cs#L79)), so changing the proxy's authority does not affect token validation.
- The SPA uses `/common` (`frontend/.env` `VITE_MSAL_AUTHORITY`); SPA + API + proxy share one app registration (`api://e65fd81b-.../access_as_user`), and that registration already signs personal accounts in via `/common` — so `/common` for the proxy needs **no** app-registration change.
- MCP 1.0.0 filter API (verified by reflecting the restored assembly):
  - Register with `IMcpServerBuilder.WithRequestFilters(Action<IMcpRequestFilterBuilder>)`.
  - Inside, `IMcpRequestFilterBuilder.AddCallToolFilter(McpRequestFilter<CallToolRequestParams, CallToolResult>)`.
  - `McpRequestFilter<TParams,TResult>` is `next => handler`; the handler returns `ValueTask<TResult>` and takes `(RequestContext<TParams> context, CancellationToken ct)`.
  - A call-tool filter's `next()` **does** observe exceptions thrown by registered tools (confirmed: the SDK's own `AuthorizationFilterSetup.CheckCallToolFilter` appeared in the original throw stack).
  - `RequestContext<T>` exposes `.Params` (→ `.Name`) and `.Services` (`IServiceProvider`).
  - `CallToolResult { bool IsError; IList<ContentBlock> Content; }`; text content is `TextContentBlock { string Text; }`.
- `Users` table mapping ([UserConfiguration.cs](../../../backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserConfiguration.cs)): table `Users`, columns `ExternalId` (unique), `Email`, nullable `FamilyId`.

---

## Task 1: Proxy brokers sign-in via `/common` (D1 — code + config)

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Oauth/EntraClient.cs:12,20,54`
- Modify: `backend/src/MenuNest.WebApi/appsettings.json:12-19`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/EntraClientTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/EntraClientTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.WebApi.Oauth;
using Microsoft.Extensions.Configuration;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class EntraClientTests
{
    private static EntraClient Build(params (string Key, string Value)[] settings)
    {
        var dict = new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:ClientId"] = "e65fd81b-7a28-439b-a2ea-98734b5b5a36",
            ["AzureAd:ClientSecret"] = "secret",
            ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111", // org tenant — must be ignored
            ["MCP:ServerUrl"] = "https://menunest.azurewebsites.net/mcp",
        };
        foreach (var (k, v) in settings) dict[k] = v;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new EntraClient(new HttpClient(), config);
    }

    [Fact]
    public void Authorize_url_uses_SignInTenant_not_org_TenantId()
    {
        var sut = Build(("AzureAd:SignInTenant", "common"));

        var url = sut.BuildAuthorizeUrl("state123", "challenge123");

        url.Should().Contain("https://login.microsoftonline.com/common/oauth2/v2.0/authorize");
        url.Should().NotContain("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Authorize_url_defaults_to_common_when_SignInTenant_absent()
    {
        var sut = Build(); // no AzureAd:SignInTenant set

        var url = sut.BuildAuthorizeUrl("state123", "challenge123");

        url.Should().Contain("/common/oauth2/v2.0/authorize");
        url.Should().NotContain("11111111-1111-1111-1111-111111111111");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter FullyQualifiedName~EntraClientTests`
Expected: FAIL — `Authorize_url_uses_SignInTenant_not_org_TenantId` finds the org tenant GUID in the URL (current code reads `AzureAd:TenantId`).

- [ ] **Step 3: Change `EntraClient` to read `AzureAd:SignInTenant`**

In `backend/src/MenuNest.WebApi/Oauth/EntraClient.cs`, replace the `Tenant` property (line 12):

```csharp
    private string Instance => config["AzureAd:Instance"]!.TrimEnd('/');
    // ADR-004: broker sign-in via /common (default) so personal/guest Microsoft
    // accounts resolve to their stable home-tenant oid, matching the SPA. This is
    // deliberately decoupled from AzureAd:TenantId (which prod sets to the org tenant).
    private string SignInTenant => config["AzureAd:SignInTenant"] ?? "common";
    private string ClientId => config["AzureAd:ClientId"]!;
```

Then update the two usages from `{Tenant}` to `{SignInTenant}`:

- Line 20 (in `BuildAuthorizeUrl`):
```csharp
        => $"{Instance}/{SignInTenant}/oauth2/v2.0/authorize"
```
- Line 54 (in `PostAsync`):
```csharp
        var url = $"{Instance}/{SignInTenant}/oauth2/v2.0/token";
```

- [ ] **Step 4: Add the default key to `appsettings.json`**

In `backend/src/MenuNest.WebApi/appsettings.json`, add `SignInTenant` to the `AzureAd` block (after `TenantId`):

```json
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "common",
    "SignInTenant": "common",
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "_AudienceComment": "For v2.0 tokens (SPA via MSAL.js) aud = ClientId (GUID only). Use the bare GUID, NOT api://{guid}.",
    "Audience": "00000000-0000-0000-0000-000000000000",
    "ClientSecret": ""
  },
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter FullyQualifiedName~EntraClientTests`
Expected: PASS (both tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.WebApi/Oauth/EntraClient.cs backend/src/MenuNest.WebApi/appsettings.json backend/tests/MenuNest.WebApi.UnitTests/Oauth/EntraClientTests.cs
git commit -m "fix(oauth): proxy brokers sign-in via /common (AzureAd:SignInTenant)" -m "Personal/guest Microsoft accounts got a guest oid via the org tenant on MCP vs a home oid via /common on the web, creating duplicate family-less Users. Decouple the proxy sign-in authority from AzureAd:TenantId; default /common. See ADR-004." -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Set `AzureAd__SignInTenant=common` in IaC (D1 — infra)

**Files:**
- Modify: `infra/modules/app-service.bicep:205-208` (add an app setting next to the other `AzureAd__*` settings)

- [ ] **Step 1: Add the app setting**

In `backend`-adjacent IaC file `infra/modules/app-service.bicep`, inside the `appSettings` array, immediately after the `AzureAd__Instance` entry (currently lines 205-208), add:

```bicep
        {
          name: 'AzureAd__Instance'
          value: azureAdInstance
        }
        {
          // ADR-004: broker MCP sign-in via /common so personal/guest Microsoft
          // accounts resolve to their stable home-tenant oid (matches the SPA).
          name: 'AzureAd__SignInTenant'
          value: 'common'
        }
```

(Leave `AzureAd__TenantId = subscription().tenantId` unchanged — it is no longer read by the proxy but is harmless.)

- [ ] **Step 2: Validate the Bicep compiles**

Run: `az bicep build --file infra/main.bicep --stdout > $null`
Expected: exits 0 with no errors (the module is referenced from `infra/main.bicep`). If `az` is unavailable, run `bicep build infra/main.bicep --stdout`.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/app-service.bicep
git commit -m "fix(oauth): set AzureAd__SignInTenant=common in App Service config" -m "Pins the proxy sign-in authority to /common in production (ADR-004)." -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: MCP boundary translates expected exceptions to clean tool errors (D3)

**Files:**
- Modify: `backend/src/MenuNest.McpServer/MenuNest.McpServer.csproj:10` (pin the MCP package)
- Create: `backend/src/MenuNest.McpServer/McpToolErrorMapper.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs`
- Test: `backend/tests/MenuNest.McpServer.UnitTests/McpToolErrorMapperTests.cs` (create)

- [ ] **Step 1: Pin the MCP package to the verified version**

In `backend/src/MenuNest.McpServer/MenuNest.McpServer.csproj`, change line 10 from the float to the verified version so the filter API is guaranteed:

```xml
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.0.0" />
```

Run: `dotnet restore backend/src/MenuNest.McpServer/MenuNest.McpServer.csproj`
Expected: restores `ModelContextProtocol.AspNetCore 1.0.0` (already in the lock/cache; no version change in practice).

- [ ] **Step 2: Write the failing test**

Create `backend/tests/MenuNest.McpServer.UnitTests/McpToolErrorMapperTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MenuNest.Domain.Exceptions;
using ModelContextProtocol.Protocol;

namespace MenuNest.McpServer.UnitTests;

public class McpToolErrorMapperTests
{
    [Fact]
    public async Task Passes_through_successful_result()
    {
        var ok = new CallToolResult { IsError = false };

        var result = await McpToolErrorMapper.GuardAsync(
            "list_recipes", services: null, () => new ValueTask<CallToolResult>(ok));

        result.Should().BeSameAs(ok);
    }

    [Fact]
    public async Task Translates_DomainException_to_error_result()
    {
        const string message = "You must join or create a family before using this feature.";

        var result = await McpToolErrorMapper.GuardAsync(
            "list_recipes", services: null,
            () => throw new DomainException(message));

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle()
            .Which.Should().BeOfType<TextContentBlock>()
            .Which.Text.Should().Be(message);
    }

    [Fact]
    public async Task Translates_ValidationException_to_error_result()
    {
        var ex = new ValidationException(new[] { new ValidationFailure("Name", "Name is required.") });

        var result = await McpToolErrorMapper.GuardAsync(
            "create_recipe", services: null, () => throw ex);

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>();
    }

    [Fact]
    public async Task Lets_unexpected_exceptions_propagate()
    {
        Func<Task> act = async () => await McpToolErrorMapper.GuardAsync(
            "list_recipes", services: null,
            () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter FullyQualifiedName~McpToolErrorMapperTests`
Expected: FAIL to **compile** — `McpToolErrorMapper` does not exist yet.

- [ ] **Step 4: Create the error mapper**

Create `backend/src/MenuNest.McpServer/McpToolErrorMapper.cs`:

```csharp
using FluentValidation;
using MenuNest.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace MenuNest.McpServer;

/// <summary>
/// MCP-boundary equivalent of the WebApi's ExceptionHandlingMiddleware. Expected
/// domain/validation exceptions thrown by tools are turned into clean, client-facing
/// tool error results and logged at Warning. Unexpected exceptions are left to
/// propagate, so the MCP SDK still records them as errors.
/// </summary>
public static class McpToolErrorMapper
{
    public const string LoggerCategory = "MenuNest.McpServer.ToolErrors";

    public static async ValueTask<CallToolResult> GuardAsync(
        string? toolName,
        IServiceProvider? services,
        Func<ValueTask<CallToolResult>> next)
    {
        try
        {
            return await next();
        }
        catch (DomainException ex)
        {
            return ToErrorResult(toolName, services, ex, ex.Message);
        }
        catch (ValidationException ex)
        {
            return ToErrorResult(toolName, services, ex, ex.Message);
        }
    }

    private static CallToolResult ToErrorResult(
        string? toolName, IServiceProvider? services, Exception ex, string message)
    {
        services?.GetService<ILoggerFactory>()
            ?.CreateLogger(LoggerCategory)
            .LogWarning(ex, "MCP tool {Tool} rejected by a domain/validation rule: {Message}", toolName, message);

        return new CallToolResult
        {
            IsError = true,
            Content = new List<ContentBlock> { new TextContentBlock { Text = message } },
        };
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter FullyQualifiedName~McpToolErrorMapperTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Wire the filter into the MCP server registration**

Replace the body of `backend/src/MenuNest.McpServer/McpServerRegistration.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace MenuNest.McpServer;

public static class McpServerRegistration
{
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<Tools.RecipeTools>()
            .WithTools<Tools.IngredientTools>()
            .WithTools<Tools.MealPlanTools>()
            .WithTools<Tools.StockTools>()
            .WithTools<Tools.ShoppingListTools>()
            .WithTools<Tools.BudgetTools>()
            // Translate expected domain/validation exceptions from tools into clean
            // tool error results (mirrors the WebApi ExceptionHandlingMiddleware). See ADR-004 / spec D3.
            .WithRequestFilters(filters =>
                filters.AddCallToolFilter(next => (context, ct) =>
                    McpToolErrorMapper.GuardAsync(
                        context.Params?.Name,
                        context.Services,
                        () => next(context, ct))));
}
```

- [ ] **Step 7: Build the whole backend to confirm the filter API compiles**

Run: `dotnet build backend/src/MenuNest.McpServer/MenuNest.McpServer.csproj`
Expected: Build succeeded, 0 errors. (Confirms `WithRequestFilters` / `AddCallToolFilter` resolve and the lambda matches `McpRequestFilter<CallToolRequestParams, CallToolResult>`.)

- [ ] **Step 8: Run the full MCP test project (regression)**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj`
Expected: PASS (existing tool tests + the 4 new mapper tests).

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.McpServer/MenuNest.McpServer.csproj backend/src/MenuNest.McpServer/McpToolErrorMapper.cs backend/src/MenuNest.McpServer/McpServerRegistration.cs backend/tests/MenuNest.McpServer.UnitTests/McpToolErrorMapperTests.cs
git commit -m "feat(mcp): translate domain/validation exceptions to clean tool errors" -m "Adds a call-tool filter mirroring the WebApi ExceptionHandlingMiddleware so expected rejections (e.g. no family yet) return an actionable tool error at Warning instead of an unhandled Error. Pins ModelContextProtocol.AspNetCore to 1.0.0." -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Deploy, then verify the identity fix end-to-end (D1 verification)

No code. This task confirms the deployed fix before touching data.

- [ ] **Step 1: Deploy the merged changes** to the `menunest` App Service via the normal pipeline (CI/CD on `main`). Wait for the deployment to complete and the app to restart.

- [ ] **Step 2: Reconnect Claude to MenuNest MCP** using the OAuth flow with `thodsaphonsp@outlook.com`, then call `list_recipes`.
Expected: the family's recipes are returned — **no** "You must join or create a family" error.

- [ ] **Step 3: Confirm no new User row was created on that call** (App Insights, last 1h). The failing call previously did `SELECT`+`INSERT`; a correct call now does only the lookup. Query the App Insights `dependencies` table for the `POST /mcp` operation around your test time and confirm a single SQL call per tool invocation (no second long INSERT call like the pre-fix `7:10:38` event).

- [ ] **Step 4: Web regression check** — log into the SPA with the same account and confirm recipes still list (the web path was already correct; this confirms no collateral change).

Do not proceed to Task 5 until Step 2 passes. If `list_recipes` still throws, the deployed SPA `VITE_MSAL_AUTHORITY` may not be `/common` — confirm that deploy secret before cleaning up data.

---

## Task 5: One-off cleanup of the orphan User row(s) (D2)

No code. Run **after** Task 4 passes, so re-login uses the correct home-oid row and no new orphan is created. Connect to Azure SQL `menunest-sql.database.windows.net`, database `MenuNest` (e.g. via Azure Data Studio / `sqlcmd` with the App Service `DefaultConnection`, or the developer's existing access).

- [ ] **Step 1: VERIFY — list candidate orphans first**

```sql
-- Orphans = family-less Users whose Email matches another User that HAS a family.
SELECT u.Id, u.Email, u.ExternalId, u.FamilyId, u.AuthProvider, u.CreatedAt
FROM   Users u
WHERE  u.FamilyId IS NULL
  AND  EXISTS (SELECT 1 FROM Users o
               WHERE o.Email = u.Email AND o.FamilyId IS NOT NULL);
```

Expected: ~1 row (the `thodsaphonsp@outlook.com` guest-oid row created today). **If the count is more than a small handful, STOP** and re-evaluate (a genuine new user who simply hasn't joined a family yet must not match this query — they won't, because they have no family-bearing twin — but verify before deleting).

- [ ] **Step 2: DELETE the confirmed orphan(s)**

```sql
DELETE u
FROM   Users u
WHERE  u.FamilyId IS NULL
  AND  EXISTS (SELECT 1 FROM Users o
               WHERE o.Email = u.Email AND o.FamilyId IS NOT NULL);
```

Expected: `(1 row affected)` (or the count confirmed in Step 1). These rows are safe to delete: a family-less User can never have created family-scoped data (every write is family-gated).

- [ ] **Step 3: Re-run the Step 1 SELECT**
Expected: 0 rows.

---

## Task 6: Verify the error-mapping behavior in production (D3 verification)

No code. Confirms D3 once deployed (Task 4 already deployed it).

- [ ] **Step 1: Trigger a genuine family-gated rejection over MCP.** Using a Microsoft account that has authenticated but has **no** family yet (or a fresh test account), call any family-gated tool (e.g. `list_recipes`) via Claude.
Expected: the MCP client receives a clean, readable error message ("You must join or create a family before using this feature.") — not a generic "unhandled exception".

- [ ] **Step 2: Confirm severity in App Insights.** Query the `exceptions` / `traces` for that call.
Expected: it is recorded at **Warning** under category `MenuNest.McpServer.ToolErrors` (or simply absent from `exceptions` as an Error) — no `ToolCallError` Error-severity record for the rejection.

---

## Notes / Risks

- **Prod `VITE_MSAL_AUTHORITY`** is a deploy secret not readable here; the fix assumes it is `/common`. Task 4 Step 2 fails loudly if not — confirm the secret during rollout.
- **MCP package float.** Task 3 Step 1 pins `ModelContextProtocol.AspNetCore` to `1.0.0` (the verified API). If a future bump is desired, re-verify `WithRequestFilters`/`AddCallToolFilter` still exist with the same signatures.
- **Out of scope (Phase 2):** automated duplicate-User detection/merge, hardening `UserProvisioner` against identity drift, family create/join tools over MCP, Key Vault / durable OAuth stores (ADR-003).
