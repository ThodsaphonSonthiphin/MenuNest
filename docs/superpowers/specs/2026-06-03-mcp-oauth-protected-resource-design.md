# MCP OAuth — Protected-Resource Discovery for claude.ai — Design Spec

> **⚠️ SUPERSEDED — DO NOT IMPLEMENT.** This describes an abandoned approach (serve protected-resource metadata pointing `authorization_servers` at the Entra tenant issuer; no proxy). It was abandoned once confirmed that claude.ai sends RFC 8707 `resource=<server URL>` which Entra cannot resolve. The shipped solution is the OAuth proxy — see [ADR-003](../../adr/003-mcp-oauth-proxy.md) and `docs/superpowers/specs/2026-06-03-mcp-oauth-proxy-design.md`.

**Date:** 2026-06-03
**Status:** Superseded
**Scope:** OAuth discovery only — make the already-deployed MenuNest MCP server connectable from **claude.ai web** (and, by extension, Claude mobile / Claude Code). No change to MCP tools, business logic, or the meal-planning domain.
**Supersedes (partially):** the "OAuth Discovery Endpoint" section of `docs/superpowers/specs/2026-06-02-menunest-mcp-server-design.md`, which assumed `/.well-known/oauth-authorization-server` alone was sufficient.

---

## Problem

The MCP server is live at `https://menunest.azurewebsites.net/mcp` (Streamable HTTP, `app.MapMcp("/mcp").RequireAuthorization()`), protected by the existing Entra ID `"Microsoft"` JwtBearer handler. It serves `/.well-known/oauth-authorization-server` (200).

But **claude.ai follows RFC 9728**: on connect it first GETs `/.well-known/oauth-protected-resource` to learn (a) which authorization server to use and (b) which **scope** to request. On this server that path is **not implemented**, so the global `FallbackPolicy` (auth required on everything) returns **401**. With no protected-resource metadata, Claude cannot discover the required scope, falls back to **Dynamic Client Registration (RFC 7591)**, and Entra rejects it:

> "Couldn't register with MenuNest's sign-in service… add an OAuth Client ID in the connector settings." (ref `ofid_bec06ce9e1317756`)

**Observed:** after manually adding a Client ID, claude.ai's next request is to `/.well-known/oauth-protected-resource` — confirming this endpoint is the blocker.

---

## Already done this session (Azure / Entra — app reg `e65fd81b-7a28-439b-a2ea-98734b5b5a36`, personal tenant `d500e2f4-…`)

| Change | State |
|---|---|
| **Web** redirect URIs `https://claude.ai/api/mcp/auth_callback`, `https://claude.com/api/mcp/auth_callback` | ✅ added |
| Client secret `claude-mcp-connector` (2-yr) | ✅ created (value handed to user; rotate if transcript shared) |
| API scope `access_as_user` (`api://e65fd81b…/access_as_user`) | ✅ already exposed |
| Confirmed prod App Service `menunest` uses the **same** Client ID + tenant | ✅ |

These are infrastructure facts the code change depends on — **not** repeated work for the implementation.

---

## Empirical findings (probed live, 2026-06-03)

| Probe | Result |
|---|---|
| `GET menunest.azurewebsites.net/mcp` | **401** (live, auth-protected) ✅ |
| `GET .../.well-known/oauth-authorization-server` | **200**, `issuer = …/common/v2.0` |
| `GET .../.well-known/oauth-protected-resource` | **401** (not implemented — the bug) ❌ |
| `GET login.microsoftonline.com/common/v2.0/.well-known/oauth-authorization-server` | **404** |
| `GET login.microsoftonline.com/common/v2.0/.well-known/openid-configuration` | **200**, but **templated** issuer `…/{tenantid}/v2.0` |
| `GET login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration` | **200**, **concrete matching** issuer ✅ |
| RFC 8414 *prefix* forms (`/.well-known/…/{tenant}/v2.0`) | **404** — Entra serves only the *suffix* form, and only `openid-configuration` |
| Real host | `menunest.azurewebsites.net` (docs say `menunest-api…` — **wrong**) |

