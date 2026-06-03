# Design — Fix MCP duplicate-identity for personal Microsoft accounts

**Date:** 2026-06-03
**Status:** Draft for approval
**Related:** ADR-003 (OAuth proxy), ADR-004 (sign-in authority = `/common`), CONTEXT.md (Identity & auth)

## 1. Problem

A personal Microsoft account (`thodsaphonsp@outlook.com`) that works normally on
the web fails on every MCP tool call with `DomainException: "You must join or
create a family before using this feature."`

**Root cause (confirmed, high confidence):** the same human gets two different
Entra `oid` values across entry paths, so `UserProvisioner` (which keys `User`
on `ExternalId == oid`) creates two Users — the web one has a Family, the MCP one
does not.

Evidence chain:
- App Insights exception: `DomainException` from `UserProvisioner.RequireFamilyAsync`, operation `POST /mcp`, tool `list_recipes`.
- User confirmed: the account has a Family and works on the web.
- SQL-dependency timing on the failing request: the first MCP tool call ran `SELECT`+`INSERT` (a *new* User was created); a later call ran a single `SELECT` then threw (found that family-less row). So MCP provisioned its **own** User.
- IaC: `infra/modules/app-service.bicep` sets `AzureAd__TenantId = subscription().tenantId` (the org tenant). The proxy's `EntraClient` builds its authority from `AzureAd:TenantId` → authenticates against `/{orgTenantId}`.
- The SPA (`frontend/.env` `VITE_MSAL_AUTHORITY`) and the API `Microsoft` scheme ([Program.cs](../../../backend/src/MenuNest.WebApi/Program.cs#L79)) use `/common`.
- Mechanism: via `/common` the personal account signs into its home (consumers) tenant → **home oid**; via the org tenant it is a guest → **guest oid**. Two oids → two Users. Org-member accounts are unaffected (same oid either way), which is why only the personal account exposed it.

**Secondary issue (real, not cosmetic):** the MCP path has no equivalent of the
web's [`ExceptionHandlingMiddleware`](../../../backend/src/MenuNest.WebApi/Middleware/ExceptionHandlingMiddleware.cs#L60-L80). The MCP SDK catches a thrown `DomainException` inside its own tool-invocation pipeline (before it reaches the middleware), logs it as a `ToolCallError` at **Error** severity, and returns a generic "unhandled exception" to the client. So even after the identity fix, a *genuinely* family-less user (someone who connects Claude before creating a family) gets a server error instead of actionable guidance — and benign domain rejections pollute the failure metrics.

## 2. Goals / Non-goals

**Goals**
- One human → one `oid` → one `User` across web and MCP.
- Personal *and* org Microsoft accounts work on MCP.
- Expected domain/validation rejections on MCP return a clean, actionable tool
  error logged at Warning (parity with the web path).
- Remove the already-created orphan User row(s).

**Non-goals (Phase 2 / out of scope)**
- Automated duplicate-User detection/merge migration.
- Hardening `UserProvisioner` to match on email or otherwise self-heal identity
  drift.
- Exposing family create/join tools over MCP.
- Moving secrets to Key Vault, scale-out durable stores (tracked by ADR-003).

## 3. Decisions

### D1 — Proxy brokers sign-in via `/common`, decoupled from `AzureAd:TenantId`
Introduce a dedicated config key for the proxy's Entra sign-in authority,
defaulting to `common`. The proxy's `EntraClient` uses this key instead of
`AzureAd:TenantId` when building authorize/token URLs.

- Proposed key: **`AzureAd:SignInTenant`** (value: `common`).
- `backend/src/MenuNest.WebApi/Oauth/EntraClient.cs`: `Tenant` now reads
  `AzureAd:SignInTenant` (fallback `"common"` if unset) instead of
  `AzureAd:TenantId`. No other behavior changes; URLs become
  `…/common/oauth2/v2.0/{authorize,token}`.
- `backend/src/MenuNest.WebApi/appsettings.json`: add `AzureAd:SignInTenant = "common"` (documents the default; dev already uses `/common`).
- `infra/modules/app-service.bicep`: add app setting `AzureAd__SignInTenant = 'common'`. Leave `AzureAd__TenantId = subscription().tenantId` untouched (no longer used by the proxy, but harmless and may serve future code).
- No change to `OAuthJwt`, `ClaimExtractor`, `CurrentUserService`, the `Microsoft`/`McpProxy` JWT schemes, or the Entra app registration. After the change, the personal account's id_token `oid` (via `/common`) equals the home oid the SPA already uses, so MCP resolves to the existing family-bearing `User`.

### D2 — One-off manual cleanup of orphan User row(s)
The pre-fix guest-oid User row(s) are dead data (family-less ⇒ no dependent rows,
since every write is family-gated). Because "FamilyId is null" alone cannot
distinguish an orphan from a brand-new legitimate user, identify orphans as
**a User whose Email matches another User that *has* a Family, and whose own
FamilyId is null**.

- Verify the count first (expected: 1 — the test account, since the proxy
  launched today). If the count is more than a small handful, **stop and
  re-evaluate** rather than bulk-deleting.
- Delete the identified orphan row(s) manually as a rollout step. No migration
  code.

### D3 — MCP boundary translates expected exceptions to clean tool errors
Add a single call-tool filter in
[`McpServerRegistration`](../../../backend/src/MenuNest.McpServer/McpServerRegistration.cs)
that wraps tool invocation and, on `DomainException` / FluentValidation
`ValidationException`, returns a tool result marked as an error containing the
exception's user-facing message, and logs at **Warning** (not Error). Genuinely
unexpected exceptions are left to propagate / log as Error as today. This mirrors
the web `ExceptionHandlingMiddleware` classification at the MCP boundary.

- **Planning task:** confirm the exact filter hook for `ModelContextProtocol.AspNetCore` 0.3.x (the SDK exposes a call-tool filter pipeline — the same mechanism as its `AuthorizationFilterSetup`). The design intent (catch → classify → clean error result at Warning) holds regardless of the precise API shape; the plan pins the signature before coding.

## 4. Verification

1. **Identity fix (the bug):** re-connect Claude with `thodsaphonsp@outlook.com`
   via the OAuth flow, call `list_recipes` → returns the family's recipes (no
   DomainException). Confirm via App Insights that the `POST /mcp` request issues
   only the lookup (no new `INSERT` into Users) and that no new family-less row
   was created.
2. **Web unaffected:** the SPA still logs in and lists recipes (regression check).
3. **Error mapping (D3):** force a family-gated tool as a family-less identity →
   the MCP client receives a clean, actionable error (not "unhandled
   exception"), and App Insights logs it at Warning, not Error.
4. **Cleanup (D2):** after deletion, the orphan-detection query returns zero rows.

## 5. Risks & mitigations

- **Assumption — prod `VITE_MSAL_AUTHORITY` is `/common`.** It is a deploy
  secret, not readable here; the dev default is `/common` and the bug's
  existence implies it. *Mitigation:* verification step 1 fails loudly if the SPA
  uses a different authority; confirm the secret during rollout.
- **Other readers of `AzureAd:TenantId`.** Grep confirms `EntraClient` is the
  only consumer; the API `Microsoft` scheme hardcodes `/common` separately. Low
  risk.
- **MCP SDK filter API drift (D3).** Mitigated by the explicit planning task to
  pin the 0.3.x signature before implementation.
- **Bulk-delete blast radius (D2).** Mitigated by verify-count-first and the
  email-match + null-Family detection rather than deleting all family-less users.

## 6. Affected files (summary)

| File | Change |
|---|---|
| `backend/src/MenuNest.WebApi/Oauth/EntraClient.cs` | Read `AzureAd:SignInTenant` (default `common`) for authority |
| `backend/src/MenuNest.WebApi/appsettings.json` | Add `AzureAd:SignInTenant = "common"` |
| `infra/modules/app-service.bicep` | Add `AzureAd__SignInTenant = 'common'` app setting |
| `backend/src/MenuNest.McpServer/McpServerRegistration.cs` | Add call-tool filter for DomainException/ValidationException → clean tool error at Warning |
| (ops, no code) | One-off delete of orphan User row(s) per D2 |

Existing artifacts already written this session: `CONTEXT.md` (glossary),
`docs/adr/004-oauth-proxy-signin-authority-common.md`.
