# ADR-103: `update_trip_place` writes Notes + Review links through to the master immediately; other enrichment stays push-only

**Date:** 2026-07-20
**Status:** Accepted (Phase 1)
**Issue:** [#44](https://github.com/ThodsaphonSonthiphin/MenuNest/issues/44)
**Relates to:** ADR-064 (per-trip override; enrichment reaches master only via explicit Push-to-master); ADR-102 (Discover reads the master); ADR-051 (Review links reuse `updateTripPlace`).

```mermaid
flowchart TD
    Q{"How do note + review-link edits<br/>reach the master so Discover shows them?"} -->|chosen| A["write-through: update_trip_place overwrites<br/>master.Notes + master.ReviewLinks on every save"]
    Q -->|rejected| B["override + explicit Push-to-master (existing pattern) —<br/>Discover stale until the user pushes (footgun)"]
    Q -->|rejected| C["write-through ALL enrichment (best-time/season/checklist too) —<br/>defeats Push-to-master; larger blast radius"]
```

## Context

Discover reads the master (ADR-102). Under the existing model (ADR-064), review links reach the
master only on explicit **Push-to-master**, so an edit made in a trip would **not** appear on
Discover until pushed — the exact footgun we removed for the note. The user accepted a small,
deliberate semantics change to keep both fields fresh.

## Decision

In `UpdateTripPlaceHandler`, after applying the per-`TripPlace` edits, **write-through**
`Notes` and `ReviewLinks` to the master `PlaceProfile` (create it if absent; otherwise overwrite
just those two fields) whenever the place has a `GooglePlaceId`. **Best-time, season periods, and
the checklist item-set stay push-only** — they still reach the master only via first-enrichment
auto-create or explicit `push_place_profile`. The MCP `update_trip_place` **signature is
unchanged** (it already carries `notes` + `reviewLinks`); only the tool description notes the new
propagation. No-`place_id` places are a no-op (their note/links live on the `TripPlace`, read via
the ADR-102 fallback).

## Consequences

**Positive:** AI or the editor sets a note/review link once and it shows on Discover instantly, no
push step. **Negative:** notes + review links now diverge from the "override until pushed"
behaviour of the other enrichment fields (a deliberate, documented asymmetry); editing links in
one trip updates the shared master (and thus what a future capture seeds), which is acceptable for
a single-user app.
