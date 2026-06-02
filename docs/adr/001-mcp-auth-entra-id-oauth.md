# ADR-001: MCP Server authenticates via Entra ID OAuth, not a static API key

**Date:** 2026-06-02
**Status:** Accepted

---

## Context

The MenuNest MCP server must be reachable by Claude on mobile and Claude.ai web.
Both clients connect to remote MCP servers over HTTPS and require the server to
support OAuth 2.0 for authentication — they cannot pass custom HTTP headers or use
a static API key that a developer configures in a local config file.

A static API key approach was considered first because it is simpler to implement:
set `MENUNEST_MCP_API_KEY` as an App Service environment variable and validate it
in a middleware. This works for Claude Desktop (local stdio or HTTP where you control
the client config), but Claude.ai and Claude mobile do not provide a mechanism to
inject arbitrary bearer tokens — they initiate an OAuth 2.0 authorization code flow
against the server's declared authorization server.

## Decision

The MCP server exposes `/.well-known/oauth-authorization-server` pointing entirely
to the existing Entra ID tenant. It does not implement any OAuth server logic itself.

Claude discovers the endpoint, redirects the user through Microsoft login (the same
identity provider already used by the app), and receives an Entra ID JWT. That token
is presented as `Authorization: Bearer` on every MCP request and validated by the
existing `"Microsoft"` JwtBearer handler already configured in `Program.cs`.

No new auth infrastructure is added. No second identity provider is introduced.

## Consequences

**Positive**
- Works on Claude mobile and Claude.ai web without any workaround.
- User identity flows naturally into `ICurrentUserService` — the same service
  controllers use — so all family-scoping and ownership checks work unchanged.
- Zero new auth infrastructure: Entra ID tenant, app registration, and JWT
  validation are already in place.

**Negative**
- The user must complete a Microsoft OAuth flow the first time they connect Claude
  to the MCP server (one-time, then Claude refreshes the token automatically).
- Google-authenticated users (who signed up via Google OAuth) cannot use the MCP
  server unless they also have a Microsoft account linked to the same profile —
  Entra ID is the only supported identity provider for MCP auth.
- If the Entra ID app registration is ever deleted or the tenant changes, the MCP
  server auth breaks along with the rest of the app.
