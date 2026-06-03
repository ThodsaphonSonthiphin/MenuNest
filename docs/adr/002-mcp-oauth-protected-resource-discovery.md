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
