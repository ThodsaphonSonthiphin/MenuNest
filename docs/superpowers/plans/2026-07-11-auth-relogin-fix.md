# Auth Re-login Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the SPA forcing a full re-login every ~1h (persist the browser token cache) and stop claude.ai re-authorizing `/mcp` frequently (persist the MCP proxy refresh state in the existing SQL DB).

**Architecture:** Two independent surfaces, one root cause each — an *ephemeral* session container. (A) The SPA MSAL + Google token cache moves from `sessionStorage` to `localStorage` so it survives mobile tab/process eviction. (C) The MCP OAuth proxy's DCR registrations and refresh-code→Entra-RT map move from volatile `IMemoryCache` to two new tables in the existing `MenuNest` SQL DB; the 1h access JWT is unchanged.

**Tech Stack:** React + MSAL.js (`@azure/msal-browser` v5) + `@react-oauth/google` (frontend); ASP.NET Core minimal APIs + EF Core (SQL Server) (backend); Vitest (frontend tests), xUnit + EF Core SQLite (backend tests).

## Global Constraints

- Decisions are locked in **ADR-036** (`docs/adr/036-spa-token-cache-persist-localstorage.md`) and **ADR-037** (`docs/adr/037-mcp-proxy-durable-refresh-sql.md`). Do not re-open them.
- The MCP **access JWT stays 1 hour** (`OAuthJwt.Mint` default `3600`, `expires_in = 3600`). Do NOT introduce a long-lived JWT (C1 was rejected).
- Durable store = the **existing** `MenuNest` SQL DB via `AppDbContext`. **No new Azure resource, no new database.**
- Persist **only** DCR client registrations + refresh codes. Authorization codes (60s) and PKCE flow state (10min) **stay in `IMemoryCache`**.
- Entra refresh token at rest relies on **Azure SQL TDE** — no app-level encryption (deferred).
- **Migrations are applied to prod MANUALLY** (CLAUDE.md). A new migration is worthless until `dotnet ef database update` is run against prod — Task 6 is mandatory, not optional.
- **Commits:** the `frontend/.husky/pre-commit` hook runs the full backend build+test and frontend `tsc`+build on every commit (~40s+) — expect the wait, do not `--no-verify`. Always `git add <explicit paths>` — never `git add -A`/`.`. Every commit subject references the ticket (`(#<n>)` or `(closes #<n>)`).
- **Non-goals:** Google's ~1h no-refresh ceiling when a tab is kept open (B); app-level RT encryption; Azure Table variant; any change to the SPA Entra/Google validation schemes or the `/common` sign-in authority (ADR-004 invariant).

---

## File Structure

**Frontend (Task 1):**
- Modify `frontend/src/shared/auth/msalConfig.ts` — cache config.
- Modify `frontend/src/shared/auth/googleAuth.ts` — `localStorage` for the Google token.
- Modify `frontend/src/shared/auth/googleAuth.test.ts` — stub `localStorage`.

**Backend (Tasks 2–5):**
- Create `backend/src/MenuNest.Domain/Entities/OAuthClient.cs`, `OAuthRefreshToken.cs`.
- Create `backend/src/MenuNest.Infrastructure/Persistence/Configurations/OAuthClientConfiguration.cs`, `OAuthRefreshTokenConfiguration.cs`.
- Modify `AppDbContext.cs`, `IApplicationDbContext.cs`, `SqliteAppDbContext.cs` — add the two DbSets.
- Add EF migration under `backend/src/MenuNest.Infrastructure/Persistence/Migrations/`.
- Rewrite `backend/src/MenuNest.WebApi/Oauth/Stores.cs` — `ClientStore` + `TokenStore` refresh path become async/SQL.
- Modify `backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs` — async call sites.
- Modify `backend/src/MenuNest.WebApi/Program.cs` — `ClientStore`/`TokenStore` become `Scoped`.
- Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthStoresPersistenceTests.cs` (+ a ProjectReference to reuse the SQLite harness).

---

### Task 1: SPA session persistence (localStorage)

**Files:**
- Modify: `frontend/src/shared/auth/msalConfig.ts:20-22`
- Modify: `frontend/src/shared/auth/googleAuth.ts:20-39`
- Test: `frontend/src/shared/auth/googleAuth.test.ts`

**Interfaces:**
- Consumes: nothing (leaf change).
- Produces: no signature changes — `getGoogleToken`/`setGoogleToken`/`clearGoogleToken` keep their shapes; only the backing store changes from `sessionStorage` to `localStorage`.

- [ ] **Step 1: Update the tests to expect `localStorage`**

In `frontend/src/shared/auth/googleAuth.test.ts`, replace every `vi.stubGlobal('sessionStorage', ...)` with `localStorage`, and add a `setGoogleToken` test. The three `getGoogleToken` tests become:

```typescript
import {clearGoogleToken, decodeGoogleIdToken, getGoogleToken, isGoogleTokenExpired, setGoogleToken} from './googleAuth'

