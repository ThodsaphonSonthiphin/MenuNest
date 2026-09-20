# Trip detail — map-driven redesign: implementation plan

> **For agentic workers:** steps use checkbox (`- [ ]`) syntax for tracking. Each task carries its
> own files, commands and checks. Work them in order — Task 6 and Task 7 both depend on Tasks 2–5.

**Goal:** turn trip detail into one screen with one map — full-bleed `TripMap`, the day's plan on a
draggable **Plan sheet** (mobile/tablet) or a floating **Plan panel** (desktop), pins that are
tappable, library **Places** as **Ghost pins**, and the **+** control arming the map already on
screen instead of opening a second one.

**Issue:** #154 (closes #6) · **Scope:** frontend only — no endpoint, DTO or EF change.

**Spec (the contract):** `docs/superpowers/specs/2026-09-20-trip-detail-map-driven-redesign-design.md`
**ADRs:** `menunest-234` … `menunest-241` · **Vocabulary:** `CONTEXT.md`

**Mocks (visual source of truth, NOT in this repo):** Claude Design → *MenuNest design system* →
`screens/trip-detail-mobile.html` (4 frames) and `screens/trip-detail-desktop.html` (option A is the
one we build). Fetch with `DesignSync get_file` against project
`107862ef-c14b-42f4-a8f2-4bbe36951e25`.

**Architecture:** `TripDetailPage` owns the layout and the single `TripMap`. A breakpoint switch
puts the same plan contents into a shared `BottomSheet` or into `PlanPanel`. The plan's data hooks
are hoisted into one `usePlan` hook so the map and the list can never drift; `PlanSummary` renders
the sheet/panel header (or the **Compact stop card** while a Stop is selected) and `PlanContent`
renders the list. Selection is one piece of Redux state read by both the map and the list.

**Tech stack:** React 19, Vite, TypeScript, Redux Toolkit + RTK Query,
`@vis.gl/react-google-maps` 1.8.3, `@dnd-kit`, vitest (node env), Playwright.

---

## Global constraints

