# ADR-004: The OAuth proxy brokers sign-in via `/common`, not the organization tenant

**Date:** 2026-06-03
**Status:** Accepted
**Amends:** ADR-003 (the OAuth proxy that brokers MCP auth to Entra)

## Context

The MCP OAuth proxy (ADR-003) builds its Entra authorize/token URLs from
`AzureAd:TenantId`. In production, IaC sets `AzureAd__TenantId` to
`subscription().tenantId` — the organization tenant — so the proxy authenticates
users against `/{orgTenantId}`. The web SPA and the API's `Microsoft` JWT scheme,
by contrast, use `/common` (the SPA via `VITE_MSAL_AUTHORITY`, the API via a
hardcoded `…/common/v2.0` authority with `ValidateIssuer = false`). All three
share one Entra app registration (`e65fd81b-…`), whose `signInAudience` already
permits personal Microsoft accounts (proven: the SPA signs personal accounts in
via `/common`).

A **personal** Microsoft account (e.g. `…@outlook.com`) that is a guest in the
org tenant therefore receives **two different `oid` values**: its **home oid**
via the SPA's `/common`, and a **guest oid** via the proxy's `/{orgTenantId}`.
Because `UserProvisioner` keys `User` on `ExternalId == oid`, the same human
became two Users — the web one with a Family, the MCP one family-less — and
every family-gated MCP tool call threw "You must join or create a family."
(Diagnosed 2026-06-03 from App Insights exception + SQL-dependency timing + IaC.)
Org-member accounts were unaffected because their oid is identical in the org
tenant regardless of path.

## Decision

The proxy brokers sign-in via **`/common`**, matching the SPA, so any given human
always yields their home oid as ExternalId. The sign-in authority is decoupled
from `AzureAd:TenantId` via a dedicated config key (default `common`) read only
by the proxy's Entra client. `AzureAd:TenantId` is no longer used to build the
proxy's authority.

## Consequences

**Positive:** One human → one `oid` → one User across web and MCP. Personal and
org accounts both work on MCP. Minimal change; no app-registration change (the
shared app reg already allows personal accounts).

**Negative:** "Sign-in authority" is now a distinct configuration concept teams
must keep consistent across entry paths — a future path that forces an org
tenant would reintroduce the duplicate-identity bug. Pre-fix guest-oid User rows
are orphaned and require a one-off cleanup. Keying identity on `oid` still
assumes a consistent authority; it is not self-healing if that invariant breaks.
