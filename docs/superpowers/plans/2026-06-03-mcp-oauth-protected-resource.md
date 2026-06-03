# MCP OAuth Protected-Resource Discovery — Implementation Plan

> **⚠️ SUPERSEDED — DO NOT IMPLEMENT.** This describes an abandoned approach (serve protected-resource metadata pointing `authorization_servers` at the Entra tenant issuer; no proxy). It was abandoned once confirmed that claude.ai sends RFC 8707 `resource=<server URL>` which Entra cannot resolve. The shipped solution is the OAuth proxy — see [ADR-003](../../adr/003-mcp-oauth-proxy.md) and `docs/superpowers/specs/2026-06-03-mcp-oauth-proxy-design.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the deployed MenuNest MCP server connectable from claude.ai by serving the RFC 9728 `/.well-known/oauth-protected-resource` document it requires for OAuth discovery.

**Architecture:** Add one anonymous minimal-API endpoint (two paths) in `MenuNest.WebApi/Program.cs`, mirroring the existing `/.well-known/oauth-authorization-server` endpoint. The document's values are built by a pure, unit-tested helper in `MenuNest.McpServer`; `authorization_servers` points at the **tenant-specific** Entra issuer (the only fully issuer-consistent discovery path — see ADR-002). No change to MCP tools, the auth pipeline, or the meal-planning domain.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, `ModelContextProtocol.AspNetCore`, xUnit + FluentAssertions. Deploy via GitHub Actions (`main_menunest.yml`) on push to `main`.

**Design spec:** [docs/superpowers/specs/2026-06-03-mcp-oauth-protected-resource-design.md](../specs/2026-06-03-mcp-oauth-protected-resource-design.md)

**Already done this session — DO NOT REPEAT:** Azure/Entra config on app reg `e65fd81b-7a28-439b-a2ea-98734b5b5a36` — web redirect URIs (`claude.ai`/`claude.com` callbacks), client secret `claude-mcp-connector`, and the exposed scope `access_as_user`. The code reads tenant/client IDs from `AzureAd` config (prod App Service `menunest` already has the concrete values).

---

## File Structure

| File | Responsibility |
|---|---|
| `backend/src/MenuNest.McpServer/McpOAuthMetadata.cs` | **Create.** Pure helper + record producing the RFC 9728 protected-resource document from config values. Testable without a web host. |
| `backend/tests/MenuNest.McpServer.UnitTests/McpOAuthMetadataTests.cs` | **Create.** Unit tests for the helper's URL/scope composition. |
| `backend/src/MenuNest.WebApi/Program.cs` | **Modify** (after line 193). Map two anonymous GET routes that call the helper. |
| `docs/adr/002-mcp-oauth-protected-resource-discovery.md` | **Create.** Records the RFC 9728 + no-DCR + tenant-issuer decisions. |
| `docs/superpowers/specs/2026-06-02-menunest-mcp-server-design.md` | **Modify.** Fix `menunest-api.azurewebsites.net` → `menunest.azurewebsites.net`. |

**Why no endpoint integration test:** there is no WebApi test project, and booting the app via `WebApplicationFactory` would require a live DB, Entra config, and the `FollowUpDispatcher` hosted service — heavy and brittle for one anonymous route. The value logic (URL/scope composition) is unit-tested in `MenuNest.McpServer.UnitTests`; the wiring/anonymous/200 behavior is verified live in Task 5 (the spec's chosen verification, D4).

---

### Task 1: Write the failing test for the metadata helper

**Files:**
- Test: `backend/tests/MenuNest.McpServer.UnitTests/McpOAuthMetadataTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.McpServer.UnitTests/McpOAuthMetadataTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.McpServer;

namespace MenuNest.McpServer.UnitTests;

