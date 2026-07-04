# ADR-018: A Leg carries an explicit `RouteSource {Routed, Estimated}` so the fallback is never silent

**Date:** 2026-07-03
**Status:** Accepted
**Relates to:** ADR-016 (computeRoutes), ADR-017 (per-leg resolution), ADR-007 (Google Maps Platform)
**Amends:** CONTEXT.md — the **Leg** glossary definition

```mermaid
flowchart TD
    Q{How does a Leg signal<br/>"real route" vs "straight-line estimate"?} -->|chosen| A["Explicit enum RouteSource<br/>{ Routed, Estimated } on the Leg"]
    Q -->|rejected| B["Infer from EncodedPolyline == null<br/>(single field, no explicit flag)"]
    Q -->|rejected| C["bool Estimated / enum by vendor<br/>{ Google, Haversine }"]
    A --> N{Naming} --> N1["by quality (Routed/Estimated),<br/>not vendor — UI/telemetry key on meaning,<br/>provider swaps don't churn the contract"]
```

## Context

`GoogleRouteService` falls back to `HaversineRouteService` on **any** Routes API
failure (currently: 403 `BILLING_DISABLED` on the whole project — see the
debug-mantra finding). The fallback is **silent**: the UI shows the straight-line
×1.3 estimate as if it were a routed distance, and the `LogWarning` never reaches
App Insights. CONTEXT.md even asserts a Leg's time is "from the Routes API, not an
estimate" — which today's behaviour contradicts.

Making the fallback honest requires the **Leg** to carry whether its numbers are
real. Options considered: infer it from `EncodedPolyline == null` (Google always
returns geometry, Haversine never does, so null is a faithful proxy); a `bool
Estimated`; or an explicit enum. The user chose the **explicit** signal for clarity
and telemetry, named by **quality** rather than vendor.

## Decision

Add `enum RouteSource { Routed, Estimated }` (in `Domain.Enums`, alongside
`TravelMode`) and carry it end-to-end on the Leg:

- `LegTime(int Seconds, int Meters, string? EncodedPolyline, RouteSource Source)` —
  internal, cached (the cached value now carries geometry + source).
- `LegDto(int Seconds, int Meters, string? EncodedPolyline, RouteSource Source)` —
  the wire contract the frontend reads.
- `GoogleRouteService` on success → `Routed` + the response `encodedPolyline`;
  `HaversineRouteService` (and any Google-failure fallback) → `Estimated`, polyline
  `null`.

Naming is by **quality, not vendor** (`Routed`/`Estimated`, not `Google`/`Haversine`):
the frontend and telemetry key on meaning (the "ประมาณ" treatment shows when
`Source == Estimated`), and swapping the routing provider or the estimator never
touches the contract or the UI.

**Glossary amendment (this ADR):** CONTEXT.md's **Leg** entry is updated from "travel
time comes from the Routes API, **not an estimate**" to acknowledge the honest
degraded mode — travel time comes from the Routes API (`Routed`), and **falls back to
a clearly-labelled straight-line estimate (`Estimated`) when the Routes API is
unavailable.** This also reconciles ADR-007's "no key-less mode" wording with the
`HaversineRouteService` that has always existed as the fallback.

The **UI treatment** of an `Estimated` Leg (map line style, itinerary badge, day
summary) and the **observability** of the fallback are separate decisions (follow-on
ADRs).

## Consequences

**Positive:** The estimate is never again shown as if it were routed truth — the
Leg self-declares. Telemetry and UI both key off one meaningful field. The contract
survives a provider swap. The glossary now matches reality.

**Negative:** Every layer (domain enum, `LegTime`, cache entry, `LegDto`, the TS
`api.ts` type, and consumers) gains a field — a wider change than a single nullable
polyline. `RouteSource` is a new domain concept to keep documented.
