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

## Travel & trip planning

- **Trip** — a planned journey owned by one **User** (user-scoped, not
  family-gated — see ADR-005). It holds a collection of saved **Places** and an
  ordered **Itinerary**, and rolls up a total **estimated cost**.
  _Avoid_: Journey, Tour, Vacation.
- **Day (ItineraryDay)** — one calendar day of a **Trip**'s itinerary; owns an ordered
  list of **Stops** and a **day start time** (default 09:00) from which the **Smart
  Schedule** cascades. Each Day carries its own start time, edited per-Day.
  _Avoid_: Date.
- **Place** — a saved location the user wants to visit, anchored to a Google
  **`place_id`** (the only Maps datum stored indefinitely — see ADR-007). Carries a
  cached snapshot (name, coordinates, address, opening hours) sourced from a live
  Google Maps Platform API, never scraped.
  _Avoid_: Location, Spot, POI, Pin.
- **Stop** — one entry in a Trip's itinerary: a reference to a **Place** plus a
  planned visit time and a **dwell** duration. Ordering Stops produces the route.
  _Avoid_: Visit, Waypoint, Item.
- **Leg** — the travel segment between two consecutive **Stops**; its travel time
  comes from the Google **Routes API**, not an estimate (see ADR-007).
  _Avoid_: Segment, Hop.
- **Dwell** — how long the user plans to stay at a Stop, in minutes. Distinct from
  **Leg** travel time.
- **Travel mode** — how a **Leg** is travelled (walk / transit / drive); chosen per
  Leg and fed to the Routes API. A Trip carries a default mode that new Legs inherit.
- **Smart Schedule** — the per-day itinerary view that cascades arrival/leave times
  from the day start through **Dwell** + **Leg** travel time, and flags each Stop
  against its best-time window and opening hours (see ADR-008).
- **Timing flag** — a warning shown on a **Stop** in the **Smart Schedule** when its
  computed arrival is problematic, stating the reason and a suggested fix in words
  (ADR-019). Three types by **reason** — **closed** (place shut at arrival),
  **off-window** (arrival outside the place's best-time window), **overflow** (the day
  runs past midnight) — and two **severities** by colour — **problem** (red: closed,
  overflow) vs **suggestion** (amber: off-window) (ADR-020, ADR-021). Only the single
  most-severe flag shows per Stop (priority overflow > closed > off-window); a
  well-timed Stop shows **no** flag. _Avoid_: warning, alert, "amber" as a noun.
- **Capture** — bringing a Place into a Trip from Google Maps. The MVP-primary paths
  are **live search** (type a name → Google Places autocomplete suggestions → pick one)
  and **map-tap** (tap a place on the in-app map), both handled client-side (ADR-014,
  ADR-015). **Pasting a link** is kept as a hidden fallback, resolved server-side
  (ADR-007). Every path ends the same way: a Google `place_id` + snapshot → preview →
  category → save. Share-from-Maps (PWA share target) and the browser bookmarklet are
  Phase 2.
- **Navigate hand-off** — opening a day's route or a single **Stop** in the external
  **Google Maps** app via a client-side deep link (Maps URLs), for real turn-by-turn
  navigation. Distinct from the in-app **map** (which only displays); the hand-off
  leaves the app. The whole-day route starts from the device's current location
  through the day's Stops in order (see ADR-011).
  _Avoid_: Directions, Routing (that is the Routes API / **Leg** travel time).
- **Itinerary map band** — the collapsible in-app **map** strip (~188px, expanded by
  default) at the top of the **Itinerary** view on mobile/tablet, showing the active
  **Day**'s numbered **Stops** and **Leg** polylines. It can be collapsed to a thin strip
  to give the stop list more room, and re-expanded. Distinct from the desktop split's
  full-height right-pane map (same data, different container). See ADR-026.
  _Avoid_: peek, mini-map (informal).
- **Weather reading** — a per-**Stop** indication of sky/precipitation conditions, shown as a
  small chip on the Stop. Each Stop carries two readings side by side — **Now** and
  **On-arrival** — never a toggle between them (ADR-028). _Avoid_: forecast (names only one
  reading), alert.
- **Now (weather)** — the current conditions at a **Stop**'s coordinates at the real present
  moment, independent of the **Trip**'s dates. _Avoid_: live (informal, the issue-#10 word).
- **On-arrival (weather)** — the forecast at a **Stop**'s coordinates for that Stop's scheduled
  **arrival** time in the **Smart Schedule** (ADR-008). _Avoid_: trip-date weather, departure weather.
- **Forecast horizon** — the 10-day window ahead of now that the weather provider can forecast;
  an **On-arrival** reading outside it resolves to **No weather data** (ADR-030).
- **No weather data** — the state a **weather reading** shows when it is unavailable: an
  **On-arrival** reading beyond the **forecast horizon** or already in the past, or a provider
  failure. Rendered as a slashed-cloud chip, never hidden silently (ADR-030). _Avoid_: unknown, error.
- _Phase-2 terms (not in MVP — see ADR-009): **Traveller / TripMember**, **Split**,
  **Settle-up**, **Trip expense**, **Trip summary**. Defined when that phase starts._
