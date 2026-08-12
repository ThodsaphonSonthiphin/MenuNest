# Durable SPA Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep a signed-in MenuNest user signed in until they explicitly log out, instead of forcing a full re-login every time the browser closes (issue #5).

**Architecture:** The SPA keeps MSAL / Google for the *interactive* sign-in only, then exchanges that provider token once for a **MenuNest-minted app session** — a 1-hour access JWT plus a rotating refresh token held under the SPA's own `localStorage` keys and backed by a new `AppSessions` SQL table. Refreshing that session is self-contained: it re-mints from the stored subject and never calls Entra or Google, which is what makes it work for both sign-in buttons. The root cause it works around is that `@azure/msal-browser` v5 encrypts its own `localStorage` cache with a key held in a **session cookie**, so a browser close purges every account and refresh token it holds.

**Tech Stack:** .NET 10 minimal APIs + EF Core (SQL Server prod, SQLite in tests), xUnit + Moq + FluentAssertions; React + TypeScript SPA, RTK Query, vitest (node environment).

## Global Constraints

- Every commit must leave the **entire** suite green — `frontend/.husky/pre-commit` runs backend `dotnet build` + `dotnet test` (Release) **and** frontend `tsc --noEmit` + `npm run build` on every commit (~40s). Never `--no-verify`.
- Stage **explicit paths only**: `git add <path> <path>`. Never `git add -A` / `git add .` — `daily-state.md` and `AGENTS.md` are dirty working files that must never enter a feature commit.
- Every commit subject references the ticket: `(#5)`. Do **not** use `closes #5` — issue #5 stays open until the interactive verification in Task 8 passes.
- Backend tests use **xUnit + Moq + FluentAssertions**. `Substitute.For<>` (NSubstitute) does **not** compile in this repo.
- Web-layer / claims / middleware tests belong in `MenuNest.WebApi.UnitTests`, not `MenuNest.Application.UnitTests`.
- A new `DbSet<>` must be added to **all three** `IApplicationDbContext` implementers — `AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext` — **and** its EF configuration, in the **same commit**. An unmapped entity fails EF model validation for every test that touches a `DbContext`.
- `InMemoryAppDbContext` does **not** call `ApplyConfigurationsFromAssembly`. Any entity whose key is not named `Id` needs an explicit `HasKey` in its `OnModelCreating`, or every InMemory test fails with "requires a primary key to be defined".
- The SPA has **no jsdom / React Testing Library**; `vite.config.ts` runs vitest with `environment: 'node'` and `include: ['src/**/*.test.ts']`. Only pure modules get real coverage — rendering and browser-lifecycle behaviour must be verified by hand (Task 8).
- The app JWT's issuer is `config["MCP:ServerUrl"]` **verbatim, including the `/mcp` suffix** (`OAuthJwt.cs:20`). Comparisons must use that exact string, never a stripped base URL.
- Database migrations are **applied to production by hand**. Neither `Program.cs` nor CD runs `Migrate()`.

---

### Task 1: `AppSession` storage

**Files:**
- Create: `backend/src/MenuNest.Domain/Entities/AppSession.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/AppSessionConfiguration.cs`
- Modify: `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs` (after the `OAuthRefreshTokens` line)
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs:62`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs:63`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs:64` and `:234`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionPersistenceTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: `MenuNest.Domain.Entities.AppSession` with `string RefreshCode`, `string Subject`, `DateTime ExpiresAt`, `DateTime CreatedAt`; and `IApplicationDbContext.AppSessions` of type `DbSet<AppSession>`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionPersistenceTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class AppSessionPersistenceTests
{
    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task An_app_session_row_survives_a_new_dbcontext()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        using (var db = NewDb(conn))
        {
            db.AppSessions.Add(new AppSession
            {
                RefreshCode = "code-1",
                Subject = "oid-1",
                ExpiresAt = DateTime.UtcNow.AddDays(365),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var db2 = NewDb(conn);
        var row = await db2.AppSessions.SingleAsync(s => s.RefreshCode == "code-1");
        row.Subject.Should().Be("oid-1");
        row.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(364));
    }

    [Fact]
    public void The_inmemory_context_can_build_its_model_with_app_sessions()
    {
        // InMemoryAppDbContext hand-rolls OnModelCreating, so a non-Id key must be
        // declared explicitly there or model validation throws on first access.
        using var db = new InMemoryAppDbContext(
            new DbContextOptionsBuilder<InMemoryAppDbContext>()
                .UseInMemoryDatabase($"appsession-{Guid.NewGuid()}").Options);

        db.Invoking(d => d.AppSessions.Any()).Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter AppSessionPersistenceTests`
Expected: FAIL to **compile** — `AppSession` does not exist and `IApplicationDbContext` has no `AppSessions`.

- [ ] **Step 3: Create the entity**

Create `backend/src/MenuNest.Domain/Entities/AppSession.cs`:

```csharp
namespace MenuNest.Domain.Entities;

/// <summary>
/// A durable, MenuNest-minted sign-in for the web SPA (ADR-161). Deliberately
/// separate from <see cref="OAuthRefreshToken"/>: this session holds no upstream
/// identity-provider token, because refreshing it never calls one (ADR-162).
/// </summary>
public sealed class AppSession
{
    public string RefreshCode { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 4: Create the EF configuration**

Create `backend/src/MenuNest.Infrastructure/Persistence/Configurations/AppSessionConfiguration.cs`:

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class AppSessionConfiguration : IEntityTypeConfiguration<AppSession>
{
    public void Configure(EntityTypeBuilder<AppSession> builder)
    {
        builder.ToTable("AppSessions");
        builder.HasKey(s => s.RefreshCode);
        builder.Property(s => s.RefreshCode).ValueGeneratedNever().HasMaxLength(128);
        builder.Property(s => s.Subject).IsRequired().HasMaxLength(128);
        builder.Property(s => s.ExpiresAt).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.HasIndex(s => s.Subject);
    }
}
```

