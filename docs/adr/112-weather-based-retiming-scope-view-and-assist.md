# Weather-based retiming: view hourly forecast AND assist arrival timing

```mermaid
flowchart TD
    Q{issue #46 "show the possible heat" — how far?} -->|chosen| A["view Hourly forecast + assisted retiming to a target hour"]
    Q -->|rejected| B["display-only hourly view (user adjusts by hand)"]
    Q -->|rejected| C["full auto-scheduler (app moves times without asking)"]
```

Issue #46 asks to see per-hour temperature "so I can plan right", and the user clarified the goal is to *arrive when the temperature is what they want*. So Phase 1 delivers **both**: an **Hourly forecast** view on a Stop, plus **Weather-based retiming** that lets the user pick a target hour and one-tap re-time the plan to hit it. A pure read-only view was rejected as not meeting the stated goal; a silent auto-scheduler was rejected because the user must stay in control — retiming is suggested then confirmed (ADR-113).