---

## Decisions (this session)

| # | Decision | Rationale |
|---|---|---|
| D1 | **No DCR shim.** Connect by pasting Entra **Client ID + Secret** into the connector's Advanced settings (one-time). | Personal/family app, ~1–2 connectors. DCR shim = extra anonymous endpoint + secret distribution for no real benefit. Matches Damien Bod's Entra MCP guidance (DCR deliberately omitted). |
| D2 | **Hand-rolled anonymous minimal-API endpoint**, mirroring the existing `/.well-known/oauth-authorization-server`. | Zero changes to the working Microsoft+Google `MultiAuth` policy scheme. Lowest blast radius; consistent with current style. Avoids wiring the SDK's `McpAuth` challenge scheme into the custom dual-provider pipeline. |
| D3 | **`authorization_servers` → tenant-specific Entra issuer** `https://login.microsoftonline.com/{tenant}/v2.0`. | Only fully **issuer-consistent** path: Entra's tenant OIDC doc returns a concrete matching `issuer`, and the RFC 9207 `iss` redirect param will equal it. The self-hosted `common` doc cannot satisfy both AS-metadata issuer-match *and* the `iss` param simultaneously. |
| D4 | **Verify with a discovery-probe before reconnecting.** | claude.ai OAuth can't run against localhost; prod is the only real test. Replaying Claude's discovery chain with curl isolates issuer/scope failures from opaque UI errors. |
| D5 | **New ADR-002**, reference ADR-001; **fix the `menunest-api` URL typo** in the 2026-06-02 spec. | Records the no-DCR + RFC 9728 decisions without mutating the accepted ADR-001. |

---

## Design

### Core change — `backend/src/MenuNest.WebApi/Program.cs`

Add an **anonymous** handler serving RFC 9728 metadata, mounted at both the bare well-known path and the resource-suffixed path (Claude was observed hitting the bare path; the suffixed form is what RFC 9728 §3.1 specifies for a resource with a path component — serve both, one handler):

```
GET /.well-known/oauth-protected-resource
GET /.well-known/oauth-protected-resource/mcp        → .AllowAnonymous()
```

Response body (values composed from `AzureAd` config + the request, **not** hardcoded to prod):

```jsonc
{
  "resource": "{scheme}://{host}/mcp",                                  // from request → env-agnostic
  "authorization_servers": [ "{Instance}{TenantId}/v2.0" ],             // prod: …/d500e2f4-…/v2.0
  "scopes_supported": [ "api://{ClientId}/access_as_user" ],            // api://e65fd81b…/access_as_user
  "bearer_methods_supported": [ "header" ]
}
```

- `Instance` = `https://login.microsoftonline.com/`, `TenantId`, `ClientId` ← `AzureAd:*` (prod App Service has the **concrete** tenant GUID + client id).
- `resource` derived from the incoming request so dev / future hosts work without edits.
- The existing `/.well-known/oauth-authorization-server` endpoint is **left unchanged** — Claude's RFC 9728 path resolves the AS via `authorization_servers` (D3) and never reads it. It remains as a harmless fallback for non-9728 clients.

### Discovery chain this produces (claude.ai)

1. `GET /.well-known/oauth-protected-resource` → 200; reads `authorization_servers[0]` = `…/{tenant}/v2.0`, `scopes_supported`.
2. AS metadata discovery: `…/{tenant}/v2.0/.well-known/oauth-authorization-server` → 404 → **OIDC fallback** `…/{tenant}/v2.0/.well-known/openid-configuration` → 200, **issuer matches**.
3. Auth-code + PKCE against Entra tenant `authorize`/`token`; user signs in with their Microsoft account; Claude exchanges the code using the configured Client ID + Secret.
4. Entra issues an access token with `aud = {ClientId}` (GUID; the app uses v2 tokens) and scope `access_as_user`.
5. Claude calls `/mcp` with `Authorization: Bearer …`; the existing `"Microsoft"` JwtBearer handler validates `aud == ClientId` (`ValidateIssuer = false`, so the tenant issuer is accepted). Tools load.