- [ ] **Step 5: Add the DbSet to the interface and all three contexts**

In `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs`, directly below `DbSet<OAuthRefreshToken> OAuthRefreshTokens { get; }`:

```csharp

    // Durable SPA sessions (ADR-161/162) — separate from the MCP proxy's store.
    DbSet<AppSession> AppSessions { get; }
```

In **each** of `AppDbContext.cs` (below line 62), `SqliteAppDbContext.cs` (below line 63) and `InMemoryAppDbContext.cs` (below line 64), add:

```csharp
    public DbSet<AppSession> AppSessions => Set<AppSession>();
```

In `InMemoryAppDbContext.cs`, in `OnModelCreating` beside the existing explicit keys (line 234):

```csharp
        modelBuilder.Entity<AppSession>().HasKey(s => s.RefreshCode);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter AppSessionPersistenceTests`
Expected: PASS, 2 tests.

- [ ] **Step 7: Generate the migration**

Run from the repo root:

```bash
cd backend && dotnet ef migrations add AppSessions \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Then confirm the generated `Up()` creates the `AppSessions` table with `RefreshCode` as the primary key. Do **not** apply it to production yet — that is Task 8.

- [ ] **Step 8: Run the whole backend suite**

Run: `dotnet test backend/MenuNest.sln`
Expected: PASS — this proves the new DbSet did not break EF model validation for the other contexts.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/AppSession.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/AppSessionConfiguration.cs \
        backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs \
        backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs \
        backend/src/MenuNest.Infrastructure/Migrations \
        backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs \
        backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs \
        backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionPersistenceTests.cs
git commit -m "feat(auth): add AppSessions table for durable SPA sessions (#5)"
```

---

### Task 2: `AppSessionStore`

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/AppSessionStore.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionStoreTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext.AppSessions`, `TokenUtil.Opaque()` (Task 1 / existing).
- Produces: `AppSessionStore(IApplicationDbContext db)` with
  `Task<string> IssueAsync(string subject, CancellationToken ct = default)`,
  `Task<string?> TakeAsync(string refreshCode, CancellationToken ct = default)` (returns the subject, single-use),
  `Task<bool> RevokeAsync(string refreshCode, CancellationToken ct = default)`,
  and `const int LifetimeDays = 365`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionStoreTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.WebApi.Oauth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class AppSessionStoreTests
{
    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task An_issued_session_survives_a_restart_and_returns_its_subject()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        string code;
        using (var db = NewDb(conn))
            code = await new AppSessionStore(db).IssueAsync("oid-1");

        using var db2 = NewDb(conn); // fresh context = App Service restart
        (await new AppSessionStore(db2).TakeAsync(code)).Should().Be("oid-1");
    }

    [Fact]
    public async Task A_refresh_code_is_single_use()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var store = new AppSessionStore(db);

        var code = await store.IssueAsync("oid-1");
        (await store.TakeAsync(code)).Should().Be("oid-1");
        (await store.TakeAsync(code)).Should().BeNull("the row is consumed on first use");
    }

    [Fact]
    public async Task An_expired_session_is_refused_and_removed()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);

        db.AppSessions.Add(new AppSession
        {
            RefreshCode = "stale",
            Subject = "oid-1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-400),
        });
        await db.SaveChangesAsync();

        (await new AppSessionStore(db).TakeAsync("stale")).Should().BeNull();
        (await db.AppSessions.AnyAsync(s => s.RefreshCode == "stale")).Should().BeFalse();
    }

    [Fact]
    public async Task Revoking_one_device_leaves_the_other_signed_in()
    {
        // ADR-159: logout revokes only the device that pressed it.
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var store = new AppSessionStore(db);

        var phone = await store.IssueAsync("oid-1");
        var laptop = await store.IssueAsync("oid-1");

        (await store.RevokeAsync(laptop)).Should().BeTrue();
        (await store.TakeAsync(phone)).Should().Be("oid-1", "the other device is untouched");
    }

    [Fact]
    public async Task Revoking_an_unknown_code_is_a_no_op()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);

        (await new AppSessionStore(db).RevokeAsync("never-issued")).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter AppSessionStoreTests`
Expected: FAIL to compile — `AppSessionStore` does not exist.

- [ ] **Step 3: Write the store**

Create `backend/src/MenuNest.WebApi/Oauth/AppSessionStore.cs`:

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.WebApi.Oauth;

/// <summary>
/// Durable SPA sessions (ADR-161). Rotation is single-use like <see cref="TokenStore"/>,
/// but there is no upstream token to exchange — the row IS the session (ADR-162), which
/// is what lets it serve a Google sign-in as well as a Microsoft one.
/// </summary>
public sealed class AppSessionStore(IApplicationDbContext db)
{
    /// <summary>Idle lifetime. Re-stamped on every rotation, so it rolls forward with use.</summary>
    public const int LifetimeDays = 365;

    public async Task<string> IssueAsync(string subject, CancellationToken ct = default)
    {
        var code = TokenUtil.Opaque();
        db.AppSessions.Add(new AppSession
        {
            RefreshCode = code,
            Subject = subject,
            ExpiresAt = DateTime.UtcNow.AddDays(LifetimeDays),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return code;
    }

    /// <summary>
    /// Single-use: consumes the row and returns its subject, or null when the code is
    /// unknown or expired. An expired row is deleted rather than left to rot.
    /// </summary>
    public async Task<string?> TakeAsync(string refreshCode, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshCode)) return null;

        var row = await db.AppSessions.FirstOrDefaultAsync(s => s.RefreshCode == refreshCode, ct);
        if (row is null) return null;

        db.AppSessions.Remove(row);
        await db.SaveChangesAsync(ct);

        return row.ExpiresAt <= DateTime.UtcNow ? null : row.Subject;
    }

