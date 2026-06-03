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
