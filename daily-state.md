---
type: daily-state
schema_version: 1
updated: '2026-07-21T21:34:19+07:00'
---

## Log

- 2026-07-12T20:06:24+07:00 — feat(trips): ReviewLink value object with http(s) URL validation (#33)
- 2026-07-12T20:07:28+07:00 — feat(trips): ReviewLink value object with http(s) URL validation (#33)
- 2026-07-12T20:12:22+07:00 — feat(trips): TripPlace.ReviewLinks list + SetReviewLinks full-replace mutator (#33)
- 2026-07-12T20:16:33+07:00 — "feat(trips):
- 2026-07-12T20:28:31+07:00 — $(cat <<'EOF'
feat(trips): TripPlace.ReviewLinks list + JSON persistence + AddTripPlaceReviewLinks migration (#33)
EOF
)
- 2026-07-12T20:49:04+07:00 — $(cat <<'EOF'
test(trips): relational persistence test guards ReviewLinks nullable column (#33)
EOF
)
- 2026-07-12T20:52:49+07:00 — (commit)
- 2026-07-12T20:54:17+07:00 — (commit)
- 2026-07-12T21:04:08+07:00 — $(cat <<'EOF'
fix(trips): ReviewLinks column NOT NULL default '[]' so zero-review places read back empty not null (#33)
EOF
)
- 2026-07-12T21:12:56+07:00 — $(cat <<'EOF'
feat(trips): ReviewLinkDto + TripPlaceDto.ReviewLinks + ToDto mapping (#33)
EOF
)
- 2026-07-12T21:22:45+07:00 — $(cat <<'EOF'
feat(trips): reviewLinks on update_trip_place (full-replace) + validation + MCP tool (#33)
EOF
)
- 2026-07-12T21:24:56+07:00 — "feat(trips):
- 2026-07-12T21:36:13+07:00 — $(cat <<'EOF'
feat(trips): ReviewLink API type + TripPlaceDto.reviewLinks (#33)
EOF
)
- 2026-07-12T21:42:55+07:00 — $(cat <<'EOF'
feat(trips): reviewLinks client helpers (validate/host/label/sanitize) (#33)
EOF
)
- 2026-07-12T21:43:45+07:00 — "feat(trips):
- 2026-07-12T21:49:41+07:00 — feat(trips): review-link icon + popover on the itinerary Stop card (#33)
- 2026-07-12T21:56:06+07:00 — feat(trips): edit review links in the Stop editor; send on updateTripPlace (closes #33)
- 2026-07-12T22:13:24+07:00 — $(cat <<'EOF'
fix(trips): size review icon in editor header + null-safe reviewLinks validation (#33)
EOF
)
- 2026-07-12T22:14:22+07:00 — "fix(trips):
- 2026-07-12T22:15:30+07:00 — docs(trips): ADR 049-053 + spec + plan + mock + glossary for Place review links (#33)
- 2026-07-13T07:31:02+07:00 — $(cat <<'EOF'
fix(trips): portal review popover so it isn't clipped by the card's overflow:hidden (#33)
EOF
)
- 2026-07-13T07:40:03+07:00 — docs: full-suite-green commit rule + no frontend visual-test harness note (CLAUDE.md) (#33)
- 2026-07-13T09:50:32+07:00 — $(cat <<'EOF'
feat(trips): current-time-start day tracks today's date on single-day trips (#35)
EOF
)
- 2026-07-13T10:02:03+07:00 — $(cat <<'EOF'
feat(trips): top-bar date follows today when current-time-start is on (closes #35)
EOF
)
- 2026-07-13T10:14:49+07:00 — docs(trips): ADR 054-057 + spec + plan + glossary for current-time-start date tracking (#35)
- 2026-07-13T10:59:13+07:00 — refactor(trips): source top-bar date from useDayRoute (single itinerary source, mirrors backend single-day guard); ADR-055 Phase-2 ordering note (#35)
- 2026-07-13T11:01:05+07:00 — (commit)
- 2026-07-13T11:32:06+07:00 — feat(trips): add ChecklistItem domain entity (#23)
- 2026-07-13T11:39:23+07:00 — feat(trips): add PlaceChecklistEntry domain entity (#23)
- 2026-07-13T11:50:22+07:00 — $(cat <<'EOF'
feat(trips): persist ChecklistItem + PlaceChecklistEntry (EF config + migration) (#23)
EOF
)
- 2026-07-13T12:02:01+07:00 — $(cat <<'EOF'
feat(trips): embed Place checklist in TripPlaceDto read model (#23)
EOF
)
- 2026-07-13T12:12:48+07:00 — $(cat <<'EOF'
feat(trips): add ListChecklistItems query (library autocomplete source) (#23)
EOF
)
- 2026-07-13T12:20:17+07:00 — feat(trips): AttachChecklistItem (create-or-reuse by name) (#23)
- 2026-07-13T12:27:25+07:00 — $(cat <<'EOF'
feat(trips): DetachChecklistItem (removes junction, keeps library) (#23)
EOF
)
- 2026-07-13T12:33:20+07:00 — feat(trips): SetChecklistEntryChecked per-place toggle (#23)
- 2026-07-13T12:40:07+07:00 — $(cat <<'EOF'
feat(trips): REST endpoints for place checklist (list/attach/detach/toggle) (#23)
EOF
)
- 2026-07-13T12:46:42+07:00 — feat(trips): MCP tools for place checklist (list/attach/detach/toggle) (#23)
- 2026-07-13T12:51:49+07:00 — $(cat <<'EOF'
feat(trips): add checklist lib pure helpers (#23)
EOF
)
- 2026-07-13T13:02:42+07:00 — $(cat <<'EOF'
feat(trips): RTK Query endpoints + types for place checklist (#23)
EOF
)
- 2026-07-13T13:03:49+07:00 — $(cat <<'EOF'
fix(trips): supply required checklist field at TripPlaceDto call sites (#23)

Task 12 added a required checklist: PlaceChecklistEntry[] field to
TripPlaceDto. addTripPlace's arg type and the useSchedule test's
mkPlace fixture both build TripPlaceDto-shaped literals and need the
new field, same as the existing reviewLinks: [] pattern.
EOF
)
- 2026-07-13T13:09:43+07:00 — $(cat <<'EOF'
feat(trips): add ChecklistIcon inline SVG (#23)
EOF
)
- 2026-07-13T13:18:53+07:00 — $(cat <<'EOF'
feat(trips): place checklist section in the stop editor modal (closes #23)
EOF
)
- 2026-07-13T13:55:46+07:00 — $(cat <<'EOF'
fix(trips): enforce checklist per-place cap + whitespace normalization + deterministic order server-side (#23)
EOF
)
- 2026-07-13T16:51:43+07:00 — docs(trips): design + plan for user-scoped Place profile library (#37) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-13T20:35:51+07:00 — feat(trips): PlaceProfile master entity + junction + migration (#37) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-13T20:41:03+07:00 — feat(trips): seed captured places from the master profile + expose HasProfile (#37) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-13T20:45:00+07:00 — feat(trips): auto-create the master profile on first enrichment (#37) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-13T20:48:12+07:00 — feat(trips): push-to-master endpoint for the place library (#37) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-13T20:51:59+07:00 — feat(trips): add hasProfile + pushPlaceProfile + place-editor slice state (#37) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-13T20:55:08+07:00 — refactor(trips): extract ReviewLinksSection + ChecklistSection shared editor sections (#37) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-13T20:59:10+07:00 — feat(trips): edit place fields from the Places tab + push-to-master (#37) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-13T21:12:07+07:00 — refactor(trips): decouple master auto-create from checklist-attach + push UX polish (#37) — Scrutinize fixes: master auto-create no longer fires on the #23 checklist-attach path (Save/push only); handlePush skips the redundant write when no master exists and shows a success affordance. — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-16T15:21:50+07:00 — feat(trips): pure stop-summary builder for compact itinerary card (#34)
- 2026-07-16T15:24:59+07:00 — feat(trips): StopDetailSheet + forecast-forward weather + FlagNote extract (#34)
- 2026-07-16T15:26:34+07:00 — feat(trips): StopDetailSheet + forecast-forward weather + FlagNote extract (#34)
- 2026-07-16T15:30:07+07:00 — feat(trips): compact stop card + tap-to-detail + reorder-mode toggle (#34)
- 2026-07-16T15:33:00+07:00 — fix(trips): keep reorder toggle reachable + e2e enters reorder mode (#34)
- 2026-07-16T15:34:25+07:00 — docs(trips): design handoff + implementation plan for itinerary detail popup & DnD toggle (#34)
- 2026-07-16T15:57:07+07:00 — fix(trips): full-row tap target + toolbar gate + sheet ordinal (#34) — Scrutinize follow-ups on the itinerary detail-popup/DnD-toggle work: — - Compact card: move the chevron inside the .stop-body button and wrap the text in .stop-text so the whole name/summary/chevron row is one tap target. The chevron (the 'opens detail' affordance) and its 36px column were previously outside the button and did nothing. — - Reorder toolbar: gate the whole bar on (remaining>=2 || reorderMode) instead of remaining>0, so a single remaining stop no longer renders a lone count with no action; the toggle stays reachable in reorder mode. — - Detail sheet: ordinal now counts within the visible remaining list (remaining.indexOf) rather than the full schedule, so 'à¸ˆà¸¸à¸”à¸—à¸µà¹ˆ N' matches the card the user tapped when visited stops are hidden. — Verified: tsc -b + vite build clean, vitest 168/168.
- 2026-07-16T17:06:40+07:00 — $(cat <<'EOF'
feat(trips): add addStopForDayId capture-context flag to trips slice (#36)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-16T17:13:46+07:00 — $(cat <<'EOF'
feat(trips): attach review links while capturing a place (shared preview card) (#36)
EOF
)
- 2026-07-16T17:22:15+07:00 — $(cat <<'EOF'
fix(trips): reset review drafts when a different place is selected in capture (#36)
EOF
)
- 2026-07-16T17:28:14+07:00 — feat(trips): AddPlaceMode capture-context (banner + relabel + addStop chain) (#36)
- 2026-07-16T17:42:23+07:00 — $(cat <<'EOF'
feat(trips): add a new place (+review link) as a stop from the itinerary picker (closes #36)
EOF
)
- 2026-07-16T17:43:17+07:00 — $(cat <<'EOF'
docs(trips): ADR-067..071 + design spec/plan/mock for add-new-place-from-itinerary (#36)
EOF
)
- 2026-07-16T18:00:47+07:00 — $(cat <<'EOF'
fix(trips): reset capture-context on page teardown + tab-gate it; restore trailing newlines (#36)
EOF
)
- 2026-07-16T20:31:27+07:00 — fix(trips): idempotent capture retry (no duplicate Place on addStop failure) + clear capture-context on itinerary-tab leave (#36)
- 2026-07-17T08:37:01+07:00 — docs(trips): ADR-072..080 + design spec/mock/glossary for place season (#19) — Per-Place season periods (good/avoid months + reason) with master+per-trip-override, AI-fill via MCP (update_trip_place + push_place_profile), and an illustrative weather-diorama off-season warning. Decisions ADR-072..080; spec docs/superpowers/specs/2026-07-17-place-season-design.md; glossary CONTEXT.md. Design only â€” not yet implemented. — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-17T08:53:51+07:00 — docs(trips): implementation plan for place season (#19) — 12-task SDD plan (backend persistence mirroring ReviewLinks, MCP full-replace + push_place_profile, lib/season.ts, year-ribbon editor, WeatherDiorama warning) from the approved spec. Design only. — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-17T09:01:07+07:00 — feat(trips): SeasonPeriod value object + SeasonKind enum (#19)
- 2026-07-17T09:14:36+07:00 — $(cat <<'EOF'
feat(trips): persist SeasonPeriods JSON on TripPlace + PlaceProfile (#19)
EOF
)
- 2026-07-17T09:24:48+07:00 — feat(trips): add SeasonPeriods to TripPlaceDto read model (#19)
- 2026-07-17T09:32:14+07:00 — $(cat <<'EOF'
feat(trips): carry SeasonPeriods through seed/override/push lifecycle (#19)
EOF
)
- 2026-07-17T09:43:05+07:00 — $(cat <<'EOF'
test(trips): make season lifecycle test a genuine DB round-trip (#19)

_db.ChangeTracker.Clear() after the first SaveChangesAsync so the
subsequent PlaceProfiles.SingleAsync reload actually deserializes
SeasonPeriodsJson instead of returning the already-tracked identity-map
instance, mirroring PlaceProfileAutoCreateRelationalTests and
PlaceProfileSeedRelationalTests. Also adds the missing trailing newline.
EOF
)
- 2026-07-17T10:44:20+07:00 — $(cat <<'EOF'
feat(trips): seasonPeriods full-replace on update_trip_place (HTTP + MCP) (#19)
EOF
)
- 2026-07-17T10:56:17+07:00 — $(cat <<'EOF'
feat(trips): expose push_place_profile as an MCP tool (#19)
EOF
)
- 2026-07-17T11:07:04+07:00 — $(cat <<'EOF'
feat(trips): season API types + updateTripPlace arg (#19)
EOF
)
- 2026-07-17T13:52:21+07:00 — $(cat <<'EOF'
feat(trips): lib/season.ts monthStatus + rangeLabel + monthOfDate (#19)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-17T14:03:24+07:00 — $(cat <<'EOF'
feat(trips): PlaceSeasonEditor year ribbon in the place/stop editors (#19)
EOF
)
- 2026-07-17T14:13:34+07:00 — feat(trips): WeatherDiorama illustrative season canvas (#19)
- 2026-07-17T14:28:22+07:00 — $(cat <<'EOF'
feat(trips): on-card off-season warning via monthStatus + WeatherDiorama (#19)
EOF
)
- 2026-07-17T14:52:07+07:00 — docs(settings): ADR-081..085 + CONTEXT term + spec/plan for Home page (#39) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-17T15:07:59+07:00 — fix(trips): scope off-season flex-wrap to season cards only (#19)

The Task-11 rule set flex-wrap:wrap on ALL .stop-card, changing the flex
line-break behaviour of every stop card to serve a band that only season
cards render. Scope it to .stop-card.season-bad/.season-good (appended under
the exact condition the band renders) so no-season cards keep trips-tokens.css's
original single-row layout byte-for-byte â€” removes the long-name-wrap regression
surface flagged in scrutinize, no reliance on a flex hypothetical-size argument.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-17T15:09:53+07:00 — $(cat <<'EOF'
feat(settings): add UserSettings entity + migration for Home page (#39)

EOF
) — $(cat <<'EOF'
Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-17T15:11:17+07:00 — $(cat <<'EOF'
chore(settings): restore trailing newlines on UserSettings files (#39)
EOF
) — $(cat <<'EOF'
Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-17T15:14:50+07:00 — (commit)
- 2026-07-17T15:25:24+07:00 — fix(settings): guard UserSettings.Create/SetHomePath + test (#39) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-18T16:05:03+07:00 — feat(settings): expose HomePath on GET /api/me (#39)
- 2026-07-19T09:07:06+07:00 — feat(settings): add PUT /api/me/settings to set Home page (#39)
- 2026-07-19T20:51:59+07:00 — feat(settings): add home-options pure lib + tests (#39)
- 2026-07-19T21:19:53+07:00 — feat(settings): wire homePath + updateUserSettings into RTK Query (#39)
- 2026-07-19T21:28:22+07:00 — $(cat <<'EOF'
feat(settings): resolve / via HomeRedirect against saved Home page (#39)
EOF
)
- 2026-07-19T21:30:05+07:00 — "feat(settings):
- 2026-07-19T21:39:34+07:00 — $(cat <<'EOF'
feat(settings): add /settings page + account-menu entry for Home page (closes #39)
EOF
)
- 2026-07-19T21:41:29+07:00 — (commit)
- 2026-07-19T22:40:02+07:00 — $(cat <<'EOF'
fix(settings): placeholder for family-less Home dropdown + guard save + review nits (#39)

EOF
)
- 2026-07-20T07:12:13+07:00 — docs(trips): design + plan for UV/feels-like at destination (#40) — ADR-086..093, CONTEXT.md glossary terms, design spec, and the implementation plan for issue #40. Refs #40. — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-20T07:15:12+07:00 — docs(trips): design + plan for UV/feels-like at destination (#40) — ADR-086..093, CONTEXT.md glossary terms, the design spec, and the implementation plan for issue #40. Design/plan only â€” no code implemented yet. Refs #40. — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-20T07:33:38+07:00 — feat(trips): parse UV index + feels-like from weather responses (#40)
- 2026-07-20T08:05:43+07:00 — $(cat <<'EOF'
feat(trips): UV band + feels-like alert helpers and DTO fields (#40)
EOF
)
- 2026-07-20T08:18:05+07:00 — $(cat <<'EOF'
fix(settings): gate Home-page save until profile loads to preserve thresholds (#40)
EOF
)
- 2026-07-20T08:28:22+07:00 — feat(trips): show UV badge + feels-like in the stop detail sheet (#40)
- 2026-07-20T08:29:31+07:00 — chore(trips): restore trailing newline on WeatherIcons.tsx (#40)
- 2026-07-20T08:36:52+07:00 — fix(auth): persist SPA token cache in localStorage so mobile sessions survive tab eviction (#5)
- 2026-07-20T08:39:08+07:00 — fix(auth): persist SPA token cache in localStorage so mobile sessions survive tab eviction (#5)
- 2026-07-20T08:44:56+07:00 — $(cat <<'EOF'
feat(trips): warn on the itinerary card when arrival UV/heat is high (#40)
EOF
)
- 2026-07-20T08:47:31+07:00 — feat(auth): OAuthClient + OAuthRefreshToken entities for durable MCP proxy store (#5)
- 2026-07-20T08:49:16+07:00 — feat(auth): OAuthClient + OAuthRefreshToken entities for durable MCP proxy store (#5)
- 2026-07-20T08:54:46+07:00 — $(cat <<'EOF'
fix(trips): keep the Â· separator out of the UV/heat alert pills (#40)
EOF
)
- 2026-07-20T08:57:00+07:00 — feat(auth): EF migration for OAuth durable stores (#5)
- 2026-07-20T09:06:11+07:00 — $(cat <<'EOF'
feat(auth): durable SQL-backed ClientStore for MCP DCR registrations (#5)
EOF
)
- 2026-07-20T09:12:07+07:00 — $(cat <<'EOF'
feat(settings): add weather-alert thresholds section to /settings (closes #40)
EOF
)
- 2026-07-20T09:15:12+07:00 — feat(auth): durable SQL-backed MCP refresh tokens (single-use rotation) (#5)
- 2026-07-20T09:20:35+07:00 — docs(discover): ADRs 094-100 + spec + plan + mock for place discovery (#42) — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-20T09:41:46+07:00 — $(cat <<'EOF'
fix(trips): carry UV/feels through the weather API+MCP DTO; optimistic settings cache (#40)

The Application-layer WeatherReadingDto was never extended with UvIndex/
FeelsLikeC, so GET /api/trips/.../weather and the MCP get_stop_weather
tool silently dropped both fields even though the domain-layer
WeatherReading already carried them. Also add an optimistic getMe cache
patch to updateUserSettings so a rapid UV-then-feels edit on the
Settings page doesn't clobber the first change via a stale refetch race.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-20T16:01:07+07:00 — docs(discover): ADRs 101-104 + design spec/plan + glossary for review-links & note (#44)
- 2026-07-20T16:02:45+07:00 — docs(discover): ADRs 101-104 + design spec/plan + glossary for review-links & note (#44)
- 2026-07-20T16:09:00+07:00 — feat(places): add Notes to PlaceProfile master + TripPlace.SetNotes + migration (#44)
- 2026-07-20T16:18:52+07:00 — feat(places): write-through Notes + ReviewLinks to master; seed note on capture (#44)
- 2026-07-20T16:27:33+07:00 — feat(discover): surface reviewLinks + note from master (fallback rep) on GET /api/places (#44)
- 2026-07-20T16:34:32+07:00 — feat(discover): show à¸£à¸µà¸§à¸´à¸§ + à¹‚à¸™à¹‰à¸• sections on the place sheet (#44)
- 2026-07-20T16:41:28+07:00 — feat(trips): show note on the stop detail sheet (#44)
- 2026-07-20T16:46:22+07:00 — $(cat <<'EOF'
fix(trips): use --sd-border/hardcoded bg for the stop-sheet note box (portaled scope) (#44)
EOF
)
- 2026-07-20T17:37:58+07:00 — $(cat <<'EOF'
test(discover): document last-write-wins + cap notes in validator + close review gaps (#44)

Addresses final-review findings for #44. All additive â€” no change to the
accepted write-through (last-write-wins) behavior:
- UpdateTripPlaceValidator: cap Notes at 2000 chars (uniform 400), matching
  the domain-level SetNotes cap.
- New relational test reproducing the accepted cross-trip clobber: an empty
  save from one trip overwrites the shared master set up by another trip.
- Domain tests: SetNotes bumps UpdatedAt; an exactly-2000-char note is
  accepted on both PlaceProfile and TripPlace.
- ListMyPlacesHandlerTests: cover the present-but-empty-master fallback
  branch (master row exists but empty -> falls back to the rep TripPlace).
- ADR-103: documented the last-write-wins consequence and mitigations.
- TripTools.cs: update_trip_place tool description now warns that
  notes/reviewLinks are shared per place across trips (last write wins).
EOF
)
- 2026-07-20T20:31:38+07:00 — docs(mcp): note push_place_profile also overwrites master notes (#44)
- 2026-07-20T20:52:52+07:00 — docs(settings): ADR-105/106 + spec/plan for numeric weather-alert thresholds (#45)
- 2026-07-20T20:54:55+07:00 — docs(settings): ADR-105/106 + spec/plan for numeric weather-alert thresholds (#45)
- 2026-07-20T21:00:15+07:00 — feat(settings): add numeric weather-alert control helpers (#45)
- 2026-07-20T21:13:22+07:00 — $(cat <<'EOF'
feat(settings): free numeric UV/feels-like alert thresholds with on-off checkbox (#45)
EOF
)
- 2026-07-20T21:31:37+07:00 — fix(settings): hydrate weather-alert controls once so an off-toggle keeps the typed value (#45)
- 2026-07-21T14:59:14+07:00 — $(cat <<'EOF'
docs(trips): ADRs 107-116 + design spec/plan for weather-based retiming (#46)

Hourly forecast view + suggested/confirmed weather-based retiming on a Stop.
Design note records the spec Â§4.2 deviation (offset resolved client-side for the
web path, server-side for MCP) since arrival is computed only client-side and the
approach leg depends on the viewer's live location. ADR renumber (107-116 collide
with #41's 107-111 on main) deferred to push-time reconciliation.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-21T15:15:05+07:00 — $(cat <<'EOF'
fix(weather): format hourly-forecast URL coords with InvariantCulture (#46)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-21T15:22:06+07:00 — $(cat <<'EOF'
feat(trips): add GetHourlyForecast query + POST /api/trips/weather/hourly (#46)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-21T16:18:17+07:00 — $(cat <<'EOF'
fix(web): show hourly divider once per date rollover with correct label (#46)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-21T18:05:38+07:00 — (commit)
- 2026-07-21T18:18:17+07:00 — $(cat <<'EOF'
fix(weather): roll hourly cache key hourly + harden isDaytime + clarify MCP retime docs (#46)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-21T18:29:03+07:00 — $(cat <<'EOF'
fix(web): retime error handling + result surfacing + tag invalidation + past-hour guard (#46)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-07-21T20:01:12+07:00 — docs(adr): renumber weather-retiming ADRs 107-116 to 112-121 to deconflict with #41 (#46) — #41 (app version) and #46 (weather retiming) both created ADRs 107-111 on main. Shift #46's block up by 5 (107-116 -> 112-121) and update every cross-ref in the ADRs, design spec, plan, CONTEXT.md glossary, and code comments. Docs/comments only; no runtime change. — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-21T20:24:11+07:00 — fix(web): restyle #46 hourly planner to match the approved mockup (#46) — The planner shipped with flat placeholder styling that diverged from the approved mock (MenuNest design system -> Screens -> issue #46) -- plain text links instead of the boxed teal panel. Rebuild: entry = gradient teal pill with icon badge; planner = bordered card with a teal-soft header bar; quick actions = pills (navy night accent); hour cells = bordered day/night boxes with a big feels-like numeral + a 'plan now' marker + coolest rings + solid-teal selected state; suggestion = tinted card with apply/cancel. Behaviour (offset/suggest/classify/coolest + retimeStop) unchanged. Adds --sd-night/--sd-night-bg/--sd-night-cell/--sd-day-cell tokens on the sheet + :root portal fallback. — No frontend visual test harness (CLAUDE.md) -- verify interactively on prod. — Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
- 2026-07-21T20:44:15+07:00 — $(cat <<'EOF'
refactor(trips): extract shared hourlyRolloverLabel helper for reuse in Discover (#47)
EOF
)
- 2026-07-21T20:57:57+07:00 — $(cat <<'EOF'
feat(discover): show hourly weather strip in the place-detail sheet (closes #47)
EOF
)
- 2026-07-21T21:34:19+07:00 — (commit)