### Token / audience correctness

The API validates `ValidAudience = AzureAd:ClientId` with `ValidateIssuer = false` ([Program.cs](../../backend/src/MenuNest.WebApi/Program.cs)). Requesting `api://{ClientId}/access_as_user` yields `aud = {ClientId}` (bare GUID) — matches. The tenant-specific `iss` is not validated by the API, so D3 does not require any API auth change.

---

## Key risk & fallbacks

**Risk:** Claude's discovery must fall through from the (404) `oauth-authorization-server` probe to the (200) `openid-configuration` probe. The current MCP TypeScript SDK does implement this OIDC fallback, and the tenant OIDC doc is confirmed 200 with a matching issuer — but this is the one runtime behavior we cannot prove without the live test (hence D4).

| If the probe / reconnect fails at… | Fallback |
|---|---|
| AS metadata not discovered (no OIDC fallback) | Point `authorization_servers` at the server origin and make the self-hosted `/.well-known/oauth-authorization-server` a faithful tenant-specific doc (`issuer = …/{tenant}/v2.0`, tenant `authorize`/`token`) so the origin-based lookup returns a self-consistent doc. |
| Login OK but `/mcp` returns 401 | Inspect the presented token's `aud`/`scp`; confirm Claude requested `api://{ClientId}/access_as_user` (scope advertised correctly) vs a generic scope. |
| Claude never probes the well-known path | **Phase 2:** inject `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource"` on `/mcp` 401s (via a JwtBearer event or tiny middleware scoped to `/mcp`). Not in core scope — claude.ai was observed probing the path directly. |

---

## Verification (D4 — before claiming done)

After CI deploys (push to `main` → `main_menunest.yml`):

1. **Probe** (curl/PowerShell, anonymous):
   - `GET /.well-known/oauth-protected-resource` → 200, body shape correct, `authorization_servers[0]` = concrete tenant issuer, scope = `api://e65fd81b…/access_as_user`.
   - `GET {authorization_servers[0]}/.well-known/openid-configuration` → 200, `issuer` **equals** `authorization_servers[0]`, has `authorization_endpoint` + `token_endpoint`.
2. **Reconnect** on claude.ai with URL `https://menunest.azurewebsites.net/mcp` + Client ID + Secret → connector reaches **Connected**, tool list (Recipe/Ingredient/MealPlan/Stock/ShoppingList/Budget) appears, one read-only tool call succeeds.

Only after both pass is the work complete.

---

## Files created / modified

| File | Action |
|---|---|
| `backend/src/MenuNest.WebApi/Program.cs` | Add anonymous `/.well-known/oauth-protected-resource[/mcp]` endpoint |
| `docs/adr/002-mcp-oauth-protected-resource-discovery.md` | Create (D1, D3, references ADR-001) |
| `docs/superpowers/specs/2026-06-02-menunest-mcp-server-design.md` | Fix `menunest-api.azurewebsites.net` → `menunest.azurewebsites.net` (URL + Claude config block) |

**Testability:** the endpoint is anonymous, so a `WebApplicationFactory` integration test can assert `200 + anonymous + JSON shape` without a real Entra token. The issuer-resolution behavior itself can only be confirmed by the live probe (D4). Final task/test breakdown is delegated to `superpowers:writing-plans`.

## Out of scope

- DCR shim (D1), `WWW-Authenticate` header injection (Phase 2 fallback), SDK `McpAuth` scheme (D2 rejected alternative).
- Any change to MCP tools, handlers, the SPA, or the meal-planning domain.
- Google-authenticated users connecting via MCP (Entra-only, per ADR-001).
