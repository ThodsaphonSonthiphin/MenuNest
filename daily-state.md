---
type: daily-state
schema_version: 1
updated: '2026-07-16T15:57:07+07:00'
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