    /// <summary>Deletes only the presented session (ADR-159). True when a row was removed.</summary>
    public async Task<bool> RevokeAsync(string refreshCode, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshCode)) return false;

        var row = await db.AppSessions.FirstOrDefaultAsync(s => s.RefreshCode == refreshCode, ct);
        if (row is null) return false;

        db.AppSessions.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter AppSessionStoreTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.WebApi/Oauth/AppSessionStore.cs \
        backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionStoreTests.cs
git commit -m "feat(auth): add AppSessionStore with single-use rotation and per-device revoke (#5)"
```

---

### Task 3: `AppSessionService`

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/AppSessionService.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionServiceTests.cs`

**Interfaces:**
- Consumes: `AppSessionStore` (Task 2); existing `OAuthJwt.Mint(string subject, string clientId, string scope, IEnumerable<Claim> extra, int lifetimeSeconds = 3600)`.
- Produces: `sealed record AppSessionTokens(string AccessToken, int ExpiresIn, string RefreshToken)`; `AppSessionService(AppSessionStore sessions, OAuthJwt jwt)` with
  `Task<AppSessionTokens> IssueAsync(string subject, string? name, string? email, CancellationToken ct = default)` and
  `Task<AppSessionTokens?> RefreshAsync(string refreshCode, CancellationToken ct = default)`;
  `const string ClientId = "menunest-spa"`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionServiceTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.WebApi.Oauth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class AppSessionServiceTests
{
    private const string ServerUrl = "https://menunest.azurewebsites.net/mcp";

    private static OAuthJwt Jwt() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "test-signing-key-please-change-in-prod",
            ["MCP:ServerUrl"] = ServerUrl,
        }).Build());

    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task An_issued_access_token_carries_oid_so_it_maps_to_the_same_user()
    {
        // CurrentUserService resolves ExternalId as objectidentifier ?? oid ?? sub.
        // If this claim were missing the session would provision a DUPLICATE user.
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var sut = new AppSessionService(new AppSessionStore(db), Jwt());

        var tokens = await sut.IssueAsync("oid-123", "Pon", "pon@x.io");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        jwt.Claims.Should().Contain(c => c.Type == "oid" && c.Value == "oid-123");
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == "oid-123");
        jwt.Issuer.Should().Be(ServerUrl);
        tokens.ExpiresIn.Should().Be(3600);
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Refreshing_rotates_the_code_and_keeps_the_subject()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var sut = new AppSessionService(new AppSessionStore(db), Jwt());

        var first = await sut.IssueAsync("oid-123", "Pon", "pon@x.io");
        var second = await sut.RefreshAsync(first.RefreshToken);

        second.Should().NotBeNull();
        second!.RefreshToken.Should().NotBe(first.RefreshToken, "rotation is single-use");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(second.AccessToken);
        jwt.Claims.Should().Contain(c => c.Type == "oid" && c.Value == "oid-123");

        (await sut.RefreshAsync(first.RefreshToken)).Should()
            .BeNull("the old code must not be reusable");
    }

    [Fact]
    public async Task Refreshing_an_unknown_code_returns_null()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var sut = new AppSessionService(new AppSessionStore(db), Jwt());

        (await sut.RefreshAsync("never-issued")).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter AppSessionServiceTests`
Expected: FAIL to compile — `AppSessionService` does not exist.

- [ ] **Step 3: Write the service**

Create `backend/src/MenuNest.WebApi/Oauth/AppSessionService.cs`:

```csharp
using System.Security.Claims;

namespace MenuNest.WebApi.Oauth;

/// <summary>The token pair handed to the SPA. Field names match the JSON the client reads.</summary>
public sealed record AppSessionTokens(string AccessToken, int ExpiresIn, string RefreshToken);

/// <summary>
/// Mints and rotates the SPA's app session (ADR-161). Refresh is deliberately
/// self-contained: it re-mints from the stored subject and never calls Entra or
/// Google, so one mechanism serves both sign-in buttons (ADR-160).
/// </summary>
public sealed class AppSessionService(AppSessionStore sessions, OAuthJwt jwt)
{
    public const string ClientId = "menunest-spa";
    private const int AccessTokenSeconds = 3600;

    public async Task<AppSessionTokens> IssueAsync(
        string subject, string? name, string? email, CancellationToken ct = default)
    {
        var extra = new List<Claim>();
        if (name is not null) extra.Add(new Claim("name", name));
        if (email is not null)
        {
            extra.Add(new Claim("email", email));
            extra.Add(new Claim("preferred_username", email));
        }

        var accessToken = jwt.Mint(subject, ClientId, string.Empty, extra, AccessTokenSeconds);
        var refreshCode = await sessions.IssueAsync(subject, ct);
        return new AppSessionTokens(accessToken, AccessTokenSeconds, refreshCode);
    }