- **The frontend has NO component or visual test harness.** `vite.config.ts` runs vitest in
  `environment: 'node'` — no jsdom, no RTL. `tsc`, `npm run build` and the unit suite **cannot**
  catch a rendering, layout or CSS bug (CLAUDE.md; learned on #33, #46, #97). Two consequences this
  plan is built around: every decidable rule goes into `pages/trips/lib/*.ts` so vitest can test it,
  and **Task 8's Playwright spec plus Task 9's interactive check are the only real gates.**
- **Prod deploys on push to `main`.** A broken render ships. Never push `main` from this work.
- **Every commit references the issue** — `type(scope): summary (#154)`, `(closes #154)` on the last.
- **`git add <explicit paths>` only.** `daily-state.md` and `AGENTS.md` must never enter a commit.
- **The pre-commit hook runs the whole suite** (backend `dotnet build` + `dotnet test`, frontend
  `tsc --noEmit` + `npm run build`). It is only armed once husky is installed — `npm install` in
  `frontend/`. If it is not armed, run the four checks by hand. Do not `--no-verify`.
- **No new colour hexes.** Everything comes from `pages/trips/trips-tokens.css` and the mock's own
  token block, which uses the same palette (`--teal #0e8f9e`, `--ink #0f172a`, `--border #eef2f6`).
- **Out of scope, explicitly unchanged:** `StopDetailSheet` and everything it hosts, `useSchedule`,
  timing flags, weather, tide, `@dnd-kit` reorder, `StopEditorDialog`, `PlaceEditorDialog`,
  `EditTripDialog`, `/discover`, `/trips`.

## Verified before planning (do not re-litigate)

- `AdvancedMarker` **does** take `onClick` (`AdvancedMarkerEventProps`, `node_modules/@vis.gl/react-google-maps/dist/index.d.ts:669`)
  — the handoff's open assumption is closed. `DiscoverMap` already relies on the imperative
  equivalent (`marker.addListener('gmp-click', …)`).
- The slice fields being removed are used in exactly two files — `TripDetailPage.tsx` and
  `ItineraryTab.tsx`. Nothing else in the app reads `activeTab`, `placesView`,
  `itineraryMapExpanded`, `activeStopId` or `addStopForDayId`.
- There is **no add-day endpoint**. `+ วัน` has to go through `updateTrip` with `dayCount + 1`.
- CI has no `VITE_GOOGLE_MAPS_BROWSER_KEY`, so `TripMap` renders `.trip-map-fallback` there. A
  Playwright spec therefore **cannot** assert on pins — it can only assert on the sheet, the
  detents, the panel and the chrome.

## File structure

| File | Responsibility |
|---|---|
| `frontend/src/pages/trips/lib/detent.ts` (+ test) | detent cycle, heights, nearest-on-release |
| `frontend/src/pages/trips/lib/fitPadding.ts` (+ test) | `fitBounds` padding per surface, clamped |
| `frontend/src/pages/trips/lib/ghostPins.ts` (+ test) | ghost partitioning, label density, toggle pref |
| `frontend/src/pages/trips/tripsSlice.ts` (+ test) | `selectedStopId`; dead fields removed |
| `frontend/src/shared/components/BottomSheet.tsx` | the shared sheet: detents, drag, a11y |
| `frontend/src/shared/components/BottomSheet.css` | its styles |
| `frontend/src/pages/trips/components/PlanPanel.tsx` | desktop floating/collapsible container |
| `frontend/src/pages/trips/hooks/usePlan.ts` | the plan's data cascade, hoisted out of `ItineraryTab` |
| `frontend/src/pages/trips/components/PlanSummary.tsx` | sheet/panel header — day stats or Compact stop card |
| `frontend/src/pages/trips/components/CompactStopCard.tsx` | the Selected Stop's card |
| `frontend/src/pages/trips/components/PlanContent.tsx` | segment, stop list, legs, add, drawer, dialogs |
| `frontend/src/pages/trips/components/TripMap.tsx` | ghost layer, pin `onClick`, framing |
| `frontend/src/pages/trips/TripDetailPage.tsx` | the screen: map + chrome + container switch |
| `frontend/src/pages/trips/TripDetailPage.css` | the new screen's styles |
| `frontend/e2e/trips.map-driven.spec.ts` | the smoke spec |
| *(deleted)* `frontend/src/pages/trips/components/ItineraryTab.tsx` | split into the three above |

---

## Task 1 — pure lib modules

- [ ] `lib/detent.ts`: `Detent = 'summary' | 'half' | 'full'`; `nextDetent` cycles
      summary → half → full → summary (Material 3's handle rule); `detentHeight(detent, viewportH,
      summaryH)` → summary is the measured header height, half is `0.55 × dvh`, full is
      `0.85 × dvh` **capped so the sheet never covers the floating top bar** (spec §4.2);
      `nearestDetent(heightPx, viewportH, summaryH)` for drag release;
      `nextDetentLabel(detent)` → the Thai label naming the **next** state, for `aria-label`.
- [ ] `lib/fitPadding.ts`: `mobileFitPadding(sheetHeightPx, viewportHeightPx)` →
      `{top: 88, right: 20, bottom: sheet + 24, left: 20}`, with the bottom **clamped** so
      `top + bottom` always leaves a usable strip — an unclamped padding larger than the container
      makes `fitBounds` behave unpredictably at the `full` detent.
      `desktopFitPadding(panelCollapsed)` → `left: 408` open, `left: 52` collapsed, 28 elsewhere.
- [ ] `lib/ghostPins.ts`: `buildGhostPins(places, days, activeDayId)` → every **Place** on the Trip
      that is not a **Stop** on the active **Day**, each carrying `scheduledDayNumber: number | null`
      (the 1-based Day it *is* scheduled on, or null) so §6.1's `อยู่ในวัน M` / `ไปวัน M` card can
      be rendered without a second pass; non-finite coords dropped, as `useDayRoute` drops them.
      `showGhostLabels(count)` → `count <= 12` (menunest-241). `readGhostPref()` / `writeGhostPref()`
      wrap `localStorage` in try/catch and default to **shown**.
- [ ] A vitest spec per module. Pure functions only — no React, no DOM beyond a stubbed
      `localStorage`.

**Check:** `npx vitest run src/pages/trips/lib`

## Task 2 — the slice carries one selection

- [ ] `activeStopId` → `selectedStopId`, `setActiveStop` → `setSelectedStop`.
- [ ] Remove `activeTab`, `placesView`, `itineraryMapExpanded`, `addStopForDayId` and their
      actions/types. `addMode` is the single armed flag; the active Day is implicit.
- [ ] `lib/addStopCapture.ts`'s `addStopDayLabel` **stays** — it now names the armed banner's day
      from `activeDayId`, so its unit test stays green untouched.
- [ ] Rewrite `tripsSlice.test.ts`: `addMode` default/toggle, `selectedStopId` default/set/clear.

**Check:** `npx vitest run src/pages/trips`

## Task 3 — `BottomSheet` (shared) and `PlanPanel`

- [ ] `shared/components/BottomSheet.tsx`. Props: `detent`, `onDetentChange`, `summaryHeightPx`,
      `header`, `children`, `onVisibleHeightChange`. Renders at the **full** detent's height and
      reveals itself with `transform: translateY()`, so the body never reflows on a detent change
      and the drag is a pure transform.
- [ ] Non-modal — **no scrim**, and nothing in the sheet's own box may swallow map input.
- [ ] Drag from the grab handle and the header only (spec §4.2 — this is what sidesteps nested
      drag/scroll hand-off). Pointer events, `setPointerCapture`, `nearestDetent` on release. The
      body is a plain scroll container.
- [ ] The handle is a `<button>` with `aria-expanded` and a Thai label naming the next detent;
      tapping it cycles the three detents.
- [ ] `transition: transform 220ms cubic-bezier(.32,.72,0,1)`, dropped while dragging and under
      `@media (prefers-reduced-motion: reduce)`.
- [ ] `components/PlanPanel.tsx`: 376px, inset 16px over the left edge, `border-radius: 16px`,
      elevated, with a rail `<button aria-expanded>` on its right edge that collapses it.

**Check:** `npx tsc -b` — plus the Task 8 spec, which is what actually exercises them.

## Task 4 — split `ItineraryTab`

- [ ] `hooks/usePlan.ts` — hoists `useGetItineraryQuery`, `useListTripPlacesQuery`,
      `useListTripsQuery`, `useSchedule`, `useStopWeather` and the derived day/nav/month values out
      of `ItineraryTab`. It shares RTK Query's cache with `useDayRoute`, so no extra subscription is
      created. Rules of Hooks: it must call `useSchedule` unconditionally with the `EMPTY_DAY`
      fallback exactly as `ItineraryTab` does today.
- [ ] `components/PlanSummary.tsx` — `วัน N · M จุด`, `x/y มาแล้ว`, `เริ่ม–เสร็จ`, `เดินทางรวม`,
      `นำทาง`, and the `DayStartEditor`. Renders `CompactStopCard` instead when a Stop is selected.
- [ ] `components/CompactStopCard.tsx` — ordinal + name, `ถึง / ออก / อยู่`, timing flag, weather
      reading, `นำทาง` · `แก้ไข` · `มาแล้ว`, plus `ดูทั้งหมด` and ✕. **It reads its fields from the
      same `useSchedule` projection the list card uses — no second derivation** (spec §6.3).
- [ ] `components/PlanContent.tsx` — the `แผนวัน` / `คลัง` segment, the stop toolbar, the DnD stop
      list with `TravelLeg`s, `+ เพิ่มจุดแวะ`, the `มาแล้ว` drawer, `StopDetailSheet`,
      `StopEditorDialog`. `คลัง` lists this Trip's Places for *editing* via the existing
      `PlaceCard` → `PlaceEditorDialog` (menunest-235).
- [ ] Clicking a stop card **selects** it (`setSelectedStop`) as well as opening its detail; a
      selected card carries the mock's `.card.sel` treatment.
- [ ] **Keep `data-testid="itin-stop-card"` and `data-testid="stop-drag-handle"` byte-identical** so
      `trips.reorder.spec.ts` keeps passing.
- [ ] Delete `ItineraryTab.tsx`.

**Check:** `npx tsc -b && npx vitest run`

## Task 5 — `TripMap`

- [ ] **Framing (menunest-239):** `path` for `FitBounds` is the active Day's Stop coordinates only.
      `viewerLocation` no longer extends the bounds — `useDayRoute` keeps prepending it to
      `segments` so the Approach leg still draws.
- [ ] `FitBounds` re-runs on detent / panel-collapse change as well as on `ResizeObserver`: the
      container does not resize, so the observer never fires (spec §6.2, ADR-026's grey-tile note).
      A new `fitPadding` object identity per settled detent is what drives it — compute it from the
      **settled** detent, never from the live drag height, or the map refits on every pointer move.
- [ ] **Route pins are real `<button>`s** with an accessible name
      (`จุดที่ 2 — The View Ferris Wheel Pattaya, ถึง 11:20`) and an `onClick` on both the button and
      the `AdvancedMarker`; selecting the same Stop twice is idempotent, so the double path is safe.
- [ ] The selected pin enlarges (mock's `.pin.sel`: 36px dot, teal halo, inverted callout) and the
      map pans to it — `setCenter`, not `panTo`, under `prefers-reduced-motion`.
- [ ] **Ghost layer renders in route mode too** — today `TripMap` renders `places` only when
      *not* in route mode. 12px desaturated dot, label beside it, below the route pins; labels drop
      above 12 in view.
- [ ] A tap on empty map (not armed) clears the selection.

**Check:** `npx tsc -b`

## Task 6 — `TripDetailPage` and the CSS

- [ ] Full-bleed map; floating top bar (back · name · `date · N วัน · ประจำวัน` · edit pencil,
      reusing `TripDateEditor` and `EditTripDialog` as-is); Day chips below it, rendered only when
      `dayCount > 1`, with `+ วัน` going through `updateTrip` at `dayCount + 1` (disabled when
      `isDaily`); control stack bottom-right above the sheet — `คลัง · N` ghost toggle, locate-me,
      **+**; the day roll-up badge top-right.
- [ ] **+ arms the map already on screen.** Delete the `.capture-overlay` second `TripMap`.
- [ ] `AddPlacePreviewCard`'s free `secondaryLabel` / `onSecondary` slot on the trip surface becomes
      **เก็บเข้าคลัง** — without it a ghost pin could never be created, because the Places tab's
      library-only **+** is the control being removed (spec §6.4).
- [ ] The armed banner stays the thin `.add-capture-banner` strip with its explicit exit (R8.3 —
      #36 shipped a banner that covered the whole map).
- [ ] Breakpoints via `useBreakpoint`, unchanged: `desktop` ≥1024 gets the panel, `mobile` and
      `tablet` get the sheet.
- [ ] Remove the dark `.trip-topbar`, the `464px 1fr` grid, the `.itin-map-band` rules and
      `.capture-overlay`.
- [ ] Diff the built CSS against the mock: tokens, colours, radii, the sheet's
      `border-radius: 20px 20px 0 0` and `0 -6px 26px rgba(15,23,42,.22)`, the panel's 376px /
      16px inset / `border-radius: 16px`, chips, the `.dot` / `.gp` pin treatments.

**Check:** `npx tsc -b && npm run build`

## Task 7 — Playwright

- [ ] `frontend/e2e/trips.map-driven.spec.ts`, stubbing `/api/trips/*`, `/api/trips/*/itinerary`
      and `/api/trips/*/places` the way `trips.search.spec.ts` stubs `/api/trips**`, so it runs
      without a seeded backend and without a Maps key.
- [ ] Assert: the sheet renders at **summary** on open; the handle cycles summary → half → full →
      summary and `aria-expanded` follows; the stop list is reachable from the half detent; on a
      desktop viewport the panel renders and its rail collapses it.
- [ ] Do **not** assert on pins — CI has no `VITE_GOOGLE_MAPS_BROWSER_KEY`, so the map is the
      fallback div there.
- [ ] `trips.reorder.spec.ts` self-skips below 2 Stops, so its passing is **not** evidence the
      itinerary still works — do not lean on it.

**Check:** `npx playwright test e2e/trips.map-driven.spec.ts`

## Task 8 — interactive, on a machine with a browser and a Maps key (NOT cloud)

- [ ] Run the app at a phone viewport and a desktop viewport.
- [ ] **Map tiles repaint after a detent change** — the container does not resize, so
      `ResizeObserver` never fires; this is the one risk the automated gates cannot see.
- [ ] The armed-capture banner: arm the map, confirm the banner is a thin strip with a working exit
      and that a pin tap while armed is not a surprise.
- [ ] A Place-heavy Trip for ghost-pin density and the `คลัง · N` toggle.
- [ ] Diff both surfaces against the two Claude Design cards — tokens, colours, structure. Passing
      the gates is **not** evidence the UI matches the mock (CLAUDE.md, learned on #46).