describe('getGoogleToken', () => {
  it('returns the stored token when it is still valid', () => {
    const token = makeToken({sub: '1', exp: futureExp()})
    const removeItem = vi.fn()
    vi.stubGlobal('localStorage', {getItem: vi.fn(() => token), setItem: vi.fn(), removeItem})
    expect(getGoogleToken()).toBe(token)
    expect(removeItem).not.toHaveBeenCalled()
  })

  it('drops the token and returns null when it has expired', () => {
    const token = makeToken({sub: '1', exp: pastExp()})
    const removeItem = vi.fn()
    vi.stubGlobal('localStorage', {getItem: vi.fn(() => token), setItem: vi.fn(), removeItem})
    expect(getGoogleToken()).toBeNull()
    expect(removeItem).toHaveBeenCalledWith('google_id_token')
  })

  it('returns null when nothing is stored', () => {
    vi.stubGlobal('localStorage', {getItem: vi.fn(() => null), setItem: vi.fn(), removeItem: vi.fn()})
    expect(getGoogleToken()).toBeNull()
  })
})

describe('setGoogleToken / clearGoogleToken', () => {
  it('writes and removes the token in localStorage', () => {
    const setItem = vi.fn()
    const removeItem = vi.fn()
    vi.stubGlobal('localStorage', {getItem: vi.fn(), setItem, removeItem})
    setGoogleToken('abc')
    expect(setItem).toHaveBeenCalledWith('google_id_token', 'abc')
    clearGoogleToken()
    expect(removeItem).toHaveBeenCalledWith('google_id_token')
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/shared/auth/googleAuth.test.ts`
Expected: FAIL — the new `localStorage` stub is not read (code still calls `sessionStorage`), so `getGoogleToken` returns `null`/does not observe the stub.

- [ ] **Step 3: Switch `googleAuth.ts` to `localStorage`**

In `frontend/src/shared/auth/googleAuth.ts`, change the storage calls (lines 21, 27, 34, 38) from `sessionStorage` to `localStorage`:

```typescript
export function getGoogleToken(): string | null {
  const token = localStorage.getItem(GOOGLE_TOKEN_KEY)
  if (!token) return null
  if (isGoogleTokenExpired(token)) {
    localStorage.removeItem(GOOGLE_TOKEN_KEY)
    return null
  }
  return token
}

export function setGoogleToken(token: string): void {
  localStorage.setItem(GOOGLE_TOKEN_KEY, token)
}

export function clearGoogleToken(): void {
  localStorage.removeItem(GOOGLE_TOKEN_KEY)
}
```

(Leave `isGoogleTokenExpired`, `decodeGoogleIdToken`, `isGoogleAuthenticated`, and the `EXPIRY_LEEWAY_SECONDS` comment unchanged.)

- [ ] **Step 4: Switch the MSAL cache to `localStorage` + auth-state cookie**

In `frontend/src/shared/auth/msalConfig.ts`, replace the `cache` block (lines 20-22):

```typescript
  cache: {
    cacheLocation: 'localStorage',
    storeAuthStateInCookie: true,
  },
```

- [ ] **Step 5: Run tests + typecheck**

Run: `cd frontend && npx vitest run src/shared/auth/googleAuth.test.ts && npx tsc --noEmit`
Expected: PASS (all googleAuth tests green) and no type errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/auth/msalConfig.ts frontend/src/shared/auth/googleAuth.ts frontend/src/shared/auth/googleAuth.test.ts
git commit -m "fix(auth): persist SPA token cache in localStorage so mobile sessions survive tab eviction (#<n>)"
```

---

### Task 2: MCP durable-store entities, EF config, and DbContext wiring

**Files:**
- Create: `backend/src/MenuNest.Domain/Entities/OAuthClient.cs`
- Create: `backend/src/MenuNest.Domain/Entities/OAuthRefreshToken.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/OAuthClientConfiguration.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/OAuthRefreshTokenConfiguration.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs:53`
- Modify: `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs:47`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs:54`
- Test: `backend/tests/MenuNest.Application.UnitTests/Persistence/OAuthEntityPersistenceTests.cs`

**Interfaces:**
- Produces (relied on by Tasks 4–5):
  - `OAuthClient { string ClientId; string ClientName; string RedirectUrisJson; string? Scope; DateTime ExpiresAt; }`
  - `OAuthRefreshToken { string RefreshCode; string EntraRefreshToken; string Subject; DateTime ExpiresAt; DateTime CreatedAt; }`
  - `IApplicationDbContext.OAuthClients : DbSet<OAuthClient>`, `IApplicationDbContext.OAuthRefreshTokens : DbSet<OAuthRefreshToken>`

- [ ] **Step 1: Create the two entities**

`backend/src/MenuNest.Domain/Entities/OAuthClient.cs`:

```csharp
namespace MenuNest.Domain.Entities;

/// <summary>
/// A DCR-registered MCP client (RFC 7591). Keyed by the opaque client_id we
/// hand back to claude.ai. Durable so a client survives an App Service restart
/// (see ADR-037). Not a domain <c>Entity</c> — its identity is the opaque id.
/// </summary>
public sealed class OAuthClient
{
    public string ClientId { get; set; } = null!;
    public string ClientName { get; set; } = null!;
    /// <summary>JSON-serialised string[] of allowed redirect URIs.</summary>
    public string RedirectUrisJson { get; set; } = null!;
    public string? Scope { get; set; }
    public DateTime ExpiresAt { get; set; }
}
```

`backend/src/MenuNest.Domain/Entities/OAuthRefreshToken.cs`:

```csharp
namespace MenuNest.Domain.Entities;

/// <summary>
/// Maps our opaque refresh code (held by claude.ai) to the stored Entra
/// refresh token. Single-use: rotated on every refresh (see ADR-037). The
/// Entra RT is protected at rest by Azure SQL TDE (no app-level encryption).
/// </summary>
public sealed class OAuthRefreshToken
{
    public string RefreshCode { get; set; } = null!;
    public string EntraRefreshToken { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configurations**

`backend/src/MenuNest.Infrastructure/Persistence/Configurations/OAuthClientConfiguration.cs`:

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    public void Configure(EntityTypeBuilder<OAuthClient> builder)
    {
        builder.ToTable("OAuthClients");
        builder.HasKey(c => c.ClientId);
        builder.Property(c => c.ClientId).ValueGeneratedNever().HasMaxLength(64);
        builder.Property(c => c.ClientName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.RedirectUrisJson).IsRequired();
        builder.Property(c => c.Scope);
        builder.Property(c => c.ExpiresAt).IsRequired();
    }
}
```

`backend/src/MenuNest.Infrastructure/Persistence/Configurations/OAuthRefreshTokenConfiguration.cs`:

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class OAuthRefreshTokenConfiguration : IEntityTypeConfiguration<OAuthRefreshToken>
{
    public void Configure(EntityTypeBuilder<OAuthRefreshToken> builder)
    {
        builder.ToTable("OAuthRefreshTokens");
        builder.HasKey(r => r.RefreshCode);
        builder.Property(r => r.RefreshCode).ValueGeneratedNever().HasMaxLength(128);
        builder.Property(r => r.EntraRefreshToken).IsRequired();
        builder.Property(r => r.Subject).IsRequired().HasMaxLength(128);
        builder.Property(r => r.ExpiresAt).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
    }
}
```

- [ ] **Step 3: Add the DbSets to all three contexts**

Add these three lines after the `Stops` DbSet ("Trip Planner module" block) in EACH context.

In `AppDbContext.cs` (after line 53) and `SqliteAppDbContext.cs` (after line 54):

```csharp
    // MCP OAuth proxy durable store (ADR-037)
    public DbSet<OAuthClient> OAuthClients => Set<OAuthClient>();
    public DbSet<OAuthRefreshToken> OAuthRefreshTokens => Set<OAuthRefreshToken>();
```

In `IApplicationDbContext.cs` (after line 47):

```csharp
    // MCP OAuth proxy durable store (ADR-037)
    DbSet<OAuthClient> OAuthClients { get; }
    DbSet<OAuthRefreshToken> OAuthRefreshTokens { get; }
```

- [ ] **Step 4: Write a round-trip persistence test**

`backend/tests/MenuNest.Application.UnitTests/Persistence/OAuthEntityPersistenceTests.cs`:

```csharp
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Persistence;

public sealed class OAuthEntityPersistenceTests
{
    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task OAuthClient_and_RefreshToken_round_trip()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);

        db.OAuthClients.Add(new OAuthClient
        {
            ClientId = "cid1", ClientName = "claude", RedirectUrisJson = "[\"https://x/cb\"]",
            Scope = "openid", ExpiresAt = DateTime.UtcNow.AddDays(365),
        });
        db.OAuthRefreshTokens.Add(new OAuthRefreshToken
        {
            RefreshCode = "rc1", EntraRefreshToken = "entra-rt", Subject = "oid-1",
            ExpiresAt = DateTime.UtcNow.AddDays(365), CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var client = await db.OAuthClients.FindAsync("cid1");
        var rt = await db.OAuthRefreshTokens.FindAsync("rc1");
        Assert.NotNull(client);
        Assert.Equal("claude", client!.ClientName);
        Assert.NotNull(rt);
        Assert.Equal("entra-rt", rt!.EntraRefreshToken);
    }
}
```

- [ ] **Step 5: Run the test**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter OAuthEntityPersistenceTests`
Expected: PASS — both rows persist and re-read.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/OAuthClient.cs backend/src/MenuNest.Domain/Entities/OAuthRefreshToken.cs backend/src/MenuNest.Infrastructure/Persistence/Configurations/OAuthClientConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/Configurations/OAuthRefreshTokenConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs backend/tests/MenuNest.Application.UnitTests/Persistence/OAuthEntityPersistenceTests.cs
git commit -m "feat(auth): OAuthClient + OAuthRefreshToken entities for durable MCP proxy store (#<n>)"
```

---

### Task 3: Generate the EF migration

**Files:**
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_OAuthDurableStores.cs` (generated)
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs` (generated)

**Interfaces:** none (generated artifact).

- [ ] **Step 1: Add the migration**

Run:
```bash
cd backend
dotnet ef migrations add OAuthDurableStores \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

- [ ] **Step 2: Verify the migration creates exactly the two tables**

Open the generated `..._OAuthDurableStores.cs` and confirm `Up()` contains `migrationBuilder.CreateTable(name: "OAuthClients", ...)` and `CreateTable(name: "OAuthRefreshTokens", ...)` with the string primary keys, and touches no other table.
Expected: exactly two `CreateTable` calls, nothing else.

- [ ] **Step 3: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/Persistence/Migrations/
git commit -m "feat(auth): EF migration for OAuth durable stores (#<n>)"
```

---

### Task 4: SQL-backed ClientStore

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Oauth/Stores.cs:1-22` (usings + `ClientStore`)
- Modify: `backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs:24-40, 43-62` (register + authorize handlers)
- Modify: `backend/src/MenuNest.WebApi/Program.cs:32` (Scoped)
- Modify: `backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj` (ProjectReference to the SQLite harness)
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthStoresPersistenceTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext.OAuthClients` (Task 2).
- Produces (relied on by OAuthEndpoints): `ClientStore(IApplicationDbContext db)` with
  `Task<string> RegisterAsync(string name, string[] redirectUris, string? scope, CancellationToken ct = default)` and
  `Task<ClientRegistration?> GetAsync(string clientId, CancellationToken ct = default)`. (The `ClientRegistration` record is unchanged.)

- [ ] **Step 1: Add the ProjectReference so the test can reuse `SqliteAppDbContext`**

In `backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj`, inside the existing `<ItemGroup>` that holds `<ProjectReference>` entries, add:

```xml
    <ProjectReference Include="..\MenuNest.Application.UnitTests\MenuNest.Application.UnitTests.csproj" />
```

- [ ] **Step 2: Write the failing ClientStore test**

`backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthStoresPersistenceTests.cs`:

```csharp
using MenuNest.Application.UnitTests.Support;
using MenuNest.WebApi.Oauth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class OAuthStoresPersistenceTests
{
    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Client_registration_survives_a_new_dbcontext()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        string clientId;
        using (var db = NewDb(conn))
            clientId = await new ClientStore(db).RegisterAsync("claude", new[] {"https://x/cb"}, "openid");

        using var db2 = NewDb(conn); // simulate an App Service restart: fresh context, same store
        var reg = await new ClientStore(db2).GetAsync(clientId);
        Assert.NotNull(reg);
        Assert.Contains("https://x/cb", reg!.RedirectUris);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.WebApi.UnitTests --filter OAuthStoresPersistenceTests`
Expected: FAIL to compile — `ClientStore` still takes `IMemoryCache` and has no `RegisterAsync`/`GetAsync`.

- [ ] **Step 4: Rewrite `ClientStore` (SQL, async)**

In `backend/src/MenuNest.WebApi/Oauth/Stores.cs`, replace the top `using` line with (keep the memory using — PkceStateStore + auth-codes still need it):

```csharp
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
```

Replace the `ClientStore` class (lines 9-22):

```csharp
/// <summary>DCR client registrations. Durable in SQL (ADR-037).</summary>
public sealed class ClientStore(IApplicationDbContext db)
{
    public async Task<string> RegisterAsync(string name, string[] redirectUris, string? scope, CancellationToken ct = default)
    {
        var clientId = TokenUtil.Opaque(16);
        db.OAuthClients.Add(new OAuthClient
        {
            ClientId = clientId,
            ClientName = name,
            RedirectUrisJson = JsonSerializer.Serialize(redirectUris),
            Scope = scope,
            ExpiresAt = DateTime.UtcNow.AddDays(365),
        });
        await db.SaveChangesAsync(ct);
        return clientId;
    }

    public async Task<ClientRegistration?> GetAsync(string clientId, CancellationToken ct = default)
    {
        var row = await db.OAuthClients
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.ExpiresAt > DateTime.UtcNow, ct);
        return row is null
            ? null
            : new ClientRegistration(row.ClientId, JsonSerializer.Deserialize<string[]>(row.RedirectUrisJson) ?? Array.Empty<string>());
    }
}
```

- [ ] **Step 5: Update the register + authorize endpoints to `async`**

In `OAuthEndpoints.cs`, make the `/oauth/register` handler async and await `RegisterAsync` (change `(DcrRequest body, ClientStore clients) =>` to `async (...)` and line 30 to `await`):

```csharp
        app.MapPost("/oauth/register", async (DcrRequest body, ClientStore clients) =>
        {
            var uris = body.redirect_uris ?? Array.Empty<string>();
            if (uris.Length == 0 || uris.Any(u => !RedirectAllowlist.IsAllowed(u)))
                return Results.BadRequest(new { error = "invalid_redirect_uri" });

            var id = await clients.RegisterAsync(body.client_name ?? "mcp-client", uris, body.scope);
            return Results.Created((string?)null, new
            {
                client_id = id,
                client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                token_endpoint_auth_method = "none",
                redirect_uris = uris,
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" },
            });
        }).AllowAnonymous();
```

Make the `/oauth/authorize` handler async and replace the `TryGet` (lines 43-53 header + first check):

```csharp
        app.MapGet("/oauth/authorize", async (
            [FromQuery] string client_id,
            [FromQuery] string redirect_uri,
            [FromQuery] string code_challenge,
            [FromQuery] string? code_challenge_method,
            [FromQuery] string? state,
            [FromQuery] string? scope,
            ClientStore clients, PkceStateStore flows, EntraClient entra) =>
        {
            var reg = await clients.GetAsync(client_id);
            if (reg is null || !reg.RedirectUris.Contains(redirect_uri))
                return Results.BadRequest(new { error = "invalid_client" });
```

(Leave the rest of the authorize body — allowlist check, S256 check, `flows.Save`, redirect — unchanged.)

- [ ] **Step 6: Make `ClientStore` Scoped**

In `Program.cs` line 32, change `AddSingleton` to `AddScoped`:

```csharp
builder.Services.AddScoped<MenuNest.WebApi.Oauth.ClientStore>();
```

- [ ] **Step 7: Run tests**

Run: `cd backend && dotnet test tests/MenuNest.WebApi.UnitTests --filter OAuthStoresPersistenceTests`
Expected: PASS — the registration survives a fresh context.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.WebApi/Oauth/Stores.cs backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs backend/src/MenuNest.WebApi/Program.cs backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthStoresPersistenceTests.cs
git commit -m "feat(auth): durable SQL-backed ClientStore for MCP DCR registrations (#<n>)"
```

---

### Task 5: SQL-backed refresh codes (TokenStore) + async token endpoint

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Oauth/Stores.cs:41-69` (`TokenStore`)
- Modify: `backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs:93-145` (token endpoint + `IssueTokens`)
- Modify: `backend/src/MenuNest.WebApi/Program.cs:34` (Scoped)
- Test: append to `backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthStoresPersistenceTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext.OAuthRefreshTokens` (Task 2).
- Produces: `TokenStore(IMemoryCache cache, IApplicationDbContext db)` — unchanged sync `SaveAuthCode`/`TakeAuthCode`, plus
  `Task<string> SaveRefreshAsync(string entraRefreshToken, string subject, CancellationToken ct = default)` and
  `Task<string?> TakeRefreshAsync(string refreshCode, CancellationToken ct = default)` (single-use: deletes the row).

- [ ] **Step 1: Write the failing refresh rotation test**

Append to `OAuthStoresPersistenceTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task Refresh_code_is_single_use_and_survives_restart()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

        string code;
        using (var db = NewDb(conn))
            code = await new TokenStore(cache, db).SaveRefreshAsync("entra-rt", "oid-1");

        // survives a "restart" (fresh context) and returns the stored RT once
        using (var db2 = NewDb(conn))
        {
            var rt = await new TokenStore(cache, db2).TakeRefreshAsync(code);
            Assert.Equal("entra-rt", rt);
        }
        // second use of the same code fails (single-use rotation)
        using (var db3 = NewDb(conn))
        {
            var again = await new TokenStore(cache, db3).TakeRefreshAsync(code);
            Assert.Null(again);
        }
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.WebApi.UnitTests --filter OAuthStoresPersistenceTests`
Expected: FAIL to compile — `TokenStore` still takes only `IMemoryCache`; no `SaveRefreshAsync`/`TakeRefreshAsync`.

- [ ] **Step 3: Rewrite `TokenStore` (auth codes in-memory, refresh in SQL)**

In `Stores.cs`, replace the `TokenStore` class (lines 41-69):

```csharp
/// <summary>Proxy auth codes (60s, in-memory) and durable refresh codes → Entra RT (SQL, ADR-037).</summary>
public sealed class TokenStore(IMemoryCache cache, IApplicationDbContext db)
{
    public string SaveAuthCode(AuthCodeData data)
    {
        var code = TokenUtil.Opaque();
        cache.Set($"code:{code}", data, TimeSpan.FromSeconds(60));
        return code;
    }

    public AuthCodeData? TakeAuthCode(string code)
    {
        if (cache.TryGetValue($"code:{code}", out AuthCodeData? d)) { cache.Remove($"code:{code}"); return d; }
        return null;
    }

    public async Task<string> SaveRefreshAsync(string entraRefreshToken, string subject, CancellationToken ct = default)
    {
        var refreshCode = TokenUtil.Opaque();
        db.OAuthRefreshTokens.Add(new OAuthRefreshToken
        {
            RefreshCode = refreshCode,
            EntraRefreshToken = entraRefreshToken,
            Subject = subject,
            ExpiresAt = DateTime.UtcNow.AddDays(365),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return refreshCode;
    }

    public async Task<string?> TakeRefreshAsync(string refreshCode, CancellationToken ct = default)
    {
        var row = await db.OAuthRefreshTokens.FirstOrDefaultAsync(r => r.RefreshCode == refreshCode, ct);
        if (row is null || row.ExpiresAt <= DateTime.UtcNow) return null;
        db.OAuthRefreshTokens.Remove(row); // single-use; a fresh code is minted by SaveRefreshAsync
        await db.SaveChangesAsync(ct);
        return row.EntraRefreshToken;
    }
}
```

- [ ] **Step 4: Make `IssueTokens` async and update the token endpoint**

In `OAuthEndpoints.cs`, replace the `authorization_code` return (line 108), the `refresh_token` block (lines 111-122), and the `IssueTokens` helper (lines 128-145):

```csharp
                return Results.Ok(await IssueTokensAsync(jwt, tokens, data.Subject, form["client_id"].ToString(), data.Scope, data.Name, data.Email, data.EntraRefreshToken, ct));
            }

            if (grant == "refresh_token")
            {
                var refreshCode = form["refresh_token"].ToString();
                var entraRt = await tokens.TakeRefreshAsync(refreshCode, ct);
                if (entraRt is null) return Results.BadRequest(new { error = "invalid_grant" });

                var refreshed = await entra.RefreshAsync(entraRt, ct);
                var newEntraRt = refreshed.RefreshToken ?? entraRt;
                var id = refreshed.IdToken is not null ? ClaimExtractor.FromIdToken(refreshed.IdToken) : null;
                if (id is null) return Results.BadRequest(new { error = "invalid_grant", error_description = "no id_token on refresh" });
                return Results.Ok(await IssueTokensAsync(jwt, tokens, id.Oid, form["client_id"].ToString(), "", id.Name, id.Email, newEntraRt, ct));
            }

            return Results.BadRequest(new { error = "unsupported_grant_type" });
        }).AllowAnonymous();
    }

    private static async Task<object> IssueTokensAsync(OAuthJwt jwt, TokenStore tokens, string subject, string clientId,
        string scope, string? name, string? email, string entraRefreshToken, CancellationToken ct)
    {
        var extra = new List<System.Security.Claims.Claim>();
        if (name is not null) extra.Add(new("name", name));
        if (email is not null) { extra.Add(new("email", email)); extra.Add(new("preferred_username", email)); }

        var accessToken = jwt.Mint(subject, clientId, scope, extra);
        var refreshCode = await tokens.SaveRefreshAsync(entraRefreshToken, subject, ct);
        return new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = refreshCode,
            scope,
        };
    }
```

(The `/oauth/callback` handler still calls the unchanged sync `tokens.SaveAuthCode(...)` — no change there.)

- [ ] **Step 5: Make `TokenStore` Scoped**

In `Program.cs` line 34, change `AddSingleton` to `AddScoped`:

```csharp
builder.Services.AddScoped<MenuNest.WebApi.Oauth.TokenStore>();
```

- [ ] **Step 6: Run the store tests**

Run: `cd backend && dotnet test tests/MenuNest.WebApi.UnitTests --filter OAuthStoresPersistenceTests`
Expected: PASS — refresh survives a fresh context and is single-use.

- [ ] **Step 7: Full backend build + test (the pre-commit hook runs this anyway)**

Run: `cd backend && dotnet build -c Release && dotnet test -c Release`
Expected: solution builds; all tests pass.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.WebApi/Oauth/Stores.cs backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs backend/src/MenuNest.WebApi/Program.cs backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthStoresPersistenceTests.cs
git commit -m "feat(auth): durable SQL-backed MCP refresh tokens (single-use rotation) (#<n>)"
```

---

### Task 6: Apply the migration to prod + verify (MANDATORY — not code)

**Files:** none. This is the deploy/ops step the codebase requires by hand (CLAUDE.md).

- [ ] **Step 1: Preview the SQL (recommended)**

Run:
```bash
cd backend
dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Expected: `CREATE TABLE [OAuthClients] ...` and `CREATE TABLE [OAuthRefreshTokens] ...`.

- [ ] **Step 2: Confirm the terminal `az` session is the personal SQL admin**

Run: `az account show --query "{name:name, user:user.name}" -o json`
Expected: `Pay-As-You-Go` / `thodsaphonSP@hotmail.co.th`.

- [ ] **Step 3: Apply the migration to prod by hand**

Run (from CLAUDE.md — `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` is required, else SqlClient picks the wrong account):
```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```
Expected: `Done.` — the two tables now exist in prod. (Skipping this yields `Invalid object name 'OAuthRefreshTokens'` (HTTP 500) on the next MCP refresh — the exact failure App Insights already shows for `Trips`.)

- [ ] **Step 4: Deploy frontend + backend, then verify in App Insights**

After deploy, run (workspace-based; see CLAUDE.md):
```bash
az monitor log-analytics query --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --workspace 587ba1f6-9c1c-4c74-9f0e-4581f3f765a2 \
  --analytics-query "AppRequests | where TimeGenerated > ago(2d) | where Name has '/mcp' and ResultCode == '401' | summarize cnt=count() by bin(TimeGenerated,1d) | order by TimeGenerated desc" -o json
```
Expected: `/mcp` 401s trend toward zero across an App Service restart; returning mobile SPA sessions stop landing on `/login`.

---

## Self-Review

**Spec coverage:** A (msalConfig localStorage + storeAuthStateInCookie + googleAuth localStorage) → Task 1. C entities/config/DbContext → Task 2; migration → Task 3; DCR durable → Task 4; refresh durable + 1h JWT retained → Task 5; manual migration apply + verify → Task 6. Non-goals (B, app-level encryption, Azure Table) → not implemented, per Global Constraints. All spec sections covered.

**Placeholder scan:** no TBD/TODO; every code step shows full code; commands have expected output. `(#<n>)` in commit subjects is the tracking-issue reference the committer fills with the real issue number (CLAUDE.md convention), not a code placeholder.

**Type consistency:** `ClientStore(IApplicationDbContext)` → `RegisterAsync`/`GetAsync` used identically in Task 4 endpoints. `TokenStore(IMemoryCache, IApplicationDbContext)` → `SaveRefreshAsync(entraRefreshToken, subject, ct)`/`TakeRefreshAsync(code, ct)` used identically in Task 5's `IssueTokensAsync`. Entities `OAuthClient`/`OAuthRefreshToken` property names match across entity, config, DbSets, and store usage. `ClientRegistration`/`AuthCodeData` records unchanged.