    /// <summary>
    /// Rotates the session. Name/email are not carried across a refresh on purpose:
    /// they are only ever read to provision a NEW user, and provisioning happens at
    /// exchange time while the real provider token is still on the request.
    /// </summary>
    public async Task<AppSessionTokens?> RefreshAsync(string refreshCode, CancellationToken ct = default)
    {
        var subject = await sessions.TakeAsync(refreshCode, ct);
        if (subject is null) return null;
        return await IssueAsync(subject, name: null, email: null, ct);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter AppSessionServiceTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.WebApi/Oauth/AppSessionService.cs \
        backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionServiceTests.cs
git commit -m "feat(auth): mint and rotate app sessions without an IdP round-trip (#5)"
```

---

### Task 4: Accept app-minted tokens on `/api/*`

Today `ForwardDefaultSelector` sends any non-Google issuer to the `Microsoft` handler, so an app-minted JWT is rejected. The lambda also cannot be unit-tested where it sits, so extract it first.

**Files:**
- Create: `backend/src/MenuNest.WebApi/Auth/BearerSchemeSelector.cs`
- Modify: `backend/src/MenuNest.WebApi/Program.cs:51-80` (the `ForwardDefaultSelector` body)
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Auth/BearerSchemeSelectorTests.cs`

**Interfaces:**
- Consumes: existing `OAuthJwt` (to mint a realistic token in the test).
- Produces: `static class BearerSchemeSelector` with `string Select(string? authorizationHeader, string appIssuer)` returning one of the constants `Google` (`"Google"`), `Microsoft` (`"Microsoft"`), `AppIssued` (`"McpProxy"`).

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Auth/BearerSchemeSelectorTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using MenuNest.WebApi.Auth;
using MenuNest.WebApi.Oauth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Auth;

public sealed class BearerSchemeSelectorTests
{
    // The app JWT's issuer is MCP:ServerUrl VERBATIM, /mcp suffix included (OAuthJwt.cs:20).
    private const string AppIssuer = "https://menunest.azurewebsites.net/mcp";

    private static string AppToken()
    {
        var jwt = new OAuthJwt(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "test-signing-key-please-change-in-prod",
                ["MCP:ServerUrl"] = AppIssuer,
            }).Build());
        return jwt.Mint("oid-1", AppSessionService.ClientId, string.Empty, Array.Empty<Claim>());
    }

    private static string TokenFrom(string issuer) =>
        new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(issuer: issuer, audience: "aud", claims: new[] { new Claim("sub", "x") }));

    [Fact]
    public void An_app_minted_token_goes_to_the_app_scheme()
    {
        BearerSchemeSelector.Select($"Bearer {AppToken()}", AppIssuer)
            .Should().Be(BearerSchemeSelector.AppIssued);
    }

    [Fact]
    public void A_google_token_still_goes_to_the_google_scheme()
    {
        BearerSchemeSelector.Select($"Bearer {TokenFrom("https://accounts.google.com")}", AppIssuer)
            .Should().Be(BearerSchemeSelector.Google);
    }

    [Fact]
    public void An_entra_token_still_goes_to_the_microsoft_scheme()
    {
        BearerSchemeSelector.Select(
                $"Bearer {TokenFrom("https://login.microsoftonline.com/common/v2.0")}", AppIssuer)
            .Should().Be(BearerSchemeSelector.Microsoft);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer not-a-jwt")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public void Anything_unreadable_falls_back_to_microsoft(string? header)
    {
        BearerSchemeSelector.Select(header, AppIssuer)
            .Should().Be(BearerSchemeSelector.Microsoft);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter BearerSchemeSelectorTests`
Expected: FAIL to compile — `BearerSchemeSelector` does not exist.

- [ ] **Step 3: Write the selector**

Create `backend/src/MenuNest.WebApi/Auth/BearerSchemeSelector.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;

namespace MenuNest.WebApi.Auth;

/// <summary>
/// Chooses the JWT bearer scheme for an incoming Authorization header. Extracted
/// from Program.cs so the branching is unit-testable (issue #5).
/// </summary>
public static class BearerSchemeSelector
{
    public const string Google = "Google";
    public const string Microsoft = "Microsoft";

    /// <summary>
    /// Scheme for tokens this app minted itself. Named "McpProxy" because it is the
    /// existing scheme already configured with OAuthJwt.ValidationParameters(); the
    /// SPA's app session (ADR-161) is validated by exactly the same parameters.
    /// </summary>
    public const string AppIssued = "McpProxy";

    /// <param name="appIssuer">MCP:ServerUrl verbatim — the issuer OAuthJwt stamps.</param>
    public static string Select(string? authorizationHeader, string appIssuer)
    {
        if (authorizationHeader is null
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Microsoft;
        }

        var token = authorizationHeader["Bearer ".Length..];
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token)) return Microsoft;

        var issuer = handler.ReadJwtToken(token).Issuer;
        if (issuer == "https://accounts.google.com") return Google;
        if (issuer == appIssuer) return AppIssued;
        return Microsoft;
    }
}
```

- [ ] **Step 4: Use it from Program.cs**

In `backend/src/MenuNest.WebApi/Program.cs`, replace the whole body of the `ForwardDefaultSelector` lambda (currently lines 51-80) with:

```csharp
        options.ForwardDefaultSelector = context =>
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("MenuNest.Auth.PolicyScheme");
            var appIssuer = builder.Configuration["MCP:ServerUrl"] ?? string.Empty;
            var scheme = MenuNest.WebApi.Auth.BearerSchemeSelector.Select(
                context.Request.Headers.Authorization.FirstOrDefault(), appIssuer);
            logger.LogDebug("Forwarding bearer to {Scheme} scheme", scheme);
            return scheme;
        };
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter BearerSchemeSelectorTests`
Expected: PASS, 7 test cases.

- [ ] **Step 6: Run the whole backend suite**

Run: `dotnet test backend/MenuNest.sln`
Expected: PASS — confirms the refactor did not change Microsoft/Google routing for existing callers.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.WebApi/Auth/BearerSchemeSelector.cs \
        backend/src/MenuNest.WebApi/Program.cs \
        backend/tests/MenuNest.WebApi.UnitTests/Auth/BearerSchemeSelectorTests.cs
git commit -m "feat(auth): route app-minted JWTs to the app scheme on /api/* (#5)"
```

---

### Task 5: Session endpoints

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/AppSessionEndpoints.cs`
- Modify: `backend/src/MenuNest.WebApi/Program.cs:37` (DI) and `:226` (mapping)
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionEndpointContractTests.cs`

**Interfaces:**
- Consumes: `AppSessionService`, `AppSessionStore` (Tasks 2-3); existing `IUserProvisioner.GetOrProvisionCurrentAsync(CancellationToken)` and `ICurrentUserService.RequireExternalId()`.
- Produces: `MapAppSession(this WebApplication app)` serving `POST /api/session/exchange`, `POST /api/session/refresh`, `POST /api/session/logout`; request body record `AppSessionEndpoints.RefreshRequest(string refresh_token)`.

- [ ] **Step 1: Write the failing test**

The endpoint bodies are thin delegates; what is worth pinning is that the exchange path provisions through `IUserProvisioner` (so a brand-new Google user is not recorded as Microsoft) and that the JSON contract matches what the SPA reads.

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionEndpointContractTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using MenuNest.WebApi.Oauth;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class AppSessionEndpointContractTests
{
    [Fact]
    public void The_refresh_request_binds_the_snake_case_field_the_spa_sends()
    {
        var body = JsonSerializer.Deserialize<AppSessionEndpoints.RefreshRequest>(
            """{"refresh_token":"abc123"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        body!.refresh_token.Should().Be("abc123");
    }

    [Fact]
    public void The_token_response_serialises_the_fields_the_spa_reads()
    {
        var json = JsonSerializer.Serialize(
            new AppSessionTokens("access-jwt", 3600, "refresh-code"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("accessToken").GetString().Should().Be("access-jwt");
        doc.RootElement.GetProperty("expiresIn").GetInt32().Should().Be(3600);
        doc.RootElement.GetProperty("refreshToken").GetString().Should().Be("refresh-code");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter AppSessionEndpointContractTests`
Expected: FAIL to compile — `AppSessionEndpoints` does not exist.

- [ ] **Step 3: Write the endpoints**

Create `backend/src/MenuNest.WebApi/Oauth/AppSessionEndpoints.cs`:

```csharp
using MenuNest.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Oauth;

/// <summary>
/// The SPA's durable-session endpoints (ADR-161). Deliberately separate from
/// /oauth/* : that is the MCP proxy's OAuth 2.1 contract, whose refresh grant is
/// anchored on a server-held Entra refresh token this session does not have.
/// </summary>
public static class AppSessionEndpoints
{
    public sealed record RefreshRequest(string refresh_token);

    public static void MapAppSession(this WebApplication app)
    {
        // Exchange runs under the existing MultiAuth scheme, so the Microsoft/Google
        // bearer is still on the request. Provision HERE: CurrentUserService.Provider
        // reads `iss`, which on our own JWT is the server URL and would resolve to
        // null, silently recording a new Google user as Microsoft.
        app.MapPost("/api/session/exchange", async (
            IUserProvisioner provisioner,
            ICurrentUserService currentUser,
            AppSessionService sessions,
            CancellationToken ct) =>
        {
            var user = await provisioner.GetOrProvisionCurrentAsync(ct);
            var tokens = await sessions.IssueAsync(
                currentUser.RequireExternalId(), user.DisplayName, user.Email, ct);
            return Results.Ok(tokens);
        });

        // AllowAnonymous is required, not optional: Program.cs sets a FallbackPolicy
        // demanding an authenticated user, and by refresh time the access token is
        // expired by definition.
        app.MapPost("/api/session/refresh", async (
            [FromBody] RefreshRequest body,
            AppSessionService sessions,
            CancellationToken ct) =>
        {
            var tokens = await sessions.RefreshAsync(body.refresh_token, ct);
            return tokens is null
                ? Results.BadRequest(new { error = "invalid_grant" })
                : Results.Ok(tokens);
        }).AllowAnonymous();

        // Revokes only the presented session (ADR-159). Idempotent: an unknown or
        // already-revoked code still reports success, so sign-out never fails.
        app.MapPost("/api/session/logout", async (
            [FromBody] RefreshRequest body,
            AppSessionStore sessions,
            CancellationToken ct) =>
        {
            await sessions.RevokeAsync(body.refresh_token, ct);
            return Results.NoContent();
        }).AllowAnonymous();
    }
}
```

- [ ] **Step 4: Register in Program.cs**

Beside the existing OAuth registrations (`Program.cs:37`, after `AddSingleton<OAuthJwt>()`):

```csharp
builder.Services.AddScoped<MenuNest.WebApi.Oauth.AppSessionStore>();
builder.Services.AddScoped<MenuNest.WebApi.Oauth.AppSessionService>();
```

And directly below `app.MapOAuthProxy();` (`Program.cs:226`):

```csharp
app.MapAppSession();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter AppSessionEndpointContractTests`
Expected: PASS, 2 tests.

- [ ] **Step 6: Verify the app still boots**

Run: `dotnet build backend/MenuNest.sln -c Release`
Expected: **0 errors.** A non-zero error count is never the full list — iterate to zero.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.WebApi/Oauth/AppSessionEndpoints.cs \
        backend/src/MenuNest.WebApi/Program.cs \
        backend/tests/MenuNest.WebApi.UnitTests/Oauth/AppSessionEndpointContractTests.cs
git commit -m "feat(auth): add /api/session exchange, refresh and logout endpoints (#5)"
```

---

### Task 6: Frontend session module

**Files:**
- Create: `frontend/src/shared/auth/appSession.ts`
- Test: `frontend/src/shared/auth/appSession.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: `interface AppSession { accessToken: string; refreshToken: string; expiresAtMs: number }`;
  `storeAppSession(t: {accessToken: string; refreshToken: string; expiresIn: number}): void`;
  `getAppSession(): AppSession | null`;
  `clearAppSession(): void`;
  `isAppSessionExpired(expiresAtMs: number, nowMs?: number): boolean`;
  `hasAppSession(): boolean`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/shared/auth/appSession.test.ts`:

```ts
import {afterEach, describe, expect, it, vi} from 'vitest'
import {
  clearAppSession,
  getAppSession,
  hasAppSession,
  isAppSessionExpired,
  storeAppSession,
} from './appSession'

function stubStorage(initial: Record<string, string> = {}) {
  const map = new Map(Object.entries(initial))
  const store = {
    getItem: vi.fn((k: string) => map.get(k) ?? null),
    setItem: vi.fn((k: string, v: string) => void map.set(k, v)),
    removeItem: vi.fn((k: string) => void map.delete(k)),
  }
  vi.stubGlobal('localStorage', store)
  return {store, map}
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('storeAppSession / getAppSession', () => {
  it('round-trips a stored session and derives an absolute expiry', () => {
    stubStorage()
    const before = Date.now()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})

    const session = getAppSession()
    expect(session?.accessToken).toBe('a')
    expect(session?.refreshToken).toBe('r')
    expect(session!.expiresAtMs).toBeGreaterThanOrEqual(before + 3600 * 1000)
  })

  it('returns null when nothing is stored', () => {
    stubStorage()
    expect(getAppSession()).toBeNull()
  })

  it('returns null when the refresh token is missing, rather than a half session', () => {
    stubStorage({'menunest.session.access': 'a', 'menunest.session.expiresAt': '99999999999999'})
    expect(getAppSession()).toBeNull()
  })

  it('returns null when the stored expiry is not a number', () => {
    stubStorage({
      'menunest.session.access': 'a',
      'menunest.session.refresh': 'r',
      'menunest.session.expiresAt': 'not-a-number',
    })
    expect(getAppSession()).toBeNull()
  })
})

describe('isAppSessionExpired', () => {
  it('is false well before expiry', () => {
    expect(isAppSessionExpired(10_000_000, 9_000_000)).toBe(false)
  })

  it('is true once past expiry', () => {
    expect(isAppSessionExpired(9_000_000, 10_000_000)).toBe(true)
  })

  it('is true inside the 60s leeway so a token never dies mid-flight', () => {
    const now = 1_000_000
    expect(isAppSessionExpired(now + 30_000, now)).toBe(true)
    expect(isAppSessionExpired(now + 90_000, now)).toBe(false)
  })
})

describe('clearAppSession / hasAppSession', () => {
  it('removes every key it wrote', () => {
    const {store} = stubStorage()
    storeAppSession({accessToken: 'a', refreshToken: 'r', expiresIn: 3600})
    expect(hasAppSession()).toBe(true)

    clearAppSession()
    expect(store.removeItem).toHaveBeenCalledWith('menunest.session.access')
    expect(store.removeItem).toHaveBeenCalledWith('menunest.session.refresh')
    expect(store.removeItem).toHaveBeenCalledWith('menunest.session.expiresAt')
    expect(hasAppSession()).toBe(false)
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/shared/auth/appSession.test.ts`
Expected: FAIL — cannot resolve `./appSession`.

- [ ] **Step 3: Write the module**

Create `frontend/src/shared/auth/appSession.ts`:

```ts
// The MenuNest-minted durable session (ADR-161). These keys are OURS: msal-browser
// v5 encrypts only its own entries with a key held in a session cookie, so anything
// we write under our own names survives a browser restart untouched.
const ACCESS_KEY = 'menunest.session.access'
const REFRESH_KEY = 'menunest.session.refresh'
const EXPIRES_KEY = 'menunest.session.expiresAt'

// Treat a token as expired early so we never send one that dies mid-flight,
// mirroring the leeway in googleAuth.ts.
const EXPIRY_LEEWAY_MS = 60_000

export interface AppSession {
  accessToken: string
  refreshToken: string
  expiresAtMs: number
}

export function storeAppSession(tokens: {
  accessToken: string
  refreshToken: string
  expiresIn: number
}): void {
  localStorage.setItem(ACCESS_KEY, tokens.accessToken)
  localStorage.setItem(REFRESH_KEY, tokens.refreshToken)
  localStorage.setItem(EXPIRES_KEY, String(Date.now() + tokens.expiresIn * 1000))
}

/**
 * The stored session, or null when any part of it is missing or unreadable.
 * All-or-nothing on purpose: a half-written session would let a caller send a
 * token it cannot renew.
 */
export function getAppSession(): AppSession | null {
  const accessToken = localStorage.getItem(ACCESS_KEY)
  const refreshToken = localStorage.getItem(REFRESH_KEY)
  const rawExpiry = localStorage.getItem(EXPIRES_KEY)
  if (!accessToken || !refreshToken || !rawExpiry) return null

  const expiresAtMs = Number(rawExpiry)
  if (!Number.isFinite(expiresAtMs)) return null

  return {accessToken, refreshToken, expiresAtMs}
}

export function clearAppSession(): void {
  localStorage.removeItem(ACCESS_KEY)
  localStorage.removeItem(REFRESH_KEY)
  localStorage.removeItem(EXPIRES_KEY)
}

export function isAppSessionExpired(expiresAtMs: number, nowMs: number = Date.now()): boolean {
  return expiresAtMs <= nowMs + EXPIRY_LEEWAY_MS
}

export function hasAppSession(): boolean {
  return getAppSession() !== null
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx vitest run src/shared/auth/appSession.test.ts`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/shared/auth/appSession.ts frontend/src/shared/auth/appSession.test.ts
git commit -m "feat(auth): add the SPA app-session storage module (#5)"
```

---

### Task 7: Wire the session into the SPA

**Files:**
- Create: `frontend/src/shared/auth/appSessionApi.ts`
- Modify: `frontend/src/shared/api/api.ts:63-93` (`acquireAccessToken`)
- Modify: `frontend/src/shared/data/useAuthDataManager.ts:145-167`
- Modify: `frontend/src/shared/components/ProtectedRoute.tsx:28`
- Modify: `frontend/src/shared/hooks/useCurrentUser.ts:34-41`
- Modify: `frontend/src/shared/auth/reauth.ts:34-44`
- Modify: `frontend/src/pages/auth/LoginPage.tsx` (both sign-in success paths)

**Interfaces:**
- Consumes: everything from Task 6.
- Produces: `exchangeForAppSession(providerToken: string): Promise<boolean>` and `refreshAppSession(refreshToken: string): Promise<AppSession | null>` from `appSessionApi.ts`.

- [ ] **Step 1: Write the API client**

Create `frontend/src/shared/auth/appSessionApi.ts`. Plain `fetch`, **never** RTK Query — the RTK base query calls `acquireAccessToken()` in `prepareHeaders`, so refreshing through it would recurse forever.

```ts
import {clearAppSession, getAppSession, storeAppSession, type AppSession} from './appSession'

const API_BASE = import.meta.env.VITE_API_BASE_URL || ''

interface TokenResponse {
  accessToken: string
  expiresIn: number
  refreshToken: string
}

/**
 * Trade a freshly-obtained Microsoft/Google token for a durable app session.
 * Returns false on any failure — the caller must carry on with the provider
 * token, because a failed exchange may never block sign-in.
 */
export async function exchangeForAppSession(providerToken: string): Promise<boolean> {
  try {
    const res = await fetch(`${API_BASE}/api/session/exchange`, {
      method: 'POST',
      headers: {Authorization: `Bearer ${providerToken}`},
    })
    if (!res.ok) return false
    const body = (await res.json()) as TokenResponse
    storeAppSession(body)
    return true
  } catch {
    return false
  }
}

/** Rotate the session. Returns null when the server refused it (revoked/expired). */
export async function refreshAppSession(refreshToken: string): Promise<AppSession | null> {
  try {
    const res = await fetch(`${API_BASE}/api/session/refresh`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({refresh_token: refreshToken}),
    })
    if (!res.ok) {
      clearAppSession()
      return null
    }
    const body = (await res.json()) as TokenResponse
    storeAppSession(body)
    return getAppSession()
  } catch {
    // A network blip must not sign the user out — keep the session and retry later.
    return null
  }
}

/** Best-effort revoke of THIS device's session only (ADR-159). */
export async function revokeAppSession(refreshToken: string): Promise<void> {
  try {
    await fetch(`${API_BASE}/api/session/logout`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({refresh_token: refreshToken}),
    })
  } catch {
    // Ignore — the local clear below is what the user actually sees.
  }
}
```

- [ ] **Step 2: Put the app session first in `acquireAccessToken`**

In `frontend/src/shared/api/api.ts`, add to the imports:

```ts
import {clearAppSession, getAppSession, isAppSessionExpired} from '../auth/appSession'
import {refreshAppSession} from '../auth/appSessionApi'
```

and insert this block at the very top of `acquireAccessToken()`, before the MSAL attempt:

```ts
    // 1. Our own durable session first — the only credential that survives a
    //    browser restart, because msal-browser v5 purges its own cache (ADR-161).
    const session = getAppSession()
    if (session) {
        if (!isAppSessionExpired(session.expiresAtMs)) return session.accessToken
        const rotated = await refreshAppSession(session.refreshToken)
        if (rotated) return rotated.accessToken
        clearAppSession()
    }
```

Leave the existing MSAL and Google blocks untouched below it — they remain the fallback and the interactive path.

- [ ] **Step 3: Make the Grid path use the same token source**

`useAuthDataManager` currently calls `acquireTokenSilent` itself, so it would still break after a browser close. Export the shared helper from `api.ts`:

```ts
export async function acquireAccessToken(): Promise<string | null> {
```

(change the existing `async function acquireAccessToken()` declaration to add `export`), then in `frontend/src/shared/data/useAuthDataManager.ts` replace the whole `useEffect` body (lines 152-167) with:

```ts
  useEffect(() => {
    let cancelled = false
    acquireAccessToken()
      .then((t) => {
        if (!cancelled && t) setToken(t)
      })
      .catch(() => {
        /* token failure is handled by the shared acquire path */
      })
    return () => {
      cancelled = true
    }
  }, [instance, accounts])
```

and change its imports to:

```ts
import {acquireAccessToken} from '../api/api'
```

removing the now-unused `apiScopes` import. Keep `useMsal()` — `accounts` is still the effect's dependency, so the token is re-fetched when MSAL settles.

- [ ] **Step 4: Let the route guard see the session**

In `frontend/src/shared/components/ProtectedRoute.tsx`, add the import:

```ts
import {hasAppSession} from '../auth/appSession'
```

and change the guard on line 28 to:

```ts
  if (!isAuthenticated && !isGoogleAuthenticated() && !hasAppSession()) {
```

- [ ] **Step 5: Exchange after each interactive sign-in**

In `frontend/src/pages/auth/LoginPage.tsx`, add:

```ts
import {exchangeForAppSession} from '../../shared/auth/appSessionApi'
```

In the Google `onSuccess` handler, before `navigate('/', {replace: true})`:

```ts
                await exchangeForAppSession(credentialResponse.credential)
```

and make that callback `async`. For Microsoft, extend the existing "interactive login just completed" effect so it exchanges once MSAL has an account:

```ts
  useEffect(() => {
    if (inProgress !== InteractionStatus.None) return
    if (isAuthenticated) {
      const account = instance.getActiveAccount()
      if (account) {
        instance
          .acquireTokenSilent({scopes: apiScopes, account})
          .then((r) => exchangeForAppSession(r.accessToken))
          .catch(() => {
            // Exchange is an upgrade, never a gate — sign-in already succeeded.
          })
      }
    }
  }, [isAuthenticated, inProgress, instance])
```

adding `import {apiScopes, loginRequest} from '../../shared/auth/msalConfig'` (extend the existing import).

- [ ] **Step 6: Revoke on sign-out and clear on 401**

In `frontend/src/shared/hooks/useCurrentUser.ts`, add imports:

```ts
import {clearAppSession, getAppSession} from '../auth/appSession'
import {revokeAppSession} from '../auth/appSessionApi'
```

and replace `signOut` (lines 34-41) with:

```ts
  const signOut = async () => {
    const session = getAppSession()
    if (session) await revokeAppSession(session.refreshToken)
    clearAppSession()
    clearGoogleToken()
    if (isMsalAuth) {
      instance.logoutRedirect()
    } else {
      window.location.href = '/login'
    }
  }
```

In `frontend/src/shared/auth/reauth.ts`, add `import {clearAppSession} from './appSession'` and add `clearAppSession()` as the first line of `handleAuthFailure()`, above `clearGoogleToken()` — otherwise a rejected session survives the bounce and 401s again immediately.

- [ ] **Step 7: Verify the frontend gates pass**

Run: `cd frontend && npx tsc --noEmit && npx vitest run && npm run build`
Expected: all three PASS. (These gates cannot catch rendering or browser-lifecycle bugs — Task 8 covers that.)

- [ ] **Step 8: Commit**

```bash
git add frontend/src/shared/auth/appSessionApi.ts \
        frontend/src/shared/api/api.ts \
        frontend/src/shared/data/useAuthDataManager.ts \
        frontend/src/shared/components/ProtectedRoute.tsx \
        frontend/src/shared/hooks/useCurrentUser.ts \
        frontend/src/shared/auth/reauth.ts \
        frontend/src/pages/auth/LoginPage.tsx
git commit -m "feat(auth): use the durable app session across both token paths (#5)"
```

---

### Task 8: Ship and verify

No automated gate in this repo can prove the actual fix works, because the symptom only appears across a real browser restart. This task is where the fix is confirmed.

**Files:**
- Modify: none (documentation commit + deployment steps)

**Interfaces:**
- Consumes: everything above.
- Produces: a verified production deployment and a closed loop on issue #5.

- [ ] **Step 1: Commit the design documents**

SDD implementers stage only code and tests, so the design docs written before this plan are still uncommitted and would otherwise orphan.

```bash
git add docs/adr/159-logout-revokes-only-the-device-that-pressed-it.md \
        docs/adr/160-login-screen-keeps-both-providers.md \
        docs/adr/161-durable-spa-session-is-app-minted-and-provider-agnostic.md \
        docs/adr/162-app-sessions-get-their-own-table.md \
        docs/adr/036-spa-token-cache-persist-localstorage.md \
        docs/superpowers/specs/2026-08-12-durable-spa-session-design.md \
        docs/superpowers/plans/2026-08-12-durable-spa-session.md \
        CONTEXT.md
git commit -m "docs(auth): ADR-159..162 + spec for the durable SPA session, supersede ADR-036 (#5)"
```

- [ ] **Step 2: Apply the migration to production BEFORE pushing**

Prod deploys on push to `main`. If the code lands before the table exists, every `/api/session/exchange` returns 500 (`Invalid object name 'AppSessions'`) — the exact failure shape that hit issue #49.

The SQL server firewalls by IP, so add a temporary rule, apply, then remove it:

```bash
IP=<your current public IP>
az sql server firewall-rule create --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply \
  --start-ip-address $IP --end-ip-address $IP

cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"

az sql server firewall-rule delete --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply
```

Requires the terminal `az` session to be `personal@example.com`. Verify with `az account show` first — expect `Pay-As-You-Go`.

- [ ] **Step 3: Push**

```bash
git push main HEAD:main
```

(The remote is named `main`, not `origin`.) Then confirm the GitHub Actions run actually reaches the deploy stage — a queued or skipped run means prod is still on old code.

- [ ] **Step 4: Verify the fix by hand — this is the acceptance test**

1. Open the deployed app and sign in with **Microsoft**.
2. Open DevTools → Application → Local Storage and confirm `menunest.session.access`, `menunest.session.refresh` and `menunest.session.expiresAt` are present.
3. **Close the browser completely** (every window, not just the tab).
4. Reopen and navigate to the app.

Expected: the app opens **signed in**, with no login card and no password prompt. Confirm in DevTools → Network that a `POST /api/session/refresh` returned 200 and that `msal.cache.encryption` is a *new* cookie — proving the session survived precisely because it no longer depends on MSAL's cache.

5. Open a page backed by a Syncfusion Grid and confirm it loads data (this is the `useAuthDataManager` path from Task 7 Step 3).
6. Press sign-out, then confirm reopening the app **does** show the login card.

- [ ] **Step 5: Confirm the telemetry moves**

The pre-fix baseline was 95 of 141 sessions starting at `/login`, and 15 of 16 sessions after a >24 h gap. After a few days of real use, re-run the classification and expect login-first sessions to fall sharply:

```bash
az monitor log-analytics query --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --workspace 587ba1f6-9c1c-4c74-9f0e-4581f3f765a2 \
  --analytics-query "AppPageViews | where TimeGenerated > ago(7d) | summarize arg_min(TimeGenerated, OperationName) by SessionId | summarize sessions=count() by landedOn=iff(OperationName=='/login','LOGIN','app')" -o json
```

Note: `views` is a KQL keyword — do not use it as a column alias.

- [ ] **Step 6: Close the issue**

Only after Step 4 passes on production:

```bash
gh issue close 5 --repo ThodsaphonSonthiphin/MenuNest \
  --comment "Fixed by the durable app session (ADR-161). Verified on prod: closing the browser no longer forces a re-login."
```

---

## Self-Review

**Spec coverage** — every section of the design spec maps to a task: §3.1 table → Task 1; §3.2 endpoints → Tasks 2, 3, 5; §3.3 issuer branch → Task 4; §4 frontend (both token sites, guard, signOut, handleAuthFailure, graceful degradation) → Tasks 6-7; §5 testing → the test steps in each task plus Task 8 Step 4; §7 risks are recorded in the ADRs.

**Placeholder scan** — every code step contains real, complete code. No "add error handling" or "similar to Task N".

**Type consistency** — `AppSessionTokens(AccessToken, ExpiresIn, RefreshToken)` is produced in Task 3 and consumed by the same names in Tasks 5 and 7 (the SPA reads the camelCase JSON `accessToken` / `expiresIn` / `refreshToken`, pinned by the Task 5 contract test). `AppSessionStore.TakeAsync` returns the subject and is used that way in Task 3. `BearerSchemeSelector.AppIssued` is `"McpProxy"`, matching the scheme registered in `Program.cs:130`. The frontend's `storeAppSession` takes `expiresIn`, exactly what the endpoint returns.
