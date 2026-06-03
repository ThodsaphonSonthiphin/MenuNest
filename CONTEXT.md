# MenuNest — Glossary (CONTEXT)

Canonical terms for the MenuNest domain. Glossary only — no implementation
detail. When a term here conflicts with how code or conversation uses a word,
the glossary wins until the glossary is deliberately changed.

## Identity & auth

- **User** — a person's account row, keyed by **ExternalId**. Distinct from
  **Family**; a User may have no Family yet.
- **ExternalId** — the stable identity key for a User. It is the identity
  provider's subject/object identifier: for Microsoft sign-ins, the Entra
  **`oid`** claim; for Google, the `sub`. One human must map to exactly one
  ExternalId across **every** entry path (web SPA and MCP), or they become two
  Users.
- **Home oid** — the `oid` a Microsoft account receives when it signs in
  through its **own** home tenant (for a personal/outlook.com account, the
  Microsoft "consumers" tenant). Reached via the `/common` authority. This is
  the canonical ExternalId for a Microsoft user.
- **Guest oid** — the *different* `oid` the same Microsoft account receives when
  it signs in as a **guest of a specific organization tenant**. Using a guest
  oid as ExternalId creates a duplicate, family-less User. Avoid: brokers must
  sign users in via a home-consistent authority, never a forced org tenant.
- **Sign-in authority** — the Entra authority a broker uses to authenticate a
  user (`/common`, `/organizations`, `/consumers`, or `/{tenantId}`). Must be
  consistent across all entry paths so a given human always yields the same
  home oid. Separate concept from the organization's own tenant id.
- **Family** — the sharing boundary that owns recipes, meal plans, budget, etc.
  Most features are **family-gated**: a User with no Family is rejected with a
  DomainException until they create or join one.
- **OAuth proxy** — MenuNest's in-app OAuth 2.1 Authorization-Server facade
  (`/oauth/*`) that brokers MCP authentication to Entra and mints the app's own
  JWT for `/mcp`. See ADR-003 / ADR-004.
