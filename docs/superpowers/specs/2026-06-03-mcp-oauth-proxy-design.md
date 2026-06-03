# MenuNest MCP OAuth Proxy (AS Facade) — Design Spec

**Date:** 2026-06-03
**Status:** Implemented (2026-06-03)
**Scope:** Make claude.ai (web/mobile) connect to the MenuNest MCP server by interposing a minimal OAuth 2.1 Authorization-Server facade — hosted in `MenuNest.WebApi` — that brokers auth to Entra ID, absorbs the RFC 8707 `resource` parameter Entra cannot handle, and mints its own short-lived JWT for the MCP endpoint.
**Supersedes (in part):** ADR-002 ("no DCR, no custom OAuth server logic, manual client credentials"). A new **ADR-003** must record the reversal.
**Reference implementation:** [`Profility-be/mcp-server-dotnet-entra-id`](https://github.com/Profility-be/mcp-server-dotnet-entra-id) (MIT, **tested with Claude AI**). We adapt its proven structure with attribution; we do **not** take a dependency (it's a sample, not a package).

---

## Problem (confirmed this session)

claude.ai implements the MCP authorization spec, which **mandates** an RFC 8707 `resource` parameter (= the canonical MCP server URL, `https://menunest.azurewebsites.net/mcp`) at `/authorize` and `/token`. Entra v2 cannot resolve a bare URL as a resource (needs a registered Application ID URI) → **`AADSTS500011`**. The URL **cannot** be registered (Entra requires a verified domain; `azurewebsites.net` isn't ours).

**Proven:** a token exchange without the `resource` param succeeds (valid token, `aud=e65fd81b…`); with `resource=<URL>` it fails (`500011`). claude.ai derives `resource` from the **server URL**, ignoring our PRM `resource` field (verified — changing PRM `resource` to `api://…` did not help). Network capture confirms claude sends `resource=https://menunest.azurewebsites.net/mcp` to Entra.

We can't change what claude sends, and can't make Entra accept the URL. **Only fix: our own AS in front of Entra** that builds clean requests to Entra (no `resource`). This is the ecosystem-standard solution, confirmed by the dhanush how-to and the Profility .NET reference (both tested with Claude); Microsoft's own answer is APIM (rejected on cost ≈ $700/mo).

---

## Decisions

| # | Decision | Rationale |
|---|---|---|
| D1 | **Hand-roll** the AS facade as endpoints in `MenuNest.WebApi` | $0 infra. APIM tier supporting MCP/OAuth brokering ≈ $700/mo. |
| D2 | **Support DCR** (`/oauth/register`) → zero-config connect | claude.ai natively does DCR; removes the manual Client ID/Secret paste that kept failing. |
| D3 | **Mint our own JWT** (HMAC-SHA256; `aud = iss = MCP server URL`); keep Entra tokens **server-side**; claude gets our JWT + an **opaque** refresh code | Matches the proven Claude-tested pattern; strict RFC 8707 audience binding; Entra tokens never leave the server. (Chosen over pass-through to avoid another claude.ai debug cycle and the small risk that claude validates `aud`=resource.) |
| D4 | **Support refresh** — claude's opaque refresh code maps (server-side) to the Entra refresh token; `/oauth/token` refreshes against Entra and re-mints | Entra access tokens expire ~1h; avoids hourly re-login. |
| D5 | **In-memory** stores (DCR clients, flow state, token map); **tenant-specific** Entra endpoints | Pass-through-free design needs only short/medium-lived state. Tenant-specific authority proven to mint a valid token for the personal MSA this session. |
| D6 | Our AS **ignores** claude's `resource` and `scope`; always sends Entra `scope=api://{ClientId}/access_as_user offline_access openid`, **no `resource`** | The crux — sidesteps `AADSTS500011`. |
| D7 | **Adapt the MIT Profility reference** (attribution in source headers); **skip** its login-page/`/continue`/branding layer — redirect `/oauth/authorize` straight to Entra | Reuse proven code; cut the branded-consent screen we don't need (minimalism). |

**Rejected:** APIM (cost); OpenIddict/Duende (heavyweight for a user-less proxy); pass-through tokens (small `aud` compatibility risk vs claude — see D3); manual client credentials (token exchange fails regardless).

---

## Architecture

```
                       menunest.azurewebsites.net  (existing App Service — unchanged deploy)
claude.ai ─(1) GET /.well-known/oauth-protected-resource[/mcp]   → authorization_servers=[this server], resource=server URL
          ─(2) GET /.well-known/oauth-authorization-server       → our /oauth/* endpoints, issuer=this server
          ─(3) POST /oauth/register  (DCR)                        → { client_id }  (public client, PKCE, no secret)
          ─(4) GET  /oauth/authorize?client_id&redirect_uri&code_challenge(claude)&state&scope?&resource?(ignored)
  our AS        │ store flow state (claude redirect/state/challenge + OUR pkce); ClientStore validates client_id
                └─→ 302 Entra /{tenant}/oauth2/v2.0/authorize  (OUR Entra client, redirect=/oauth/callback,
                                                                 scope=api://{cid}/access_as_user offline_access openid,
                                                                 OUR code_challenge, state=flowRef, NO resource)
Entra ───────────→ GET /oauth/callback?code&state(flowRef)
  our AS        │ POST Entra /token (client_id+secret, code, redirect=/oauth/callback, OUR verifier) → {access,refresh,id}
                │ extract user claims from Entra token; mint proxyAuthCode; TokenStore[proxyAuthCode]={entra tokens, claims, claudeChallenge}
                └─→ 302 claudeRedirect?code=proxyAuthCode&state=claudeState
claude.ai ─(5) POST /oauth/token (grant=authorization_code, code=proxyAuthCode, code_verifier(claude), redirect_uri)
  our AS        │ verify claude PKCE vs stored challenge; single-use code
                │ mint OUR JWT (HMAC; aud=iss=ServerUrl; sub+claims from Entra token; scope)
                │ mint opaque refreshCode; TokenStore[refreshCode]={entra refresh token}
                └─→ 200 { access_token=<our JWT>, refresh_token=<opaque>, expires_in, token_type:"Bearer", scope }
          ─(6) GET/POST /mcp + Authorization: Bearer <our JWT>  → NEW "McpProxy" JwtBearer scheme validates → tools load
          ─(R) POST /oauth/token (grant=refresh_token, refresh_token=<opaque>)
  our AS        └─→ Entra /token (refresh) → re-mint our JWT + new opaque refreshCode
```

**Why this validates:** claude sees a standard OAuth2 AS (DCR + PKCE + authorize/token) whose `issuer` = our server — no Entra templated-issuer or `resource` problem. Our JWT's `aud = server URL` satisfies RFC 8707 audience binding. The Entra leg is internal and `resource`-free.

---

## Endpoints (in `MenuNest.WebApi`)

### Modified discovery docs (anonymous)

- **`/.well-known/oauth-protected-resource[/mcp]`** — `resource` = server URL (revert the experimental `api://…`); `authorization_servers` = `["{base}"]` (**our server**); `scopes_supported` = `["api://{ClientId}/access_as_user","openid","profile","email"]`; `bearer_methods_supported` = `["header"]`.
- **`/.well-known/oauth-authorization-server`** — `issuer` = `{base}`; `authorization_endpoint`=`{base}/oauth/authorize`; `token_endpoint`=`{base}/oauth/token`; `registration_endpoint`=`{base}/oauth/register`; `response_types_supported=["code"]`; `grant_types_supported=["authorization_code","refresh_token"]`; `code_challenge_methods_supported=["S256"]`; `token_endpoint_auth_methods_supported=["none"]`.

### New `/oauth/*` (anonymous)

- **`POST /oauth/register`** (DCR / RFC 7591) — validate each `redirect_uri` (HTTPS + allowlist `claude.ai`/`claude.com` callbacks); `ClientStore.Register(...)` → return `{ client_id, client_id_issued_at, token_endpoint_auth_method:"none", redirect_uris, grant_types, response_types }`. No secret.
- **`GET /oauth/authorize`** — validate `client_id` (ClientStore) + `redirect_uri` (allowlist) + `code_challenge_method=S256`; build OUR PKCE; persist flow state; 302 → Entra authorize (params above, **no `resource`**).
- **`GET /oauth/callback`** — look up flow by `state`; POST Entra `/token` (authorization_code, OUR verifier, client_id+secret); require a refresh token; extract user claims; store `TokenData` under a new `proxyAuthCode`; 302 → claude redirect with `code=proxyAuthCode&state=claudeState`.
- **`POST /oauth/token`** (`application/x-www-form-urlencoded`):
  - `authorization_code`: verify `BASE64URL(SHA256(code_verifier)) == stored claudeChallenge`; consume code (single-use); mint our JWT; mint opaque `refresh_token`; return token response.
  - `refresh_token`: look up Entra refresh token by the opaque code; refresh at Entra; re-mint JWT + rotate opaque refresh code.

### `/mcp` — **changed auth scheme**

- Today `/mcp` uses the default `MultiAuth` policy (validates Entra tokens). With D3 it must validate **our** JWT. Add a JwtBearer scheme **`"McpProxy"`** (`ValidIssuer=ValidAudience={MCP:ServerUrl}`, `IssuerSigningKey`=HMAC key from `Jwt:SigningKey`, `ValidateLifetime=true`). Change `app.MapMcp("/mcp").RequireAuthorization(<policy bound to "McpProxy">)`. The existing `"Microsoft"`/`"Google"` schemes and controller auth are **unchanged**.
- **Critical integration:** our minted JWT MUST carry the claims `ICurrentUserService` reads (so family-scoping/ownership still work). The plan must inspect `ICurrentUserService` + the `"Microsoft"` handler (`MapInboundClaims=false`) to pin the exact claim(s) (likely `sub`/`oid`, `name`, `email`) and copy them from the Entra token into our JWT at `/oauth/callback`.

---

## State & stores (`IMemoryCache`-backed, single instance)

| Store | Holds | Lifetime |
|---|---|---|
| `IClientStore` (InMemory) | DCR client registrations (id → redirect_uris) | 30 d sliding |
| `IPkceStateManager` (InMemory) | flow state: claude redirect/state/challenge, OUR verifier, scope | 10 min |
| `ITokenStore` (InMemory) | proxyAuthCode → {Entra access+refresh, user claims, claudeChallenge}; refreshCode → Entra refresh token | code 60 s; refresh-map = refresh token lifetime |

> **As-built:** the `IClientStore`/`IPkceStateManager`/`ITokenStore` interfaces were omitted — the code uses concrete singletons `ClientStore`, `PkceStateStore`, `TokenStore` (backed by `IMemoryCache`). Add interfaces only if AzureTable variants are ever needed.

**Constraint:** correctness assumes a **single App Service instance** (in-memory state isn't shared). MenuNest runs one instance — acceptable. Scale-out would require the reference's `AzureTable*` store variants (documented in ADR-003).

---

## Config / infra (one-time)

1. **Entra app `e65fd81b…`** — add Web redirect URI `https://menunest.azurewebsites.net/oauth/callback`. Remove the now-unused `claude.ai`/`claude.com` callbacks (claude redirects to *our* AS, not Entra).
2. **App Service `menunest` settings** — add:
   - `AzureAd__ClientSecret` = the `claude-mcp-connector` value (server-side Entra exchange).
   - `Jwt__SigningKey` = a strong random secret (HMAC). *Production note: move to Key Vault later.*
   - `MCP__ServerUrl` = `https://menunest.azurewebsites.net/mcp` (stable `aud`/`iss` for our JWT).
3. Mirror these as empty keys in `appsettings.json` for discoverability. No new Azure resources.

---

## Security

- `redirect_uri` allowlist (claude.ai/claude.com) enforced at `/oauth/register` + `/oauth/authorize` — blocks open-redirect/token theft.
- PKCE S256 required end-to-end (claude↔our AS; our AS↔Entra with a separate pair).
- Proxy auth codes: single-use, 60 s, cryptographically random; opaque refresh codes rotated on use.
- Entra `client_secret` and JWT `SigningKey` stay server-side (App Service config; Key Vault later). Entra access/refresh tokens never sent to claude.
- All `/oauth/*` + `.well-known` endpoints `AllowAnonymous`.

---

## Files

| File | Action |
|---|---|
| `backend/src/MenuNest.WebApi/Oauth/OAuthEndpoints.cs` (or `Controllers/OAuthController.cs`) | **Create** — `/oauth/authorize|callback|token|register` (adapted from Profility `OAuthController`, minus login-page/`/continue`) |
| `backend/src/MenuNest.WebApi/Oauth/WellKnown*.cs` / `Program.cs` | **Modify** — PRM + AS metadata point at our `/oauth/*` |
| `backend/src/MenuNest.WebApi/Oauth/PkceStateManager.cs` + `Models/PkceStateData.cs` | **Create** (adapt) |
| `backend/src/MenuNest.WebApi/Oauth/ClientStore/{IClientStore,InMemoryClientStore}.cs` + `Models/ClientMapping.cs` | **Create** (adapt) |
| `backend/src/MenuNest.WebApi/Oauth/TokenStore/{ITokenStore,InMemoryTokenStore}.cs` + `Models/TokenData.cs` | **Create** (adapt) |
| `backend/src/MenuNest.WebApi/Oauth/Jwt/JwtBuilder.cs` (mint JWT, opaque-token gen, PKCE verify) | **Create** (adapt) |
| `backend/src/MenuNest.WebApi/Program.cs` | **Modify** — DI for the stores/JwtBuilder/HttpClient; add `"McpProxy"` JwtBearer scheme; `/mcp` uses it; map `/oauth/*` |
| `backend/src/MenuNest.WebApi/appsettings.json` | **Modify** — add empty `AzureAd:ClientSecret`, `Jwt:SigningKey`, `MCP:ServerUrl` |
| `backend/tests/MenuNest.McpServer.UnitTests/…` (or a new WebApi test project) | **Create** — unit tests for pure pieces: PKCE verify, JWT build/validate round-trip, redirect allowlist, metadata builders |
| `docs/adr/003-mcp-oauth-proxy.md` | **Create** — records D1–D7; supersedes ADR-002 |

Each adapted file carries a header crediting the MIT-licensed Profility reference. Adapt + **review** (don't blind-copy) — verify PKCE, allowlist, single-use codes against this spec.

---

## Key risks & verification

- **Risk A — claude.ai compatibility with our AS.** Mitigated by adopting the Claude-tested Profility pattern; residual integration risk remains, resolved only against the live client.
- **Risk B — claim mapping.** If our JWT omits a claim `ICurrentUserService` needs, tools authenticate but user resolution breaks. The plan pins the exact claims first.
- **Risk C — single-instance assumption** for in-memory stores (documented).

**Verification (probe-then-reconnect):**
1. After deploy, curl the chain: PRM → our AS metadata → `/oauth/register` returns a client_id → `/oauth/authorize` 302s to Entra **without** `resource`.
2. Controlled end-to-end repro (localhost client through *our* `/oauth/*`) → confirm our JWT comes out and validates at `/mcp` — **before** involving claude.ai.
3. claude.ai reconnect → **Connected**, tools list, one read-only tool call succeeds, and it resolves the correct user. Not done until this passes.

---

## Out of scope

- Branded login/consent page, account-picker UI (Profility's `/continue` layer).
- Multi-instance/distributed state (single-instance assumption documented; AzureTable variants noted).
- Any change to MCP tools or the meal-planning domain.
- Google-auth users via MCP (Entra-only, per ADR-001).