public class McpOAuthMetadataTests
{
    private const string Instance = "https://login.microsoftonline.com/";
    private const string Tenant   = "d500e2f4-1325-41d2-9f92-2f2f39e8ea19";
    private const string ClientId = "e65fd81b-7a28-439b-a2ea-98734b5b5a36";
    private const string Resource = "https://menunest.azurewebsites.net/mcp";

    [Fact]
    public void Build_points_authorization_servers_at_tenant_specific_issuer()
    {
        var meta = McpOAuthMetadata.Build(Instance, Tenant, ClientId, Resource);

        meta.authorization_servers.Should().ContainSingle()
            .Which.Should().Be("https://login.microsoftonline.com/d500e2f4-1325-41d2-9f92-2f2f39e8ea19/v2.0");
    }

    [Fact]
    public void Build_advertises_the_access_as_user_scope_for_the_api()
    {
        var meta = McpOAuthMetadata.Build(Instance, Tenant, ClientId, Resource);

        meta.scopes_supported.Should().ContainSingle()
            .Which.Should().Be("api://e65fd81b-7a28-439b-a2ea-98734b5b5a36/access_as_user");
    }

    [Fact]
    public void Build_passes_through_resource_and_sets_header_bearer_method()
    {
        var meta = McpOAuthMetadata.Build(Instance, Tenant, ClientId, Resource);

        meta.resource.Should().Be(Resource);
        meta.bearer_methods_supported.Should().Equal("header");
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/")]   // trailing slash
    [InlineData("https://login.microsoftonline.com")]    // no trailing slash
    public void Build_normalizes_instance_trailing_slash(string instance)
    {
        var meta = McpOAuthMetadata.Build(instance, Tenant, ClientId, Resource);

        meta.authorization_servers[0].Should().Be(
            "https://login.microsoftonline.com/d500e2f4-1325-41d2-9f92-2f2f39e8ea19/v2.0");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (RED)**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~McpOAuthMetadata"`
Expected: **build failure** — `error CS0103: The name 'McpOAuthMetadata' does not exist` (the helper is not implemented yet). This is the expected RED state in C#.

---

### Task 2: Implement the metadata helper (GREEN)

**Files:**
- Create: `backend/src/MenuNest.McpServer/McpOAuthMetadata.cs`

- [ ] **Step 1: Write the minimal implementation**

Create `backend/src/MenuNest.McpServer/McpOAuthMetadata.cs`:

```csharp
namespace MenuNest.McpServer;

/// <summary>
/// RFC 9728 OAuth 2.0 Protected Resource Metadata for the MCP endpoint.
/// Property names are snake_case to match the wire format exactly (System.Text.Json
/// serializes them verbatim). Served anonymously at /.well-known/oauth-protected-resource
/// so MCP clients (claude.ai) can discover the authorization server + required scope.
/// </summary>
public sealed record ProtectedResourceMetadata(
    string resource,
    string[] authorization_servers,
    string[] scopes_supported,
    string[] bearer_methods_supported);

public static class McpOAuthMetadata
{
    /// <summary>
    /// Builds the protected-resource document.
    /// <paramref name="instance"/> is the Entra instance (e.g. "https://login.microsoftonline.com/").
    /// <paramref name="tenantId"/> must be the concrete tenant GUID in prod — pointing the
    /// authorization server at the tenant-specific issuer is the only fully issuer-consistent
    /// discovery path for claude.ai (see ADR-002). <paramref name="resourceUrl"/> is the MCP
    /// endpoint URL, derived from the incoming request so it is environment-agnostic.
    /// </summary>
    public static ProtectedResourceMetadata Build(
        string instance, string tenantId, string clientId, string resourceUrl)
        => new(
            resource: resourceUrl,
            authorization_servers: [$"{instance.TrimEnd('/')}/{tenantId}/v2.0"],
            scopes_supported: [$"api://{clientId}/access_as_user"],
            bearer_methods_supported: ["header"]);
}
```

- [ ] **Step 2: Run the test to verify it passes (GREEN)**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj --filter "FullyQualifiedName~McpOAuthMetadata"`
Expected: **PASS** — 5 tests passed (3 `[Fact]` + 2 `[Theory]` cases).

- [ ] **Step 3: Commit**

```bash
git add backend/src/MenuNest.McpServer/McpOAuthMetadata.cs backend/tests/MenuNest.McpServer.UnitTests/McpOAuthMetadataTests.cs
git commit -m "feat(mcp): add ProtectedResourceMetadata builder (RFC 9728, tenant-specific issuer)"
```

---

### Task 3: Wire the anonymous discovery endpoints into Program.cs

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Program.cs` (insert after line 193, before `app.Run();`)

`using MenuNest.McpServer;` is already present (Program.cs line 4), so `McpOAuthMetadata` is in scope.

- [ ] **Step 1: Add the endpoints**

In `backend/src/MenuNest.WebApi/Program.cs`, find this existing block (ends at line 193):

```csharp
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

Immediately **after** that block (and before `app.Run();`), insert:

```csharp
// OAuth 2.0 Protected Resource Metadata (RFC 9728): claude.ai fetches this FIRST to
// discover the authorization server + required scope before attempting login. Anonymous.
// authorization_servers points at the tenant-specific Entra issuer (concrete GUID in prod)
// — the only fully issuer-consistent discovery path. See ADR-002.
// Served at both the bare well-known path and the resource-suffixed path (RFC 9728 §3.1),
// since clients differ on which they probe.
IResult ProtectedResourceMetadata(HttpContext http)
{
    var azureAd = app.Configuration.GetSection("AzureAd");
    var resourceUrl = $"{http.Request.Scheme}://{http.Request.Host}/mcp";
    return Results.Ok(McpOAuthMetadata.Build(
        azureAd["Instance"]!, azureAd["TenantId"]!, azureAd["ClientId"]!, resourceUrl));
}

app.MapGet("/.well-known/oauth-protected-resource", ProtectedResourceMetadata).AllowAnonymous();
app.MapGet("/.well-known/oauth-protected-resource/mcp", ProtectedResourceMetadata).AllowAnonymous();
```

- [ ] **Step 2: Build the solution to verify it compiles**

Run: `dotnet build backend/MenuNest.sln`
Expected: **Build succeeded.** 0 errors. (A local function among top-level statements is valid C#; it is in scope for the two `MapGet` calls that follow it.)

- [ ] **Step 3: Run the full McpServer test suite to confirm nothing regressed**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests/MenuNest.McpServer.UnitTests.csproj`
Expected: **PASS** — all tests pass (existing tool tests + the 5 new metadata tests).

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.WebApi/Program.cs
git commit -m "feat(mcp): serve /.well-known/oauth-protected-resource for claude.ai discovery"
```

---

### Task 4: Documentation — ADR-002 and the URL typo fix

**Files:**
- Create: `docs/adr/002-mcp-oauth-protected-resource-discovery.md`
- Modify: `docs/superpowers/specs/2026-06-02-menunest-mcp-server-design.md`

- [ ] **Step 1: Create ADR-002**

Create `docs/adr/002-mcp-oauth-protected-resource-discovery.md`:

```markdown
# ADR-002: MCP OAuth uses RFC 9728 protected-resource discovery; DCR deliberately omitted

**Date:** 2026-06-03
**Status:** Accepted
**Supersedes (in part):** ADR-001 (which assumed `/.well-known/oauth-authorization-server` alone was sufficient)

---

## Context

ADR-001 established that the MCP server authenticates via Entra ID OAuth and serves
`/.well-known/oauth-authorization-server`. In practice, **claude.ai follows RFC 9728**:
on connect it first fetches `/.well-known/oauth-protected-resource` to learn (a) which
authorization server to use and (b) which scope to request. That endpoint did not exist,
so the global fallback auth policy returned 401. With no protected-resource metadata,
claude.ai fell back to Dynamic Client Registration (RFC 7591), which Entra ID rejects —
producing the connector error "Couldn't register with MenuNest's sign-in service."

Two further facts constrain the fix (verified by probing live, 2026-06-03):
- Entra does not serve the RFC 8414 `oauth-authorization-server` path; it serves
  `openid-configuration`. The `common` authority returns a **templated** issuer
  (`.../{tenantid}/v2.0`), but the **tenant-specific** authority returns a concrete,
  self-matching issuer.
- The self-hosted `common` metadata cannot satisfy both AS-metadata issuer-matching and
  the RFC 9207 `iss` redirect parameter at once.

## Decision

1. Serve `/.well-known/oauth-protected-resource` (RFC 9728), anonymously, at both the bare
   path and the `/mcp`-suffixed path.
2. Point its `authorization_servers` at the **tenant-specific** Entra issuer
   (`https://login.microsoftonline.com/{tenant}/v2.0`) — the only fully issuer-consistent
   discovery path. Values are composed from `AzureAd` configuration at request time.
3. **Do not implement Dynamic Client Registration.** Connecting clients paste the
   pre-registered Entra Client ID + Secret into the connector's settings once.

## Consequences

**Positive**
- claude.ai (and Claude mobile / Claude Code) can complete OAuth discovery and connect.
- No DCR shim to build, secure, or maintain; no second auth scheme wired into the existing
  Microsoft + Google policy pipeline (the endpoint is a plain anonymous minimal API).
- Issuer is consistent end-to-end (AS metadata, token `iss`, and RFC 9207 `iss` param all
  agree), so no API auth changes are required.

**Negative**
- The MCP discovery advertises a tenant-specific authority, so only Microsoft accounts in
  tenant `d500e2f4-…` can connect Claude (the web app itself still uses `common`). Fine for
  the owner and family added to the tenant; arbitrary outside Microsoft accounts cannot.
- Each new connector must be given the Client ID + Secret manually (accepted trade-off vs a
  DCR shim).
- Depends on the MCP client falling through Entra's 404 `oauth-authorization-server` probe to
  the 200 `openid-configuration` probe (current SDK behavior; verified live before sign-off).
```

- [ ] **Step 2: Fix the URL typo in the 2026-06-02 spec**

In `docs/superpowers/specs/2026-06-02-menunest-mcp-server-design.md`, replace every occurrence of `menunest-api.azurewebsites.net` with `menunest.azurewebsites.net` (the real deployed host; the `-api` form does not resolve).

Use Edit with `replace_all: true`:
- old: `menunest-api.azurewebsites.net`
- new: `menunest.azurewebsites.net`

- [ ] **Step 3: Verify no stale references remain**

Run: `git grep -n "menunest-api" -- docs/` (PowerShell: `git grep -n "menunest-api" -- docs/`)
Expected: **no output** (exit code 1) — all references fixed.

- [ ] **Step 4: Commit**

```bash
git add docs/adr/002-mcp-oauth-protected-resource-discovery.md docs/superpowers/specs/2026-06-02-menunest-mcp-server-design.md
git commit -m "docs(mcp): add ADR-002 (RFC 9728 discovery, no DCR); fix deployed API host typo"
```

---

### Task 5: Deploy and verify (probe-then-reconnect, D4)

**Files:** none — this is deployment + live verification. No success claim until both the probe and the reconnect pass.

- [ ] **Step 1: Push to main to trigger the CI deploy**

```bash
git push origin main
```
Expected: push succeeds; GitHub Actions workflow `main_menunest.yml` starts. Wait for it to finish deploying to App Service `menunest` before probing.

- [ ] **Step 2: Confirm the deploy completed**

Run: `gh run list --workflow=main_menunest.yml --limit 1`
Expected: the latest run shows `completed  success`. (If still in progress, wait and re-run.)

- [ ] **Step 3: Probe the new endpoint (must run via PowerShell — the Bash sandbox blocks outbound network)**

Run (PowerShell):
```powershell
$r = Invoke-WebRequest -Uri "https://menunest.azurewebsites.net/.well-known/oauth-protected-resource" -UseBasicParsing -TimeoutSec 40
$j = $r.Content | ConvertFrom-Json
"HTTP $([int]$r.StatusCode)"
"resource              = $($j.resource)"
"authorization_servers = $($j.authorization_servers -join ', ')"
"scopes_supported      = $($j.scopes_supported -join ', ')"
```
Expected:
- `HTTP 200`
- `resource              = https://menunest.azurewebsites.net/mcp`
- `authorization_servers = https://login.microsoftonline.com/d500e2f4-1325-41d2-9f92-2f2f39e8ea19/v2.0`
- `scopes_supported      = api://e65fd81b-7a28-439b-a2ea-98734b5b5a36/access_as_user`

(If this returns 401, the deploy has not finished or the route lacks `.AllowAnonymous()` — recheck Task 3.)

- [ ] **Step 4: Confirm the discovery chain resolves at Entra**

Run (PowerShell):
```powershell
$as = "https://login.microsoftonline.com/d500e2f4-1325-41d2-9f92-2f2f39e8ea19/v2.0"
$o = Invoke-WebRequest -Uri "$as/.well-known/openid-configuration" -UseBasicParsing -TimeoutSec 40
$cfg = $o.Content | ConvertFrom-Json
"issuer_matches        = $($cfg.issuer -eq $as)"
"has_authorize_endpoint= $([bool]$cfg.authorization_endpoint)"
"has_token_endpoint    = $([bool]$cfg.token_endpoint)"
```
Expected: all three print `True` (issuer equals `$as`; both endpoints present).

- [ ] **Step 5: Reconnect on claude.ai (manual)**

In claude.ai → the MenuNest custom connector:
- URL: `https://menunest.azurewebsites.net/mcp`
- OAuth Client ID: `e65fd81b-7a28-439b-a2ea-98734b5b5a36`
- OAuth Client Secret: the `claude-mcp-connector` value created this session

Click **Connect** → complete the Microsoft login → connector reaches **Connected**, the tool list (Recipe / Ingredient / MealPlan / Stock / ShoppingList / Budget) appears, and one read-only tool call (e.g. `list_ingredients`) succeeds.

- [ ] **Step 6: Record the outcome**

If both the probe (Steps 3–4) and the reconnect (Step 5) pass — the work is complete.
If the reconnect fails after a successful probe, apply the spec's fallback table (login OK but `/mcp` 401 → inspect the presented token's `aud`/`scp`; AS metadata not discovered → switch to the origin-based self-hosted fallback). Do not claim success until Step 5 passes.

---

## Self-Review

**Spec coverage:**
- Core change — anonymous `/.well-known/oauth-protected-resource[/mcp]` endpoint → Tasks 1–3. ✓
- `authorization_servers` = tenant-specific Entra issuer (D3) → Task 2 helper + Task 1 test. ✓
- Values from `AzureAd` config + request host → Task 3. ✓
- No DCR, hand-rolled (D1, D2) → Task 3 (plain anonymous endpoint, no SDK `McpAuth`). ✓
- ADR-002 + URL typo fix (D5) → Task 4. ✓
- Probe-then-reconnect verification (D4) → Task 5. ✓
- Azure-side work not repeated → called out in header; Task 5 reuses the existing secret. ✓

**Placeholder scan:** No TBD/TODO/"add error handling" placeholders; every code/command step shows the actual content. ✓

**Type consistency:** `McpOAuthMetadata.Build(string, string, string, string)` and the `ProtectedResourceMetadata` record (props `resource`, `authorization_servers`, `scopes_supported`, `bearer_methods_supported`) are used identically across the test (Task 1), the implementation (Task 2), and the endpoint (Task 3). ✓
