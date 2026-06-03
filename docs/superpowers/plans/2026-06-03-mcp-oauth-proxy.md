# MCP OAuth Proxy (AS Facade) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a minimal OAuth 2.1 Authorization-Server facade in `MenuNest.WebApi` so claude.ai connects to `/mcp` — it brokers auth to Entra ID (stripping the RFC 8707 `resource` Entra rejects) and mints its own JWT for the MCP endpoint.

**Architecture:** claude.ai talks only to our `/oauth/*` AS (DCR + PKCE + authorize/token). Our AS drives a clean Entra authorization-code+PKCE flow server-side (no `resource` param), keeps the Entra tokens server-side, and returns a short-lived HMAC JWT (`aud=iss=MCP:ServerUrl`) plus an opaque refresh code. `/mcp` validates **our** JWT via a new `"McpProxy"` JwtBearer scheme; the existing `"Microsoft"`/`"Google"` schemes (controllers/SPA) are untouched.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, `System.IdentityModel.Tokens.Jwt`, `IMemoryCache`, xUnit + FluentAssertions. Reference (MIT, adapt + attribute, do not depend): [`Profility-be/mcp-server-dotnet-entra-id`](https://github.com/Profility-be/mcp-server-dotnet-entra-id).

**Design spec:** [docs/superpowers/specs/2026-06-03-mcp-oauth-proxy-design.md](../specs/2026-06-03-mcp-oauth-proxy-design.md)

**Already done — DO NOT REPEAT:** Entra app `e65fd81b-7a28-439b-a2ea-98734b5b5a36` exists in tenant `d500e2f4-1325-41d2-9f92-2f2f39e8ea19`; scope `access_as_user` exposed; client secret `claude-mcp-connector` created (value `<CLIENT_SECRET — from App Service config, never commit>`). The prod App Service is `menunest` (RG `MenuNest`), host `https://menunest.azurewebsites.net`. Deploy = push to remote `main` (GitHub Actions `main_menunest.yml`).

**Pinned claim mapping (spec Risk B):** `CurrentUserService` ([backend/src/MenuNest.Infrastructure/Authentication/CurrentUserService.cs](../../backend/src/MenuNest.Infrastructure/Authentication/CurrentUserService.cs)) reads `oid` (→ `oid` short → `sub`) for `ExternalId` (the user key, used to find the existing user row), `email`/`preferred_username` for `Email`, `name` for `DisplayName`. Our minted JWT MUST carry `oid`, `name`, `preferred_username`, `email`. The `"McpProxy"` scheme must set `MapInboundClaims = false` so these pass through verbatim.

---

## File Structure

All new proxy code under `backend/src/MenuNest.WebApi/Oauth/` (one responsibility per file):

| File | Responsibility |
|---|---|
| `Oauth/PkceUtil.cs` | PKCE: generate verifier, S256 challenge, verify (pure) |
| `Oauth/TokenUtil.cs` | Cryptographically-random opaque tokens (pure) |
| `Oauth/RedirectAllowlist.cs` | Allowed claude redirect URIs (pure) |
| `Oauth/OAuthJwt.cs` | Mint our HMAC JWT + expose `TokenValidationParameters` |
| `Oauth/ClaimExtractor.cs` | Extract `oid`/`name`/`email` from an Entra id_token (pure) |
| `Oauth/OAuthDiscovery.cs` | Build PRM + authorization-server metadata objects (pure) |
| `Oauth/Stores.cs` | `ClientStore`, `PkceStateStore`, `TokenStore` (IMemoryCache singletons) + records |
| `Oauth/EntraClient.cs` | Server-side calls to Entra authorize-URL / token / refresh |
| `Oauth/OAuthEndpoints.cs` | `MapOAuthProxy()` — maps `/oauth/*` + `.well-known/*` |
| `Program.cs` | DI + `"McpProxy"` scheme + `/mcp` policy + `MapOAuthProxy()` |
| `appsettings.json` | Add empty `AzureAd:ClientSecret`, `Jwt:SigningKey`, `MCP:ServerUrl` |

New test project `backend/tests/MenuNest.WebApi.UnitTests/` covers the pure units (PKCE, JWT round-trip, allowlist, claim extraction, discovery docs). Endpoint wiring + Entra calls are verified live (Task 15), matching the spec's verification strategy.

**Locked signatures (used across tasks — keep identical):**
```csharp
PkceUtil.GenerateVerifier() -> string
PkceUtil.Challenge(string verifier) -> string
PkceUtil.Verify(string verifier, string challenge) -> bool
TokenUtil.Opaque(int byteLength = 32) -> string
RedirectAllowlist.IsAllowed(string? redirectUri) -> bool
OAuthJwt(IConfiguration)            // reads Jwt:SigningKey, MCP:ServerUrl
OAuthJwt.Mint(string subject, string clientId, string scope, IEnumerable<Claim> extra, int lifetimeSeconds = 3600) -> string
OAuthJwt.ValidationParameters() -> TokenValidationParameters
ClaimExtractor.FromIdToken(string idToken) -> UserIdentity   // record (Oid, Name, Email)
OAuthDiscovery.ProtectedResource(string baseUrl, string clientId) -> object
OAuthDiscovery.AuthorizationServer(string baseUrl) -> object
ClientStore.Register(string name, string[] redirectUris, string? scope) -> string  // clientId
ClientStore.TryGet(string clientId, out ClientRegistration reg) -> bool
PkceStateStore.Save(PkceFlow flow) -> string   // flowId
PkceStateStore.Take(string flowId) -> PkceFlow?
TokenStore.SaveAuthCode(AuthCodeData data) -> string  // code
TokenStore.TakeAuthCode(string code) -> AuthCodeData?
TokenStore.SaveRefresh(string entraRefreshToken) -> string  // refreshCode
TokenStore.TakeRefresh(string refreshCode) -> string?
EntraClient.BuildAuthorizeUrl(string state, string codeChallenge) -> string
EntraClient.ExchangeCodeAsync(string code, string codeVerifier, CancellationToken) -> EntraTokens
EntraClient.RefreshAsync(string refreshToken, CancellationToken) -> EntraTokens
// records:
UserIdentity(string Oid, string? Name, string? Email)
ClientRegistration(string ClientId, string[] RedirectUris)
PkceFlow(string ClientRedirectUri, string ClientState, string ClientCodeChallenge, string OurVerifier, string Scope)
AuthCodeData(string ClientCodeChallenge, string Subject, string ClientId, string Scope, string? Name, string? Email, string EntraRefreshToken)
EntraTokens(string AccessToken, string? RefreshToken, string? IdToken, int ExpiresIn)
```

---

### Task 1: Test project + PkceUtil (TDD)

**Files:**
- Create: `backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj`
- Create: `backend/src/MenuNest.WebApi/Oauth/PkceUtil.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/PkceUtilTests.cs`

- [ ] **Step 1: Create the test project**

Create `backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="8.9.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MenuNest.WebApi\MenuNest.WebApi.csproj" />
  </ItemGroup>
</Project>
```

Add it to the solution:
```bash
dotnet sln backend/MenuNest.sln add backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj
```

- [ ] **Step 2: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/PkceUtilTests.cs`:
```csharp
using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class PkceUtilTests
{
    [Fact]
    public void Challenge_then_Verify_roundtrips()
    {
        var verifier = PkceUtil.GenerateVerifier();
        var challenge = PkceUtil.Challenge(verifier);

        PkceUtil.Verify(verifier, challenge).Should().BeTrue();
    }

    [Fact]
    public void Verify_fails_for_wrong_verifier()
    {
        var challenge = PkceUtil.Challenge(PkceUtil.GenerateVerifier());

        PkceUtil.Verify(PkceUtil.GenerateVerifier(), challenge).Should().BeFalse();
    }

    [Fact]
    public void Challenge_is_base64url_without_padding_and_matches_known_vector()
    {
        // RFC 7636 Appendix B test vector
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        PkceUtil.Challenge(verifier).Should().Be("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj`
Expected: build failure — `The name 'PkceUtil' does not exist`.

- [ ] **Step 4: Implement PkceUtil**

Create `backend/src/MenuNest.WebApi/Oauth/PkceUtil.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;

namespace MenuNest.WebApi.Oauth;

/// <summary>PKCE (RFC 7636) S256 helpers.</summary>
public static class PkceUtil
{
    public static string GenerateVerifier()
        => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier)
        => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static bool Verify(string verifier, string challenge)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Challenge(verifier)),
            Encoding.ASCII.GetBytes(challenge));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj`
Expected: PASS — 3 tests.

- [ ] **Step 6: Commit**
```bash
git add backend/tests/MenuNest.WebApi.UnitTests backend/src/MenuNest.WebApi/Oauth/PkceUtil.cs backend/MenuNest.sln
git commit -m "feat(oauth): add WebApi test project + PKCE S256 util" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: TokenUtil + RedirectAllowlist (TDD)

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/TokenUtil.cs`, `backend/src/MenuNest.WebApi/Oauth/RedirectAllowlist.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/TokenUtilTests.cs`, `.../RedirectAllowlistTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/TokenUtilTests.cs`:
```csharp
using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class TokenUtilTests
{
    [Fact]
    public void Opaque_is_urlsafe_and_unique()
    {
        var a = TokenUtil.Opaque();
        var b = TokenUtil.Opaque();
        a.Should().NotBe(b);
        a.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }
}
```

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/RedirectAllowlistTests.cs`:
```csharp
using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class RedirectAllowlistTests
{
    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("https://claude.com/api/mcp/auth_callback", true)]
    [InlineData("https://evil.example.com/cb", false)]
    [InlineData("http://claude.ai/api/mcp/auth_callback", false)] // not https / not exact
    [InlineData(null, false)]
    public void IsAllowed_matches_only_known_claude_callbacks(string? uri, bool expected)
        => RedirectAllowlist.IsAllowed(uri).Should().Be(expected);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "Opaque|IsAllowed"`
Expected: build failure — types don't exist.

- [ ] **Step 3: Implement**

Create `backend/src/MenuNest.WebApi/Oauth/TokenUtil.cs`:
```csharp
using System.Security.Cryptography;

namespace MenuNest.WebApi.Oauth;

public static class TokenUtil
{
    public static string Opaque(int byteLength = 32)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

Create `backend/src/MenuNest.WebApi/Oauth/RedirectAllowlist.cs`:
```csharp
namespace MenuNest.WebApi.Oauth;

/// <summary>Exact-match allowlist of MCP client callback URLs (claude.ai / claude.com).</summary>
public static class RedirectAllowlist
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback",
    };

    public static bool IsAllowed(string? redirectUri)
        => redirectUri is not null && Allowed.Contains(redirectUri);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "Opaque|IsAllowed"`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/TokenUtil.cs backend/src/MenuNest.WebApi/Oauth/RedirectAllowlist.cs backend/tests/MenuNest.WebApi.UnitTests/Oauth/TokenUtilTests.cs backend/tests/MenuNest.WebApi.UnitTests/Oauth/RedirectAllowlistTests.cs
git commit -m "feat(oauth): add opaque token generator + redirect allowlist" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: OAuthJwt — mint + validation parameters (TDD)

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/OAuthJwt.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthJwtTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthJwtTests.cs`:
```csharp
using System.Security.Claims;
using FluentAssertions;
using MenuNest.WebApi.Oauth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class OAuthJwtTests
{
    private const string ServerUrl = "https://menunest.azurewebsites.net/mcp";

    private static OAuthJwt Build() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "test-signing-key-please-change-in-prod",
            ["MCP:ServerUrl"] = ServerUrl,
        }).Build());

    [Fact]
    public void Minted_token_validates_with_ValidationParameters_and_carries_claims()
    {
        var sut = Build();
        var token = sut.Mint(
            subject: "oid-123",
            clientId: "client-abc",
            scope: "api://x/access_as_user",
            extra: new[] { new Claim("name", "Pon"), new Claim("email", "pon@x.io") });

        var principal = new JwtSecurityTokenHandler()
            .ValidateToken(token, sut.ValidationParameters(), out _);

        principal.FindFirst("oid")!.Value.Should().Be("oid-123");
        principal.FindFirst("name")!.Value.Should().Be("Pon");
        principal.FindFirst("email")!.Value.Should().Be("pon@x.io");
        principal.FindFirst("aud")!.Value.Should().Be(ServerUrl);
        principal.FindFirst("iss")!.Value.Should().Be(ServerUrl);
    }

    [Fact]
    public void Token_signed_with_different_key_fails_validation()
    {
        var token = Build().Mint("oid-1", "c", "s", Array.Empty<Claim>());
        var other = new OAuthJwt(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Jwt:SigningKey"] = "different", ["MCP:ServerUrl"] = ServerUrl }).Build());

        var act = () => new JwtSecurityTokenHandler().ValidateToken(token, other.ValidationParameters(), out _);
        act.Should().Throw<SecurityTokenInvalidSignatureException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "OAuthJwt"`
Expected: build failure — `OAuthJwt` does not exist.

- [ ] **Step 3: Implement OAuthJwt**

Create `backend/src/MenuNest.WebApi/Oauth/OAuthJwt.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MenuNest.WebApi.Oauth;

/// <summary>
/// Mints and validates the proxy's own access token. aud == iss == MCP:ServerUrl
/// (RFC 8707 audience binding). HMAC-SHA256, key derived from Jwt:SigningKey.
/// Adapted from the MIT-licensed Profility/mcp-server-dotnet-entra-id reference.
/// </summary>
public sealed class OAuthJwt
{
    private readonly string _serverUrl;
    private readonly SymmetricSecurityKey _key;

    public OAuthJwt(Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _serverUrl = config["MCP:ServerUrl"]
            ?? throw new InvalidOperationException("MCP:ServerUrl is not configured.");
        var signingKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");
        _key = new SymmetricSecurityKey(SHA256.HashData(Encoding.UTF8.GetBytes(signingKey)));
    }

    public string Mint(string subject, string clientId, string scope, IEnumerable<Claim> extra, int lifetimeSeconds = 3600)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("oid", subject),
            new("client_id", clientId),
            new("scope", scope),
            new("jti", Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(extra);

        var token = new JwtSecurityToken(
            issuer: _serverUrl,
            audience: _serverUrl,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(lifetimeSeconds),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = _serverUrl,
        ValidAudience = _serverUrl,
        IssuerSigningKey = _key,
        ClockSkew = TimeSpan.FromMinutes(5),
    };
}
```

> Note: the test uses `new Claim("oid",...)` is set by `Mint` from `subject`; the `extra` claims add `name`/`email`. `JwtSecurityTokenHandler.ValidateToken` maps inbound claim types by default, but the test reads short names (`oid`,`name`,`email`,`aud`,`iss`) which are not remapped, so assertions hold.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "OAuthJwt"`
Expected: PASS — 2 tests.

- [ ] **Step 5: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/OAuthJwt.cs backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthJwtTests.cs
git commit -m "feat(oauth): mint+validate proxy JWT (HMAC, aud=iss=server url)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: ClaimExtractor (TDD)

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/ClaimExtractor.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/ClaimExtractorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/ClaimExtractorTests.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class ClaimExtractorTests
{
    private static string MakeIdToken(params Claim[] claims)
        => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: claims));

    [Fact]
    public void FromIdToken_reads_oid_name_and_email()
    {
        var idToken = MakeIdToken(
            new Claim("oid", "obj-1"),
            new Claim("name", "Pon"),
            new Claim("preferred_username", "pon@x.io"));

        var id = ClaimExtractor.FromIdToken(idToken);

        id.Oid.Should().Be("obj-1");
        id.Name.Should().Be("Pon");
        id.Email.Should().Be("pon@x.io");
    }

    [Fact]
    public void FromIdToken_prefers_email_claim_then_falls_back_to_preferred_username()
    {
        var idToken = MakeIdToken(new Claim("oid", "o"), new Claim("email", "real@x.io"),
            new Claim("preferred_username", "upn@x.io"));
        ClaimExtractor.FromIdToken(idToken).Email.Should().Be("real@x.io");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "ClaimExtractor"`
Expected: build failure.

- [ ] **Step 3: Implement**

Create `backend/src/MenuNest.WebApi/Oauth/ClaimExtractor.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;

namespace MenuNest.WebApi.Oauth;

public record UserIdentity(string Oid, string? Name, string? Email);

/// <summary>Pulls the identity claims MenuNest needs out of an Entra id_token.</summary>
public static class ClaimExtractor
{
    public static UserIdentity FromIdToken(string idToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
        string? Get(string t) => jwt.Claims.FirstOrDefault(c => c.Type == t)?.Value;

        var oid = Get("oid") ?? Get("http://schemas.microsoft.com/identity/claims/objectidentifier") ?? Get("sub")
            ?? throw new InvalidOperationException("id_token has no oid/sub claim.");
        return new UserIdentity(oid, Get("name"), Get("email") ?? Get("preferred_username"));
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "ClaimExtractor"`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/ClaimExtractor.cs backend/tests/MenuNest.WebApi.UnitTests/Oauth/ClaimExtractorTests.cs
git commit -m "feat(oauth): extract oid/name/email from Entra id_token" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: OAuthDiscovery — PRM + AS metadata (TDD)

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/OAuthDiscovery.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthDiscoveryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthDiscoveryTests.cs`:
```csharp
using System.Text.Json;
using FluentAssertions;
using MenuNest.WebApi.Oauth;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class OAuthDiscoveryTests
{
    private const string Base = "https://menunest.azurewebsites.net";
    private const string Cid = "e65fd81b-7a28-439b-a2ea-98734b5b5a36";

    private static JsonElement Json(object o)
        => JsonSerializer.SerializeToElement(o);

    [Fact]
    public void ProtectedResource_points_authorization_servers_at_this_server()
    {
        var j = Json(OAuthDiscovery.ProtectedResource(Base, Cid));
        j.GetProperty("resource").GetString().Should().Be($"{Base}/mcp");
        j.GetProperty("authorization_servers")[0].GetString().Should().Be(Base);
        j.GetProperty("scopes_supported")[0].GetString().Should().Be($"api://{Cid}/access_as_user");
    }

    [Fact]
    public void AuthorizationServer_advertises_our_oauth_endpoints_and_dcr()
    {
        var j = Json(OAuthDiscovery.AuthorizationServer(Base));
        j.GetProperty("issuer").GetString().Should().Be(Base);
        j.GetProperty("authorization_endpoint").GetString().Should().Be($"{Base}/oauth/authorize");
        j.GetProperty("token_endpoint").GetString().Should().Be($"{Base}/oauth/token");
        j.GetProperty("registration_endpoint").GetString().Should().Be($"{Base}/oauth/register");
        j.GetProperty("code_challenge_methods_supported")[0].GetString().Should().Be("S256");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "OAuthDiscovery"`
Expected: build failure.

- [ ] **Step 3: Implement**

Create `backend/src/MenuNest.WebApi/Oauth/OAuthDiscovery.cs`:
```csharp
namespace MenuNest.WebApi.Oauth;

/// <summary>RFC 9728 (protected resource) + RFC 8414 (authorization server) metadata documents.</summary>
public static class OAuthDiscovery
{
    public static object ProtectedResource(string baseUrl, string clientId) => new
    {
        resource = $"{baseUrl}/mcp",
        authorization_servers = new[] { baseUrl },
        scopes_supported = new[] { $"api://{clientId}/access_as_user", "openid", "profile", "email" },
        bearer_methods_supported = new[] { "header" },
    };

    public static object AuthorizationServer(string baseUrl) => new
    {
        issuer = baseUrl,
        authorization_endpoint = $"{baseUrl}/oauth/authorize",
        token_endpoint = $"{baseUrl}/oauth/token",
        registration_endpoint = $"{baseUrl}/oauth/register",
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code", "refresh_token" },
        code_challenge_methods_supported = new[] { "S256" },
        token_endpoint_auth_methods_supported = new[] { "none" },
    };
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "OAuthDiscovery"`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/OAuthDiscovery.cs backend/tests/MenuNest.WebApi.UnitTests/Oauth/OAuthDiscoveryTests.cs
git commit -m "feat(oauth): PRM + authorization-server discovery docs pointing at proxy" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Stores + records (TDD for ClientStore)

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/Stores.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Oauth/ClientStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Oauth/ClientStoreTests.cs`:
```csharp
using FluentAssertions;
using MenuNest.WebApi.Oauth;
using Microsoft.Extensions.Caching.Memory;

namespace MenuNest.WebApi.UnitTests.Oauth;

public class ClientStoreTests
{
    private static ClientStore New() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void Register_then_TryGet_returns_redirect_uris()
    {
        var store = New();
        var clientId = store.Register("claude", new[] { "https://claude.ai/api/mcp/auth_callback" }, null);

        store.TryGet(clientId, out var reg).Should().BeTrue();
        reg.RedirectUris.Should().ContainSingle().Which.Should().Be("https://claude.ai/api/mcp/auth_callback");
    }

    [Fact]
    public void TryGet_unknown_client_returns_false()
        => New().TryGet("nope", out _).Should().BeFalse();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "ClientStore"`
Expected: build failure.

- [ ] **Step 3: Implement Stores.cs**

Create `backend/src/MenuNest.WebApi/Oauth/Stores.cs`:
```csharp
using Microsoft.Extensions.Caching.Memory;

namespace MenuNest.WebApi.Oauth;

public record ClientRegistration(string ClientId, string[] RedirectUris);
public record PkceFlow(string ClientRedirectUri, string ClientState, string ClientCodeChallenge, string OurVerifier, string Scope);
public record AuthCodeData(string ClientCodeChallenge, string Subject, string ClientId, string Scope, string? Name, string? Email, string EntraRefreshToken);

/// <summary>DCR client registrations. In-memory; single-instance assumption (see ADR-003).</summary>
public sealed class ClientStore(IMemoryCache cache)
{
    public string Register(string name, string[] redirectUris, string? scope)
    {
        var clientId = TokenUtil.Opaque(16);
        cache.Set($"client:{clientId}", new ClientRegistration(clientId, redirectUris),
            TimeSpan.FromDays(30));
        return clientId;
    }

    public bool TryGet(string clientId, out ClientRegistration reg)
        => cache.TryGetValue($"client:{clientId}", out reg!);
}

/// <summary>Authorize→callback flow state. Short-lived, single-use.</summary>
public sealed class PkceStateStore(IMemoryCache cache)
{
    public string Save(PkceFlow flow)
    {
        var id = TokenUtil.Opaque();
        cache.Set($"flow:{id}", flow, TimeSpan.FromMinutes(10));
        return id;
    }

    public PkceFlow? Take(string flowId)
    {
        if (cache.TryGetValue($"flow:{flowId}", out PkceFlow? flow)) { cache.Remove($"flow:{flowId}"); return flow; }
        return null;
    }
}

/// <summary>Proxy auth codes (60s, single-use) and opaque refresh codes → Entra refresh token.</summary>
public sealed class TokenStore(IMemoryCache cache)
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

    public string SaveRefresh(string entraRefreshToken)
    {
        var refreshCode = TokenUtil.Opaque();
        cache.Set($"refresh:{refreshCode}", entraRefreshToken, TimeSpan.FromDays(30));
        return refreshCode;
    }

    public string? TakeRefresh(string refreshCode)
    {
        if (cache.TryGetValue($"refresh:{refreshCode}", out string? rt)) { cache.Remove($"refresh:{refreshCode}"); return rt; }
        return null;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj --filter "ClientStore"`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/Stores.cs backend/tests/MenuNest.WebApi.UnitTests/Oauth/ClientStoreTests.cs
git commit -m "feat(oauth): in-memory client/pkce/token stores + records" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: EntraClient (server-side Entra calls)

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/EntraClient.cs`

No unit test (external HTTP + secrets); verified in the Task 15 controlled repro.

- [ ] **Step 1: Implement EntraClient**

Create `backend/src/MenuNest.WebApi/Oauth/EntraClient.cs`:
```csharp
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MenuNest.WebApi.Oauth;

public record EntraTokens(string AccessToken, string? RefreshToken, string? IdToken, int ExpiresIn);

/// <summary>Drives the clean Entra authorization-code+PKCE flow server-side — NO RFC 8707 resource param.</summary>
public sealed class EntraClient(HttpClient http, IConfiguration config)
{
    private string Instance => config["AzureAd:Instance"]!.TrimEnd('/');
    private string Tenant => config["AzureAd:TenantId"]!;
    private string ClientId => config["AzureAd:ClientId"]!;
    private string ClientSecret => config["AzureAd:ClientSecret"]!;
    private string ServerBase => config["MCP:ServerUrl"]!.Replace("/mcp", "");
    private string Callback => $"{ServerBase}/oauth/callback";
    private string Scope => $"api://{ClientId}/access_as_user offline_access openid profile email";

    public string BuildAuthorizeUrl(string state, string codeChallenge)
        => $"{Instance}/{Tenant}/oauth2/v2.0/authorize"
         + $"?client_id={Uri.EscapeDataString(ClientId)}"
         + "&response_type=code"
         + $"&redirect_uri={Uri.EscapeDataString(Callback)}"
         + $"&scope={Uri.EscapeDataString(Scope)}"
         + $"&state={Uri.EscapeDataString(state)}"
         + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
         + "&code_challenge_method=S256"
         + "&prompt=select_account";

    public Task<EntraTokens> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
        => PostAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = Callback,
            ["code_verifier"] = codeVerifier,
            ["scope"] = Scope,
        }, ct);

    public Task<EntraTokens> RefreshAsync(string refreshToken, CancellationToken ct)
        => PostAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope,
        }, ct);

    private async Task<EntraTokens> PostAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var url = $"{Instance}/{Tenant}/oauth2/v2.0/token";
        using var resp = await http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Entra token endpoint failed ({(int)resp.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new EntraTokens(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            root.TryGetProperty("id_token", out var it) ? it.GetString() : null,
            root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600);
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/EntraClient.cs
git commit -m "feat(oauth): Entra client (authorize url, code exchange, refresh) without resource param" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: OAuthEndpoints scaffold + `/oauth/register` + discovery docs

**Files:**
- Create: `backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs`

- [ ] **Step 1: Implement the endpoint module with `/register` + the two well-known docs**

Create `backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs`:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Oauth;

public static class OAuthEndpoints
{
    private static string BaseUrl(HttpRequest r) => $"{r.Scheme}://{r.Host}";

    public static void MapOAuthProxy(this WebApplication app)
    {
        var clientId = app.Configuration["AzureAd:ClientId"]!;

        // --- Discovery (anonymous) ---
        app.MapGet("/.well-known/oauth-protected-resource",
            (HttpRequest r) => Results.Ok(OAuthDiscovery.ProtectedResource(BaseUrl(r), clientId))).AllowAnonymous();
        app.MapGet("/.well-known/oauth-protected-resource/mcp",
            (HttpRequest r) => Results.Ok(OAuthDiscovery.ProtectedResource(BaseUrl(r), clientId))).AllowAnonymous();
        app.MapGet("/.well-known/oauth-authorization-server",
            (HttpRequest r) => Results.Ok(OAuthDiscovery.AuthorizationServer(BaseUrl(r)))).AllowAnonymous();

        // --- DCR (RFC 7591) ---
        app.MapPost("/oauth/register", (DcrRequest body, ClientStore clients) =>
        {
            var uris = body.redirect_uris ?? Array.Empty<string>();
            if (uris.Length == 0 || uris.Any(u => !RedirectAllowlist.IsAllowed(u)))
                return Results.BadRequest(new { error = "invalid_redirect_uri" });

            var id = clients.Register(body.client_name ?? "mcp-client", uris, body.scope);
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
    }

    public record DcrRequest(string[]? redirect_uris, string? client_name, string? scope);
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs
git commit -m "feat(oauth): endpoint module — discovery docs + DCR /oauth/register" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: `/oauth/authorize`

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs`

- [ ] **Step 1: Add the authorize endpoint inside `MapOAuthProxy` (after `/oauth/register`)**

```csharp
        // --- Authorize: validate, store flow, redirect to Entra (NO resource param) ---
        app.MapGet("/oauth/authorize", (
            [FromQuery] string client_id,
            [FromQuery] string redirect_uri,
            [FromQuery] string code_challenge,
            [FromQuery] string? code_challenge_method,
            [FromQuery] string? state,
            [FromQuery] string? scope,
            ClientStore clients, PkceStateStore flows, EntraClient entra) =>
        {
            if (!clients.TryGet(client_id, out var reg) || !reg.RedirectUris.Contains(redirect_uri))
                return Results.BadRequest(new { error = "invalid_client" });
            if (!RedirectAllowlist.IsAllowed(redirect_uri))
                return Results.BadRequest(new { error = "invalid_redirect_uri" });
            if (!string.Equals(code_challenge_method, "S256", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "invalid_request", error_description = "code_challenge_method must be S256" });

            var ourVerifier = PkceUtil.GenerateVerifier();
            var flowId = flows.Save(new PkceFlow(redirect_uri, state ?? "", code_challenge, ourVerifier, scope ?? ""));
            return Results.Redirect(entra.BuildAuthorizeUrl(flowId, PkceUtil.Challenge(ourVerifier)));
        }).AllowAnonymous();
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs
git commit -m "feat(oauth): /oauth/authorize — store flow, redirect to Entra resource-free" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: `/oauth/callback`

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs`

- [ ] **Step 1: Add the callback endpoint inside `MapOAuthProxy`**

```csharp
        // --- Callback: Entra returns code -> exchange -> store -> redirect proxy code to claude ---
        app.MapGet("/oauth/callback", async (
            [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error,
            PkceStateStore flows, TokenStore tokens, EntraClient entra, CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(error)) return Results.BadRequest(new { error });
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)) return Results.BadRequest(new { error = "invalid_request" });

            var flow = flows.Take(state);
            if (flow is null) return Results.BadRequest(new { error = "invalid_state" });

            var entraTokens = await entra.ExchangeCodeAsync(code, flow.OurVerifier, ct);
            if (string.IsNullOrEmpty(entraTokens.RefreshToken) || string.IsNullOrEmpty(entraTokens.IdToken))
                return Results.BadRequest(new { error = "server_error", error_description = "Entra did not return refresh/id token" });

            var id = ClaimExtractor.FromIdToken(entraTokens.IdToken);
            var proxyCode = tokens.SaveAuthCode(new AuthCodeData(
                ClientCodeChallenge: flow.ClientCodeChallenge,
                Subject: id.Oid, ClientId: "", Scope: flow.Scope,
                Name: id.Name, Email: id.Email, EntraRefreshToken: entraTokens.RefreshToken));

            var sep = flow.ClientRedirectUri.Contains('?') ? '&' : '?';
            var target = $"{flow.ClientRedirectUri}{sep}code={Uri.EscapeDataString(proxyCode)}&state={Uri.EscapeDataString(flow.ClientState)}";
            return Results.Redirect(target);
        }).AllowAnonymous();
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs
git commit -m "feat(oauth): /oauth/callback — exchange Entra code, map claims, mint proxy code" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: `/oauth/token` (authorization_code + refresh_token)

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs`

- [ ] **Step 1: Add the token endpoint inside `MapOAuthProxy`**

```csharp
        // --- Token: mint our JWT (authorization_code) or refresh ---
        app.MapPost("/oauth/token", async (
            HttpRequest req, TokenStore tokens, EntraClient entra, OAuthJwt jwt, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var grant = form["grant_type"].ToString();

            if (grant == "authorization_code")
            {
                var code = form["code"].ToString();
                var verifier = form["code_verifier"].ToString();
                var data = tokens.TakeAuthCode(code);
                if (data is null) return Results.BadRequest(new { error = "invalid_grant" });
                if (!PkceUtil.Verify(verifier, data.ClientCodeChallenge))
                    return Results.BadRequest(new { error = "invalid_grant", error_description = "PKCE failed" });

                return Results.Ok(IssueTokens(jwt, tokens, data.Subject, form["client_id"].ToString(), data.Scope, data.Name, data.Email, data.EntraRefreshToken));
            }

            if (grant == "refresh_token")
            {
                var refreshCode = form["refresh_token"].ToString();
                var entraRt = tokens.TakeRefresh(refreshCode);
                if (entraRt is null) return Results.BadRequest(new { error = "invalid_grant" });

                var refreshed = await entra.RefreshAsync(entraRt, ct);
                var newEntraRt = refreshed.RefreshToken ?? entraRt;
                var id = refreshed.IdToken is not null ? ClaimExtractor.FromIdToken(refreshed.IdToken) : null;
                // Subject must be stable; re-read from refreshed id_token when present, else fail safe.
                if (id is null) return Results.BadRequest(new { error = "invalid_grant", error_description = "no id_token on refresh" });
                return Results.Ok(IssueTokens(jwt, tokens, id.Oid, form["client_id"].ToString(), "", id.Name, id.Email, newEntraRt));
            }

            return Results.BadRequest(new { error = "unsupported_grant_type" });
        }).AllowAnonymous();
```

Add this private helper to the `OAuthEndpoints` class (outside `MapOAuthProxy`):
```csharp
    private static object IssueTokens(OAuthJwt jwt, TokenStore tokens, string subject, string clientId,
        string scope, string? name, string? email, string entraRefreshToken)
    {
        var extra = new List<System.Security.Claims.Claim>();
        if (name is not null) extra.Add(new("name", name));
        if (email is not null) { extra.Add(new("email", email)); extra.Add(new("preferred_username", email)); }

        var accessToken = jwt.Mint(subject, clientId, scope, extra);
        var refreshCode = tokens.SaveRefresh(entraRefreshToken);
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

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**
```bash
git add backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs
git commit -m "feat(oauth): /oauth/token — mint proxy JWT + refresh via Entra" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: Program.cs wiring — DI, McpProxy scheme, /mcp policy, map endpoints

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Program.cs`

- [ ] **Step 1: Register services + the McpProxy auth scheme**

In `Program.cs`, after `builder.Services.AddMenuNestMcpServer();` (line ~26) add:
```csharp
// MCP OAuth proxy (AS facade) — see docs/adr/003-mcp-oauth-proxy.md
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<MenuNest.WebApi.Oauth.EntraClient>();
builder.Services.AddSingleton<MenuNest.WebApi.Oauth.ClientStore>();
builder.Services.AddSingleton<MenuNest.WebApi.Oauth.PkceStateStore>();
builder.Services.AddSingleton<MenuNest.WebApi.Oauth.TokenStore>();
builder.Services.AddSingleton<MenuNest.WebApi.Oauth.OAuthJwt>();
```

In the `AddAuthentication("MultiAuth")` chain, after the `.AddJwtBearer("Google", …)` block add a third scheme:
```csharp
    .AddJwtBearer("McpProxy", options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters =
            new MenuNest.WebApi.Oauth.OAuthJwt(builder.Configuration).ValidationParameters();
    });
```

In `AddAuthorization(...)`, add a policy bound to that scheme (alongside the existing `FallbackPolicy`):
```csharp
    options.AddPolicy("McpProxy", policy =>
    {
        policy.AddAuthenticationSchemes("McpProxy");
        policy.RequireAuthenticatedUser();
    });
```

- [ ] **Step 2: Point `/mcp` at the McpProxy policy and remove the old hand-rolled well-known endpoints**

Replace the existing line:
```csharp
app.MapMcp("/mcp").RequireAuthorization();
```
with:
```csharp
app.MapMcp("/mcp").RequireAuthorization("McpProxy");
```

Delete the old hand-rolled blocks (the `/.well-known/oauth-authorization-server` MapGet and the `/.well-known/oauth-protected-resource[/mcp]` MapGet added earlier this session) — they are replaced by `MapOAuthProxy()`.

Immediately before `app.Run();` add:
```csharp
app.MapOAuthProxy();
```

Add `using MenuNest.WebApi.Oauth;` to the usings if you prefer unqualified `MapOAuthProxy()` (otherwise call `MenuNest.WebApi.Oauth.OAuthEndpoints.MapOAuthProxy(app)`).

- [ ] **Step 3: Build + run full WebApi-related tests**

Run: `dotnet build backend/MenuNest.sln`
Expected: Build succeeded, 0 errors.
Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests/MenuNest.WebApi.UnitTests.csproj`
Expected: PASS (all Oauth unit tests).

- [ ] **Step 4: Commit**
```bash
git add backend/src/MenuNest.WebApi/Program.cs
git commit -m "feat(oauth): wire proxy DI, McpProxy JWT scheme, /mcp policy, map endpoints" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 13: appsettings keys + ADR-003

**Files:**
- Modify: `backend/src/MenuNest.WebApi/appsettings.json`
- Create: `docs/adr/003-mcp-oauth-proxy.md`

- [ ] **Step 1: Add config keys (empty defaults) to appsettings.json**

In `backend/src/MenuNest.WebApi/appsettings.json`, add `ClientSecret` to the `AzureAd` object and two new top-level sections:
```jsonc
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "common",
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "ClientSecret": "",
    "Audience": "00000000-0000-0000-0000-000000000000"
  },
  "Jwt": { "SigningKey": "" },
  "MCP": { "ServerUrl": "https://menunest.azurewebsites.net/mcp" },
```
(Keep the existing `_AudienceComment` if present; only add the `ClientSecret` line + the `Jwt`/`MCP` sections.)

- [ ] **Step 2: Create ADR-003**

Create `docs/adr/003-mcp-oauth-proxy.md`:
```markdown
# ADR-003: MenuNest hosts an OAuth Authorization-Server facade to broker MCP auth to Entra

**Date:** 2026-06-03
**Status:** Accepted
**Supersedes (in part):** ADR-002 ("no DCR, no custom OAuth server logic, manual client credentials")

## Context

claude.ai (per the MCP authorization spec) sends an RFC 8707 `resource` parameter equal
to the MCP server URL to the authorization server. Entra ID v2 cannot resolve a bare URL
as a resource (it requires a registered Application ID URI) and returns AADSTS500011; the
URL cannot be registered (no verified domain). This was confirmed empirically: a token
exchange without `resource` succeeds, with `resource=<url>` fails. claude.ai derives the
value from the server URL, not from our metadata, so no metadata tweak fixes it. Microsoft
itself documents that non-Microsoft clients can't use Entra directly because Entra lacks
DCR; its recommended fix is Azure API Management (~$700/mo — rejected on cost).

## Decision

Host a minimal OAuth 2.1 Authorization-Server facade inside MenuNest.WebApi (`/oauth/*` +
the two `.well-known` docs). claude.ai talks only to our AS (DCR + PKCE). Our AS runs a
clean Entra authorization-code+PKCE flow server-side (no `resource`), keeps Entra tokens
server-side, and mints its own HMAC JWT (`aud=iss=MCP:ServerUrl`) for `/mcp`, plus an
opaque refresh code. `/mcp` validates our JWT via a new `McpProxy` JwtBearer scheme.
Approach and structure adapted from the MIT `Profility-be/mcp-server-dotnet-entra-id`
reference (tested with Claude). DCR is supported (zero-config connect), reversing ADR-002.

## Consequences

**Positive:** claude.ai web/mobile can connect; zero-config (no manual client id/secret);
Entra tokens never leave the server; no extra Azure cost; controllers' existing auth
unchanged.

**Negative:** We now run security-sensitive OAuth code (PKCE, code single-use, redirect
allowlist enforced). In-memory stores assume a single App Service instance (scale-out
needs durable stores — the reference's AzureTable variants). A JWT signing key and the
Entra client secret live in App Service config (move to Key Vault later). Only Entra
identities work (per ADR-001).
```

- [ ] **Step 3: Build (config still parses) + commit**

Run: `dotnet build backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`
Expected: Build succeeded.
```bash
git add backend/src/MenuNest.WebApi/appsettings.json docs/adr/003-mcp-oauth-proxy.md
git commit -m "docs(oauth): add ADR-003 + config keys for the OAuth proxy" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 14: Azure / Entra infrastructure config (one-time)

**Files:** none (Azure CLI). Requires `az` logged into the personal tenant.

- [ ] **Step 1: Add our callback as an Entra Web redirect URI (replacing the now-unused claude ones)**

Run:
```bash
az ad app update --id e65fd81b-7a28-439b-a2ea-98734b5b5a36 \
  --web-redirect-uris "https://menunest.azurewebsites.net/oauth/callback"
```
Verify:
```bash
az ad app show --id e65fd81b-7a28-439b-a2ea-98734b5b5a36 --query "web.redirectUris" -o json
```
Expected: `["https://menunest.azurewebsites.net/oauth/callback"]`.

- [ ] **Step 2: Set App Service settings (secret, signing key, server url)**

Generate a signing key and set all three (PowerShell):
```powershell
$key = [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 }))
az webapp config appsettings set --name menunest --resource-group MenuNest --settings `
  "AzureAd__ClientSecret=<CLIENT_SECRET — from App Service config, never commit>" `
  "Jwt__SigningKey=$key" `
  "MCP__ServerUrl=https://menunest.azurewebsites.net/mcp"
```
Verify (names only):
```bash
az webapp config appsettings list --name menunest --resource-group MenuNest --query "[?name=='Jwt__SigningKey' || name=='MCP__ServerUrl' || name=='AzureAd__ClientSecret'].name" -o json
```
Expected: all three names listed. (Setting app settings restarts the app.)

- [ ] **Step 3: No commit** (infra change). Note completion in the task log.

---

### Task 15: Deploy + verify (probe → controlled repro → claude.ai)

**Files:** none — deployment + live verification. No success claim until the claude.ai reconnect passes.

- [ ] **Step 1: Push to deploy**
```bash
git push main main
```
Expected: GitHub Actions `main_menunest.yml` runs to `completed success`; App Service restarts.

- [ ] **Step 2: Probe discovery (PowerShell — Bash sandbox blocks outbound)**
```powershell
$b = "https://menunest.azurewebsites.net"
$prm = (Invoke-WebRequest "$b/.well-known/oauth-protected-resource" -UseBasicParsing -TimeoutSec 40).Content | ConvertFrom-Json
"authz_servers = $($prm.authorization_servers -join ',')"   # expect: https://menunest.azurewebsites.net
$as = (Invoke-WebRequest "$b/.well-known/oauth-authorization-server" -UseBasicParsing -TimeoutSec 40).Content | ConvertFrom-Json
"registration = $($as.registration_endpoint)"               # expect: .../oauth/register
```
Expected: `authorization_servers` = the server base; `registration_endpoint` = `.../oauth/register`.

- [ ] **Step 3: Confirm DCR + authorize-redirect strip `resource`**
```powershell
$reg = Invoke-WebRequest "$b/oauth/register" -Method POST -ContentType "application/json" `
  -Body '{"redirect_uris":["https://claude.ai/api/mcp/auth_callback"],"client_name":"probe"}' -UseBasicParsing
$cid = ($reg.Content | ConvertFrom-Json).client_id
$cc = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
try {
  Invoke-WebRequest "$b/oauth/authorize?client_id=$cid&redirect_uri=https%3A%2F%2Fclaude.ai%2Fapi%2Fmcp%2Fauth_callback&code_challenge=$cc&code_challenge_method=S256&state=x" -UseBasicParsing -MaximumRedirection 0
} catch { $_.Exception.Response.Headers.Location.ToString() }
```
Expected: a 302 `Location` to `login.microsoftonline.com/.../authorize?...` that contains `scope=...access_as_user...` and **does NOT** contain `resource=`.

- [ ] **Step 4: Controlled end-to-end repro through OUR proxy (before claude.ai)**

Open the `Location` URL from Step 3 in a browser, sign in, and copy the `http(s)://claude.ai/...auth_callback?code=...` URL it bounces to (the browser will fail to load claude's callback in this manual test — copy the `code` from the address bar). Then redeem it at OUR token endpoint with the matching verifier (the verifier whose S256 challenge is `E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM` is `dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk`):
```powershell
$body = "grant_type=authorization_code&client_id=$cid&code=<PROXY_CODE>&code_verifier=dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk&redirect_uri=https://claude.ai/api/mcp/auth_callback"
$tok = (Invoke-WebRequest "$b/oauth/token" -Method POST -Body $body -ContentType "application/x-www-form-urlencoded" -UseBasicParsing).Content | ConvertFrom-Json
$tok.token_type   # Bearer
# decode the access_token payload and confirm aud=server url, oid present:
$p = $tok.access_token.Split('.')[1].Replace('-','+').Replace('_','/'); switch ($p.Length % 4){2{$p+='=='}3{$p+='='}}
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p))
```
Expected: a token response; the JWT payload shows `"aud":"https://menunest.azurewebsites.net/mcp"`, `"iss":"…/mcp"`, and an `"oid"` claim. This proves the whole proxy works before involving claude.ai.

- [ ] **Step 5: claude.ai reconnect**

Remove and re-add the MenuNest connector on claude.ai with URL `https://menunest.azurewebsites.net/mcp` (no client id/secret needed — DCR is now supported). Complete the Microsoft login. Expected: **Connected**, tools list (Recipe/Ingredient/MealPlan/Stock/ShoppingList/Budget), one read-only tool call (e.g. `list_ingredients`) succeeds and resolves the correct user.

- [ ] **Step 6: Record outcome.** Done only when Step 5 passes. If tools authenticate but data is wrong/empty, suspect claim mapping (compare the `oid` in the Step 4 JWT to the existing user's `ExternalId`).

---

## Self-Review

**Spec coverage:**
- D1 hand-roll in WebApi → Tasks 1–12. ✓
- D2 DCR → Task 8 (`/oauth/register`). ✓
- D3 mint-own JWT (`aud=iss=ServerUrl`), Entra tokens server-side → Tasks 3, 10, 11; McpProxy scheme Task 12. ✓
- D4 refresh → Task 11 (`refresh_token` grant) + TokenStore refresh map (Task 6). ✓
- D5 in-memory stores + tenant-specific Entra → Task 6 + EntraClient `{Tenant}` (Task 7). ✓
- D6 ignore claude resource/scope, no `resource` to Entra → Task 7 `BuildAuthorizeUrl`/`ExchangeCodeAsync` (no resource), verified Task 15 Step 3. ✓
- D7 adapt Profility w/ attribution, skip login page → headers note attribution; no `/continue`/Views. ✓
- Claim mapping (Risk B) → Tasks 4, 10, 11 (`oid`/`name`/`email` into JWT); `MapInboundClaims=false` (Task 12). ✓
- Discovery docs revert/repoint → Task 5 + Task 12 (delete old blocks). ✓
- Config + ADR + infra → Tasks 13, 14. ✓
- Verification (probe → controlled repro → reconnect) → Task 15. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; commands have expected output. The only `<PROXY_CODE>` is a runtime value the operator pastes during the manual repro (Task 15 Step 4), not a code placeholder.

**Type consistency:** Signatures in the locked-signatures block match their definitions (Tasks 1–7) and call sites (Tasks 8–12): `PkceUtil.Verify`, `OAuthJwt.Mint`/`ValidationParameters`, `ClaimExtractor.FromIdToken`→`UserIdentity(Oid,Name,Email)`, `ClientStore.TryGet(out ClientRegistration)`, `PkceStateStore.Save/Take(PkceFlow)`, `TokenStore.SaveAuthCode/TakeAuthCode(AuthCodeData)`/`SaveRefresh/TakeRefresh`, `EntraClient.BuildAuthorizeUrl/ExchangeCodeAsync/RefreshAsync`→`EntraTokens`. Consistent.
