# Discover capture — design spec (issue #48)

**Status:** Approved. Consolidates decision map [#53](https://github.com/ThodsaphonSonthiphin/MenuNest/issues/53)
(`discover-add-place-48`), 13 of 13 tickets resolved, no fog remaining.

**Goal:** A user can add a place from inside Discover (ไปไหนดี) from a Google Maps URL, by tapping
the map, from a latitude/longitude pair, and from a Plus Code. The captured place then appears in
Discover. The capture path is shared with the Trips add-place flow so both surfaces gain all four
inputs, and the capability is exposed over MCP.

**Source of truth:** this spec consolidates ADR-148 through ADR-165 so an executor reads one
document instead of nineteen. Where this spec and an ADR disagree, **the ADR wins** — each
requirement below cites the ADR that decided it, and that ADR carries the rejected alternatives and
the reasoning.

**Approved mocks (fidelity references, not illustrations):** Claude Design project `8d8d4c81`
(*MenuNest design system*), group Screens —
`screens/issue-48-discover-capture.html` (the capture surface, ADR-164) and
`screens/issue-6-add-stop-picker.html` (the add-stop picker, ADR-165).

---

## Global constraints

These apply to every plan and every task below.

- **UI copy is Thai.** Backend error messages stay English and are not translated (ADR-145); the SPA
  words user-facing failures in Thai itself.
- **Icons are inline-SVG components. Never emoji.** `@syncfusion/react-icons` is not installed.
- **Three classes implement `IApplicationDbContext`** — `AppDbContext` (prod),
  `SqliteAppDbContext` and `InMemoryAppDbContext` (tests). A new `DbSet<>` must be added to all
  three or the build fails `CS0535`. A new *column* on an existing entity needs none of them:
  `SqliteAppDbContext` applies the real Infrastructure configurations, and `InMemoryAppDbContext`
  only mirrors value conversions.
- **Backend tests use xUnit + Moq + FluentAssertions.** `Substitute.For<>` (NSubstitute) will not
  compile. Four test projects exist under `backend/tests/`; put a test beside the layer it
  exercises.
- **An entity/property and its EF configuration must land in the same commit.** An unmapped or
  invalid model fails EF model validation for every test touching the `DbContext`, and the
  pre-commit hook runs the whole suite.
- **Migrations are applied to prod BY HAND.** Neither `Program.cs` nor the CD workflow calls
  `db.Database.Migrate()`. A deployed API with an unapplied migration throws
  `Invalid object name` / `Invalid column name` and the SPA shows "An unexpected error occurred" —
  this exact outage happened on #49. The prod SQL server firewalls by IP; add a temporary rule,
  apply, remove it.
- **`git add <explicit paths>` only** — never `-A` or `.`. `daily-state.md` and `AGENTS.md` are
  working files that must never enter a feature commit.
- **Every commit references the tracking issue** — `(closes #48)` on the last one, `(#48)` or
  `Refs #6` / `Refs #95` elsewhere.
- **The frontend has no component/visual test harness.** vitest runs in `environment: 'node'` with
  no jsdom and no React Testing Library, so `tsc -b`, `npm run build` and the unit suite cannot
  catch rendering, layout, CSS or DOM-interaction bugs. Every UI task must be verified
  interactively, and any mockup-backed task must have its produced CSS diffed against the approved
  card — the review gates are blind to visual fidelity (#46 shipped a flat HourlyPlanner through
  all of them).
- **Prod deploys on push to `main`.** Smoke-test any map / overlay / layout change before pushing.

---

## Plan split

Four plans, in dependency order. Each produces working, testable software on its own.

| plan | scope | depends on |
|---|---|---|
| **A — identity & idempotency** (backend + the one frontend passthrough) | `TripPlace.OriginTripPlaceId`, its migration, the `ListMyPlaces` group key, `AddTripPlace` idempotency and the enrichment copy | — |
| **B — four-input resolver & MCP** | `IPlaceResolver` widened, offline Plus Code, `ResolvedPlaceDto` gains `alreadySaved` / `nearMatches` / `derivedFrom`, `resolve_place` widened, `list_my_places` added | A |
| **C — shared capture component & Discover armed mode** | one surface-agnostic component with four mode pills, armed mode on both maps, trip picker fix | A, B |
| **D — add-stop picker** | two sections, library search, two-line rows, three empty states | A |

**A must ship before D**: without `OriginTripPlaceId` a copied coordinate place splits into a second
Discover card, which is the exact defect ADR-156 exists to prevent (ADR-158 §6).

---

## Requirements

### 1. Where a captured place lives — ADR-155

- **R1.1** A Discover capture writes a **`TripPlace`**. No new entity, no new `DbSet<>`, no
  migration for this requirement on its own.
- **R1.2** `ListMyPlacesHandler` keeps its single query — one join, the `t.DeletedAt == null` gate,
  and the `rows.Count == 0` early return.
- **R1.3** The capture preview offers **two same-level buttons**: `เพิ่มเข้าทริป` (pick an existing
  Trip) and `สร้างทริปใหม่` (the zero-input create-and-seed of ADR-098). `สร้างทริปใหม่` is a
  sibling button and never becomes a row inside the trip picker.
- **R1.4** A place is lost from Discover when its Trip is soft-deleted. **Accepted** — this is
  already what happens to every trip-captured place.
- **R1.5 (prerequisite)** The trip picker must be fixed **before capture ships**.
  `AddToTripDialog` calls `useListTripsQuery()` with no arguments, so `ListTripsHandler` applies
  `Take = 10` ordered by `StartDate` **ascending** — the ten oldest-starting Trips, no search, no
  paging. Since the picker becomes the mandatory gate for every capture, it must pass
  `take: 100` and `sortColumn: 'startDate'`, `sortDirection: 'Descending'`, and add a search box
  bound to the `search` parameter `ListTripsHandler` already implements. **No backend change.**

### 2. One identity across trips — ADR-156

- **R2.1** `TripPlace` gains a nullable **`Guid? OriginTripPlaceId`**. It is **opaque**: no foreign
  key, no index. Nothing queries by it — the grouping runs in memory after `ToListAsync`.
- **R2.2** It is written **only** when a row is created by adding an existing Discover place to a
  Trip, and **unconditionally** in that case (even when a `place_id` is present). Every fresh
  capture, every existing row and every Trips-side add leaves it `null`.
- **R2.3** The value stored is **already the root**. `DiscoverPlaceDto` gains
  `Guid OriginTripPlaceId`, computed at read time as `rep.OriginTripPlaceId ?? rep.Id`; the client
  passes that value through and the handler **stores it verbatim, performing no lookup**. This is
  what makes a chain (Oct ← Dec ← Mar) impossible.
- **R2.4** The group key becomes `GooglePlaceId ?? $"tp:{p.OriginTripPlaceId ?? p.Id}"`.
  `GooglePlaceId` still wins whenever present, so the column is inert for the common case.
- **R2.5** `AddTripPlaceCommand` gains `Notes`, `ReviewLinks`, `BestTimeWindows` and
  `SeasonPeriods`, applied **per field — only to whatever the master left empty**, not gated on
  `PlaceProfileSync.SeedIntoAsync`'s return value as a whole. `SeedIntoAsync` returning `true`
  only means a master **row** exists; `BestTimeWindows`/`SeasonPeriods` are push-only
  (`UpdateTripPlaceHandler` never writes them through), so an existing master is routinely empty
  for those two fields. An all-or-nothing gate on "a master exists" would silently drop them even
  though the Discover card the user just tapped displayed them (read from `rep`, per
  `ListMyPlacesHandler`'s own empty-aware fallback). A master that actually holds a value for a
  field stays canonical for that field.

### 3. Duplicates — ADR-149

- **R3.1** An exact `place_id` match is **idempotent, never a second row**. Detection fires at
  **resolve time**, before the capture form renders, so the form opens in an "already saved" state
  and the user never types a category or review links that would be discarded.
- **R3.2** The **handler is idempotent too** — it pre-checks and returns the existing row instead of
  inserting. One policy covers the SPA, MCP and a race.
- **R3.3** **Nothing is merged.** The capture's enrichment is not written onto the existing place.
- **R3.4** **No English error reaches the user on this path.** The SPA words it in Thai and offers
  to open the existing place.
- **R3.5** A `place_id`-less **near match warns and never blocks**: scan the caller's whole library
  for places within **100 m**, show the **nearest 3**, non-blocking. The name is displayed so the
  user can judge but **does not participate in the predicate** — no fuzzy matching over freeform
  Thai names. The primary button stays enabled.
- **R3.6** `TripPlace` keeps its filtered unique `(TripId, GooglePlaceId)` index
  (`HasFilter("[GooglePlaceId] IS NOT NULL")`) as the integrity backstop **under** the idempotent
  handler. `place_id`-less rows are excluded by the filter, so two coordinate captures remain two
  rows — deliberately.

### 4. A coordinate place — ADR-148 §1

- **R4.1** **`Name` is a required form field** for the lat/lng and Plus Code inputs. There is no
  "save without a name".
- **R4.2** On entering a coordinate, make **one best-effort reverse-geocode call** to prefill
  `Address` and offer that address as a *suggested* name the user may accept or overwrite.
- **R4.3** **The lookup never blocks capture.** If Geocoding is disabled, restricted, rate-capped or
  fails, the capture still succeeds with a user-typed name and a null address.
- **R4.4** `Address` stays optional.

### 5. Plus Code — ticket #57

- **R5.1** The Trips `searchText` resolver returns **zero results for every Plus Code**, so it
  cannot be reused.
- **R5.2** Use the **offline `open-location-code`** decode ($0). Geocoding also works but on a wrong
  locality is confidently ~500 km off, which is why a short code must ask for its locality rather
  than guess it from the map camera.

### 6. Google Maps URL forms — ticket #56

- **R6.1** Today only the `/place/<name>/` segment is read and Text-Searched by name. 12+ shapes are
  rejected and 7 resolve **silently wrong**.
- **R6.2** Worst live defect: **every Google ccTLD short link is rejected in prod**, and CI hides it
  by stubbing `.com`.
- **R6.3** Fixing `GooglePlaceResolver`'s pre-existing accuracy weakness (it discards the `place_id`
  present in a long URL and re-finds the place by name, which can return a different branch of a
  chain) is **out of scope** — see below.

### 7. Cost — ticket #58

- **R7.1** `priceLevel` + `regularOpeningHours` already pin **both** existing field masks to
  **ENTERPRISE**: Text Search $35/1k, Place Details $20/1k, 1k free per month each.
- **R7.2** Coordinates and Plus Codes need at most **Geocoding $5/1k with 10k free per month**, and
  the coordinate path makes no call at all.
- **R7.3** An accidental armed tap must never write. Its full cost: POI → one Place Details call and
  a card to dismiss; empty ground → $0 and a form to dismiss; own pin → $0 and a toast.

### 8. Armed Capture mode — ADR-150, generalised by ADR-163 §3

- **R8.1** Capture is a **distinct armed mode**. Unarmed, the Discover map behaves exactly as today:
  a tap on your own pin selects and opens `PlaceSheet`; POI and ground taps do nothing.
- **R8.2** While armed, **every tap belongs to capture**, one rule with no per-surface branch:

  | target | behaviour |
  |---|---|
  | a Google POI | `ev.stop()`, resolve the `place_id`, show the preview card |
  | empty ground | drop a draft pin and open the **coordinate** input prefilled with that lat/lng; **no Geocoding call** |
  | one of the user's own pins | a `มีอยู่ในคลังแล้ว` toast and nothing else — no `PlaceSheet`, no Weather call |
  | a cluster | unchanged; the clusterer zooms in |

- **R8.3** The mode is signalled **three ways at once**: a **thin** banner strip across the top
  (the `.add-capture-banner` treatment Trips already ships — never a fill; #36 shipped a banner that
  covered the whole map and read as a black screen), the user's own pins **dimmed**, and
  `PlaceBottomSheet` swapped for the capture sheet. Armed from a `+` FAB above the bottom sheet;
  exited by the banner's `‹` or `Esc`.
- **R8.4** **After a successful add the mode stays armed** and the form clears. The new place
  immediately renders as one more dimmed own-pin, which doubles as the confirmation.
- **R8.5** At Discover, arming **remembers the Trip chosen at the previous save**, so a run of
  captures picks a Trip once (ADR-163 §4). Once remembered, the first preview button carries its
  name — `เพิ่มเข้า เชียงใหม่ ก.ย. ▾` — and commits in one tap; the `▾` opens the trip picker.

### 9. One shared capture component — ADR-163

- **R9.1** **One component, surface-agnostic.** It does **not** take `tripId`. It takes a **commit
  target** — the callback that turns a resolved place plus the form's category and review links
  into a saved row — and each surface supplies its own.
- **R9.2** **Both maps accept all four inputs.** The trip map gains the coordinate and Plus Code
  inputs **in the same change**, not a later one.
- **R9.3** The tap rules of R8.2 are identical on both maps. This **supersedes ADR-016 §2**.
- **R9.4** **Add-as-stop stays Trips-only.** A Discover capture writes a `TripPlace` and stops
  there; the route from library to itinerary is the add-stop picker of §11, not a day selector
  bolted onto Discover.
- **R9.5** **POI-tap resolution stays client-side** on the Maps JS SDK — the tap already carries a
  `place_id` and both surfaces render a `<Map>`. The URL, coordinate and Plus Code inputs route
  through `resolve_place`.

### 10. The four inputs and how the user switches — ADR-164

- **R10.1** **Four explicit mode pills** in the existing `.seg-tab` treatment —
  `ค้นหาชื่อ` · `ลิงก์ Google Maps` · `พิกัด` · `Plus Code` — with `ค้นหาชื่อ` active by default.
  Each mode owns its field, placeholder, confirm button, validation rule and error copy. A single
  smart field was rejected: `2R+59 เชียงใหม่` is both a plausible search and a valid short Plus
  Code, and a wrong guess collapses R10.4's errors into a vague "not found".
- **R10.2** **The map tap is not a pill.** It is a gesture on the surface behind the sheet,
  advertised as a hint line under the active field. An empty-ground tap switches the sheet to
  **พิกัด** with the tapped lat/lng prefilled.
- **R10.3** Every resolve failure **keeps the user in the mode they chose**, with the other modes one
  tap away and named as the escape route. Nothing is written on a failed resolve.
- **R10.4** Three distinct error states, each naming its own fix:
  `อ่านลิงก์นี้ไม่ได้` (+ "open it in Google Maps and use แชร์ → คัดลอกลิงก์, or switch to พิกัด /
  Plus Code"), `รูปแบบ Plus Code ไม่ถูกต้อง` (missing `+`), and
  `Plus Code สั้นเกินไป ต้องบอกเมืองต่อท้าย` (short code with no locality).
- **R10.5** `PlaceLinkFallbackDialog` **stops being a dialog**. Its `resolvePlace` call and Thai copy
  move into the `ลิงก์` mode of the sheet; the URL path becomes one of four peers rather than a
  hidden fallback.
- **R10.6** The capture form collects **what Trips collects today** — category (colour dot, prefilled
  from the Google guess with a `เดาจาก Google` badge that hides once the user overrides it) and
  review links.

### 11. The add-stop picker — ADR-158 and ADR-165

- **R11.1** **Two sections.** `ในทริปนี้` — `useListTripPlacesQuery(tripId)`, today's content and
  today's one-call `addStop`, unchanged. `หรือเลือกจากคลังสถานที่` — `useListMyPlacesQuery()`
  **minus every card whose `trips[]` already contains this `tripId`**. A section heading, **not** a
  per-row Trip chip.
- **R11.2** **One tap on a library row = `addTripPlace` then `addStop`.** No confirm step. The copy
  carries `originTripPlaceId` and the enrichment per §2, so Discover keeps showing one card.
- **R11.3** A **half-done tap degrades into a valid state**, not corruption. If `addStop` fails
  after `addTripPlace` succeeded the place is in the Trip's pool, just unscheduled — and it
  self-corrects on screen, because `addTripPlace` invalidates both `TripPlaces` and `MyPlaces`, so
  the row moves from the library section to `ในทริปนี้` by itself and the next tap takes the plain
  `addStop` path. Copy: `เพิ่มเป็นจุดแวะไม่สำเร็จ — ที่นี้อยู่ในทริปแล้ว แตะอีกครั้งเพื่อจัดลงวัน`,
  in the `addError` slot the picker already has. **No rollback, no compensating delete.**
- **R11.4** A row shows **name + category** on two lines, and a `place_id`-less place is **not
  marked**. #64's `ไม่มีเวลาเปิด-ปิด` chip **stays in Discover** — a picker row says *which place I
  mean*, not *whether to go*.
- **R11.5** Rows use the **SHORT** category label set — `เที่ยว / กิน / คาเฟ่ / ที่พัก / ช้อป /
  อื่น ๆ`, matching `PlaceBottomSheet`. The app's two label sets are **deliberately not** reconciled
  here (ADR-165 §4).
- **R11.6** A **client-side search box on the library section only**, with a result count.
  `ในทริปนี้` is the Trip's own pool and stays unfiltered.
- **R11.7** `+ เพิ่มสถานที่ใหม่` **stays at the top** of the panel, unchanged.
- **R11.8** **Three empty states, three copies:**

  | cause | copy |
  |---|---|
  | the library holds nothing this Trip lacks | `ที่ในคลังของคุณอยู่ในทริปนี้ครบแล้ว` |
  | the library is genuinely empty | `คุณยังไม่มีสถานที่ในคลัง` (today's string, correct only here); both sections vanish |
  | the search matched nothing | `ไม่มีที่ในคลังตรงกับ "<query>"` + a clear-the-search hint |

  The `ในทริปนี้` heading **stays rendered when that section is empty**
  (`ยังไม่มีที่ในทริปนี้`), otherwise the library list below reads as the Trip's own.
- **R11.9** The library row's only visual marker is a **hairline left accent**
  (`.add-stop-item.lib`).
- **R11.10** Two existing strings change **meaning** rather than wording: the
  `หรือเลือกจากคลังสถานที่` divider becomes true, and `คุณยังไม่มีสถานที่ในคลัง` narrows to the one
  case where it is accurate. Both are currently rendered over a list that comes from
  `useListTripPlacesQuery` — a promise the code has never kept.
- **R11.11** The picker is an **inline panel** in the itinerary column, replacing the
  `+ เพิ่มจุดแวะ` dashed button — **not a modal**, on either breakpoint. Desktop's left column is a
  fixed `464px`, so there is no second layout variant.
- **R11.12** New CSS is **exactly three things**: `.add-stop-divider.first` (the `ในทริปนี้` heading
  — the existing divider with its top margin dropped), `.add-stop-cat` (the second row line) and
  `.as-search` plus its count. Everything else is `trips-tokens.css` / `TripsPage.css` /
  `TripDetailPage.css` as they stand.

### 12. MCP — ADR-157

- **R12.1** `resolve_place` widens to **one discriminated input**: the parameter is renamed
  `url` → `input` and accepts a Google Maps URL (resolved as today), `"13.7563, 100.5018"`
  (verbatim passthrough, no Google call), or a Plus Code (offline decode).
  `ResolvePlaceValidator` must accept the two new shapes — it currently rejects everything that is
  not an allowed Google Maps host.
- **R12.2** **No capture tool is added.** `add_trip_place` remains the commit and the two-step
  survives; the two-step *is* the preview-and-confirm.
- **R12.3** `ResolvedPlaceDto` gains **`alreadySaved`** (the existing place this input resolves to,
  with the Trips it sits on — **library-level, not per-trip**, because at resolve time neither
  surface knows the target Trip yet) and **`nearMatches`** (up to 3 within 100 m, nearest first,
  non-blocking). Both come from a **server-side** scan of the caller's own library, and both fire
  for **all three** inputs. `ResolvePlaceHandler` gains `IApplicationDbContext`.
- **R12.4** `ResolvedPlaceDto` gains **`derivedFrom`**:

  | value | trustworthy? |
  |---|---|
  | `ExactPlaceId` | yes |
  | `NameSearch` | **no** — may be a different branch of a chain |
  | `CoordinateVerbatim` | exactly what the caller supplied |
  | `PlusCodeFull` | yes — a deterministic offline decode |
  | `PlusCodeShort` | **no** — may be far off |

  `resolve_place`'s tool description instructs the agent to **read the resolved name and address back
  to the user and get a reply before calling `add_trip_place`** whenever `derivedFrom` is not
  `ExactPlaceId`.
- **R12.5** **`list_my_places`** is added as a new `PlaceTools` type registered alongside the
  existing seven, returning the same grouped `DiscoverPlaceDto` the SPA reads, with the
  already-flattened `OriginTripPlaceId`.
- **R12.6** `add_trip_place` exposes **`originTripPlaceId`**, passed straight through.

### 13. Trip-less place rendering — ticket #64

- **R13.1** A neutral `ไม่มีเวลาเปิด-ปิด` chip renders on **both** the Discover list row and the
  detail sheet wherever hours are unknown — existing Google places included.
- **R13.2** The open-now filter still **keeps** those places.

---

## Out of scope

Carried verbatim from map #53's own boundary list. Each is revisitable as its own issue.

1. **Editing and deleting a place from Discover.** Place CRUD is already complete at both layers and
   the trip-crud-50 map closed it out explicitly. #48 asks only that a place can be **added**.
2. **Bulk capture** — several URLs or Plus Codes at once, a CSV import, a whole Google Maps saved
   list. A batch path is a different destination with its own partial-failure, progress and cost
   questions.
3. **Sharing captured places between users**, or importing another user's place.
4. **Fixing `GooglePlaceResolver`'s pre-existing accuracy weakness** (R6.3). It predates #48.
5. **Bounding the `list_my_places` payload** — a `search` or `near` parameter, or paging.
   `ListMyPlacesQuery` is parameterless and Discover scopes by viewport client-side.
6. **Sniffing a pasted URL while the `ค้นหาชื่อ` mode is active** and switching modes on the user's
   behalf. Kept as a possible later addition on top of the four pills, never as the only route.
7. **Unifying the app's two Thai category-label sets.** `PlaceBottomSheet` uses the short set,
   `AddPlacePreviewCard` the long one, for the same six `PlaceCategory` values. An app-wide copy
   change for a reason unrelated to #48.

---

## Verification requirements

Beyond the per-task tests in each plan:

- **The migration in Plan A must be applied to prod by hand** before anything depending on
  `OriginTripPlaceId` is deployed. Preview with `dotnet ef migrations script --idempotent` first.
- **Both mocks are fidelity gates.** Before merging Plan C or Plan D, fetch the card and diff the
  produced CSS and markup against it — tokens, colours, structural treatment.
- **Interactive verification is mandatory** for Plans C and D. Specifically unverifiable by any
  automated gate: the banner's footprint, the dimmed-pin state, the draft pin, the two picker
  dividers, the second row line, the search box and the left accent.
- **Smoke-test before pushing** — prod deploys on push to `main`.
