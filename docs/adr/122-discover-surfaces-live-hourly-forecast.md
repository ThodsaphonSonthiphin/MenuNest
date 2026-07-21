# Discover surfaces the live Hourly forecast (reverses the Phase-2 no-live-weather deferral)

```mermaid
flowchart TD
    Q{show live weather in Discover?} -->|chosen| A["yes — surface the Hourly forecast strip when a Place is selected"]
    Q -->|rejected| B["keep the ADR-096 Phase-2 deferral: no live Google call in Discover"]
    Q -->|rejected| C["show the two-chip Now / On-arrival Weather reading (needs a scheduled arrival — none exists in Discover)"]
```

Issue #47 ("แสดงสภาพอากาศรายชั่วโมงนี่นี่ด้วย") asks for hourly weather on the **Discover**
place-detail sheet (`PlaceSheet`). ADR-096 had deliberately deferred *live Weather reading in
Discover* to Phase 2 to keep Discover call-free (all four **Discovery signals** are computed from
already-stored data). We reverse that deferral **only for the Hourly forecast** — the one weather
surface #47 names — not the two-chip **Weather reading** (Now / On-arrival), which stays trip-only
because it needs a Stop's scheduled **arrival** that Discover has no concept of. The four
Discovery signals remain no-call; the Hourly forecast is a distinct, opt-in-by-selection surface.
Cost is bounded (see ADR-123) and reuses the existing `forecast/hours` walk — no new billing SKU
(ADR-119).