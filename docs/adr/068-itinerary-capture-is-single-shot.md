# Itinerary Capture is single-shot — one place → one Stop → return to the itinerary

```mermaid
flowchart TD
    Q{"After capturing one new place in the add-stop flow, stay armed or return?"} -->|chosen| A["Add the Stop and return to the itinerary immediately (single-shot)"]
    Q -->|rejected| B["Stay armed to capture several places in a row (each becomes a Stop), exit on Done — mirrors ADR-016 but hides the itinerary while adding"]
```

The Places-tab **Capture** stays armed after each add for bulk library entry (ADR-016). The
itinerary add-stop flow deliberately diverges: it captures exactly one Place, schedules it as
a **Stop**, and bounces straight back to the itinerary so the user immediately sees the new
Stop in context (tight feedback loop). To add another, they tap "+ เพิ่มจุดแวะ" again. This
divergence from ADR-016 is intentional — the two flows have different goals (bulk library fill
vs. add this one stop). See [[067]].
