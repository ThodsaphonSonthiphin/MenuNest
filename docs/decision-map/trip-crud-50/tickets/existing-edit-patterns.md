---
title: Existing patterns - which edit, commit and destructive-confirm conventions should a trip-edit surface inherit?
type: research
mode: AFK
status: closed
assignee: 
blocked_by: []
gist: A shared useConfirm() destructive modal already exists and is mounted app-wide via AppLayout - but the entire Trips feature uses ZERO confirmations, every destructive trips action fires on first tap. ADR-013 mandates commit-on-change and its stated rationale (a mis-pick is cheap to re-pick) does not transfer to a field that destroys stops; ADR-085's own tie-breaker (single field -> autosave) fails for a multi-field form; ADR-137 forbids putting IsDaily on the full-replace UpdateTrip. The trip card is a single button so a secondary action needs it unwrapped, and dialogs are portaled so page-scoped trips tokens do not resolve - the two existing dialog families are teal and orange and do not match.
---

## Question

What edit, commit and destructive-confirm patterns does the MenuNest SPA already establish, and which of them should a trip-edit surface inherit? Cover: the in-place commit-on-change editors (TripDateEditor, DayStartEditor, DailyToggle) and the ADRs behind them; the save/cancel dialogs (CreateTripDialog, StopEditorDialog, PlaceEditorDialog, PlaceLinkFallbackDialog); every existing confirmation before a destructive action anywhere in the app, or the fact that none exists; the Syncfusion components and trips-tokens.css tokens each of them uses; and how a trip card on TripsPage is currently structured, given the whole card is a single tap target that navigates.

<!-- decision-map:resolution:start -->
## Resolution

A shared useConfirm() destructive modal already exists and is mounted app-wide via AppLayout - but the entire Trips feature uses ZERO confirmations, every destructive trips action fires on first tap. ADR-013 mandates commit-on-change and its stated rationale (a mis-pick is cheap to re-pick) does not transfer to a field that destroys stops; ADR-085's own tie-breaker (single field -> autosave) fails for a multi-field form; ADR-137 forbids putting IsDaily on the full-replace UpdateTrip. The trip card is a single button so a secondary action needs it unwrapped, and dialogs are portaled so page-scoped trips tokens do not resolve - the two existing dialog families are teal and orange and do not match.

Resolved AFK by a research subagent reading the repo at `c:\Repo2\t\menunest` (read-only, source only — nothing was verified in a running browser). Every claim is cited to a file and line.

## In-place commit-on-change editors

### TripDateEditor — `frontend/src/pages/trips/components/TripDateEditor.tsx`

- **Commit:** immediate on pick, no confirm (`:70-90`). No-op guard at `:72`
- **Optimistic:** local state, not RTK cache patching (`:73` set, `:86` revert). Re-sync effect at `:64-66`
- **Unmount guard:** a `mounted` ref set *in the effect body* so StrictMode's double-mount restores it (`:54-60`)
- **Error surface:** an `onError` callback **up to the parent** (`:38`) — not a toast, not inline. Rendered by `TripDetailPage.tsx:116` (desktop) and `:200` (mobile)
- **Loading:** none. The only disable is the `locked` daily prop (`:109`)
- **Syncfusion:** `DatePicker` from `@syncfusion/react-calendars`, `editable={false} openOnFocus clearButton={false}` (`:101-110`)
- **Critical:** it sends the **whole trip** on every date pick (`:75-82`), carrying `dayCount` through unchanged deliberately — `:24-26` *"Only the start date changes here … so no itinerary days are dropped (shrinking is out of scope)."*

### DayStartEditor — `components/DayStartEditor.tsx`

Same shape (optimistic `:66`, revert `:71`, `mounted` ref `:49-55`, re-sync `:59-61`, `onError` `:37`). **Three immediate commit triggers:** `onChange` (`:77`), a "ตอนนี้" now-button (`:78`), and a persisted checkbox (`:80-88`) which has no optimistic state. `TimePicker` with `format="HH:mm" step={15}` (`:93-103`). Plain `<button>` and plain `<input type="checkbox">` escapes at `:105-122`. Parent must pass `key={dayId}`.

### DailyToggle — `components/DailyToggle.tsx`

- **Commit:** immediate (`:25`), **no optimism at all** — the UI moves only after the invalidation refetch
- **Loading:** the only one of the three that disables in flight (`:13`, `:39`)
- **Blocked-state idiom worth stealing:** an unsatisfiable guard renders **clickable, not `disabled`**, so touch users get the reason — `:9-10`, `:21-23`, `:34`, `:36 aria-disabled`
- **Markup:** a hand-rolled `<button role="switch" aria-checked>` with track/knob spans (`:32-46`), **not** a Syncfusion Switch
- It calls `setTripDaily`, **never** `updateTrip` — ADR-137

### Two more commit-on-change precedents outside trips

- **Visited toggle** (`ItineraryTab.tsx:152,475,516`) — RTK **cache-patch** optimistic write with no invalidation (ADR-042); the only cache-patching code in the repo
- **Settings home-page dropdown** (`pages/settings/*.tsx:101-109,230`) — autosave on select with an inline `บันทึกแล้ว`; the repo's one autosave-with-confirmation precedent (ADR-085 §4)

## Save/cancel dialogs

All four use `Dialog` from `@syncfusion/react-popups` with `open onClose modal`, conditionally mounted by the parent (`{open && <Dialog…>}`), never kept mounted with `open={false}`.

**CreateTripDialog** — the only trips dialog using react-hook-form (`:71`, `Controller` per field, e.g. `:144-147`). Explicit submit (`:97-112`), server error into local state (`:110`, rendered `:308`), `isLoading` disables with the label swapping to `'…'` (`:314-321`). Actions are **hand-rolled buttons**, and Cancel is bare `onClick={onClose}` with **no dirty-state warning** (`:310-323`). Syncfusion inside: `TextBox` for name/destination, `DatePicker` for start date; the day-count stepper (`:244-264`), travel-mode tiles (`:289-303`) and daily switch (`:190-203`) are all hand-rolled. Field set is exactly the destination's list (`:22-29`), `MIN_DAYS=1`, `MAX_DAYS=60`. **Reusable:** the live derived end-date summary (`:89-95`, rendered `:271-280`) and the `effectiveDayCount = isDaily ? 1 : dayCount` coercion (`:88`, rationale `:85-87` — the summary must reflect the coerced value "or it misrepresents the trip about to be created (#49)").

**StopEditorDialog** — explicit `บันทึก` (`:219-226`), staged local state (`:50-57`), client validation before the write (`:82-85`), and **dirty-diffing to avoid a second write** (`:89-93`). Error is local state rendered inside the dialog (`:200`); the dialog stays open on error and `onClose()` only on success (`:107`). Syncfusion: only `DropDownList` for travel mode (`:167-173`). Note `MODES` labels still carry emoji (`:26-30`), pre-existing and against `frontend-guidelines.md:104-148`. Nested child editors (`BestTimeEditor`, `PlaceSeasonEditor`, `DwellStepper`) commit into parent state, not to the server; `ChecklistSection` is the exception and writes straight through.

**PlaceEditorDialog** — same skeleton and same CSS class as StopEditorDialog. Adds a third action, "ดันขึ้น master" (`:78-94`), whose success is a **transient inline badge** (`:144`) reset by any child `onChange` (`:129-132`) — the nearest thing in the repo to an in-dialog "saved" confirmation. Delete is labelled `เอาออกจากทริปนี้` (`:141`), not `ลบ` — deliberate, ADR-065.

**PlaceLinkFallbackDialog** — 53 lines, no save/cancel pair: one action that resolves and immediately closes (`:24-26`). The only dialog surfacing the error straight off the RTK hook rather than local state (`:20`, `:48`). The only trips dialog using the Syncfusion `Button` (`:41-46`).

## Destructive-action confirmations that exist today

**This is the loudest finding: confirmations DO exist app-wide, but the entire Trips feature has ZERO of them. Every destructive action in trips fires on the first tap.**

| Trips action | File:line | Confirms? |
|---|---|---|
| Remove a stop (`ลบจุดนี้`) | `StopEditorDialog.tsx:113-121`, button `:203-218` | **No** |
| Remove a place (`เอาออกจากทริปนี้`) | `PlaceEditorDialog.tsx:68-76`, button `:139-142` | **No** |
| Detach a checklist item (`✕`) | `ChecklistSection.tsx:53-57`, button `:72-74` | **No** |
| Delete a best-time window | `BestTimeEditor.tsx:43` | **No** (local until Save) |
| Delete a season period | `PlaceSeasonEditor.tsx:60` | **No** (local until Save) |
| **Delete a trip** | — | **The UI does not exist.** Endpoint `api.ts:1371-1373`, hook exported `:1678`, **not one call site in `frontend/src`** |

The two live trips deletes are also styled *not* to read as destructive — `TripDetailPage.css:535-550`: `.se-delete { color: var(--se-ink-soft) }` with only a hover tint, sitting at the far left of `.se-foot` opposite a large orange `.se-save`.

### The rest of the app — five idioms

- **(a) The shared `useConfirm()` promise-modal — 4 features, 8 call sites**, only in `meal-plan`, `recipes`, `shopping`, `stock`. E.g. `RecipeDetailPage.tsx:174-181`, `StockPage.tsx:106-116`, `useMealSlotDetail.tsx:91-100`, `useShoppingListDetail.tsx:36-44/52-63/72-79/89-95`. Established copy convention: `title: 'ลบ <thing>'`, `message` naming the item in `<strong>"…"</strong>`, `confirmText: 'ลบ'`, `destructive: true`, caller returns early on `!ok`
- **(b) Hand-rolled `Dialog`-as-confirm** — `FamilyPage.tsx:561-596` and `:598-625`. Note the leave button hardcodes `background: var(--color-danger)` inline (`:623-626`) instead of using `Color.Error`. Deleting a family relationship (`useFamilyPage.ts:46-53`) has **no** confirm
- **(c) Hand-rolled backdrop+modal — health.** `EpisodeDetailPage.tsx:312-345` ("จะลบทั้ง intakes และ follow-ups … ไม่สามารถกู้คืนได้"), `ShareLinksPage.tsx:303-338`, `DrugMasterPage.tsx:84-89`. These keep a `กำลังลบ...` in-flight label
- **(d) Syncfusion Grid's built-in confirm** — `IngredientsPage.tsx:50 confirmOnDelete: true`, `ShoppingListDetailPage.tsx:239`. Irrelevant to a non-Grid surface
- **(e) Undo-toast instead of confirm — budget.** `AccountDetailPage.tsx` deletes optimistically with a 5-second undo window (`:35`, `:62-69`, `:111-116`, `:219-224`), plus an unmount effect (`:78-91`) that commits the pending delete so a Back-navigation cannot leave the row alive. Single-pending policy documented at `:99-101`. **This is the one "destructive without a confirm gate" pattern in the app that is actually defensible, and it is a live alternative to a confirm dialog for trip delete**
- **(f) No `window.confirm` anywhere** — `ConfirmProvider.tsx:6-8` says it *"Replaces window.confirm with a Syncfusion-styled modal"*

## Confirmation primitives available

**A shared one exists and is already mounted app-wide — a trip-edit surface should not build anything.**

`frontend/src/shared/components/ConfirmProvider.tsx`, options at `:11-18`:

```ts
export interface ConfirmOptions {
  title?: string; message: ReactNode; confirmText?: string; cancelText?: string
  /** Renders the primary button in red — use for deletes / destructive actions. */
  destructive?: boolean
}
```

Defaults (`:57-63`): `title = 'ยืนยันการทำรายการ'`, `confirmText = 'ยืนยัน'`, `cancelText = 'ยกเลิก'`, `destructive = false`. Renders a `Dialog` `modal width 420px` (`:68-74`) plus two `Button`s (`:86-101`) — cancel `Variant.Outlined/Color.Secondary`, primary `Variant.Filled` with `color={destructive ? Color.Error : Color.Primary}`. Dismissal resolves **false** (`:70`). Hook at `shared/hooks/useConfirm.ts:9-15`. **Mounted for every routed page including trips** — `AppLayout.tsx:19-26` wraps the `<Outlet/>` in `<ConfirmProvider>`.

Limitations to design around:
1. **No `isLoading` / pending state.** `settle(true)` closes the modal immediately (`:49-53`); the caller then awaits the mutation with the modal already gone. If a trip delete needs a "กำลังลบ…" state, either extend the provider or use the health-style hand-rolled modal
2. **Single-slot resolver** (`:40`) — a second `confirm()` while one is open orphans the first promise
3. **The modal body is inline-styled, not tokenised** (`:75`), and it is portaled to `document.body`, so it will not see any `.trips-page` / `.trip-detail`-scoped token

## The TripsPage trip card

Both cards are a **single `<button>`** — `TripsPage.tsx:22-33` (daily) and `:35-48` (regular). No card-level menu, no hover-revealed actions, **nothing focusable inside the card today**. `:46` renders `{t.startDate}` **raw**, with no Thai/BE formatting unlike `CreateTripDialog.thaiDate` (`:59-61`) or `TripDateEditor.fmt` (`:12-16`). CSS at `TripsPage.css:80-104`, grid at `:74-78`.

**Adding a secondary action.** The card is a `<button>`, so a nested `<button>` inside it is **invalid HTML** — browsers will not render it as a descendant and React will not warn. Three viable routes:

1. **Unwrap the button** — make `.trip-card` a `<div>`/`<li>` with a full-bleed stretched-link button behind the content (`position:absolute; inset:0`) and the secondary action above it at a higher `z-index`. Preserves the whole-card tap target, keeps both controls real buttons; requires reworking `:hover`/`:active` and adding `position: relative`
2. **Keep the button, put the action outside it** — smallest diff, but the action overlaps content the card owns and the `translateY(-2px)` hover desyncs the two
3. **Move the action off the card entirely**, into `TripDetailPage`'s header next to `DailyToggle` (`:111-113` desktop, `:195-197` mobile). Needs **no** card change at all, and both existing trips-delete affordances already live in a detail dialog footer rather than a list row, so it matches precedent

Whichever route: `data-testid="trip-card"` is referenced by the Playwright e2e config — keep it on whatever element ends up being the navigation target.

## Design tokens and Syncfusion inventory

**The portal gotcha, answered directly: the bulk of the trips tokens are PAGE-SCOPED, not `:root`.**

- `trips-tokens.css:11-19` defines on **`:root`** only the review and UV tokens, and says why: *"Global fallback so the review-link popover (portaled to document.body, outside .trips-page/.trip-detail scope) still resolves --review/--review-bg."*
- `trips-tokens.css:21-47` defines on **`.trips-page, .trip-detail`** everything else: `--teal`, `--teal-deep`, `--teal-soft`, `--ink`, `--page`, `--surface`, `--border`, `--muted`, `--warn(-bg,-ink)`, `--bad(-bg)`, `--now(-bg)`, `--arr(-bg,-rain,-rain-bg)`, `--visited(-bg)`
- A legacy `--trp-*` set is defined **twice**, page-scoped both times: `TripsPage.css:6-31` and `TripDetailPage.css:7-19`

**Consequence:** both existing trips dialogs redeclare their entire palette locally. `TripsPage.css:168-193` spells it out — *"The Syncfusion Dialog renders in a body-level PORTAL, so it sits OUTSIDE the `.trips-page` scope and cannot see the `--trp-*` / `--teal` tokens."* And the two dialog families **do not match each other**: `.create-trip-dialog` is teal (`TripsPage.css:177-185`), `.stop-editor-dialog` is warm orange (`TripDetailPage.css:251-273`, `--se-accent #ef6d2d`). **A trip-edit dialog has to pick a side.**

Reusable without redeclaring: `.trips-field-error` (defined twice with different font sizes — `TripsPage.css:66-71` and `:154-159`), `.trips-muted`, `.trips-empty`. Font convention: `'Spline Sans Mono'` on numeric/time elements (`trips-tokens.css:49-57`).

**Syncfusion actually installed** (`frontend/package.json:28-40`): `react-buttons`, `react-calendars` (pinned 33.1.44), `react-data`, `react-dropdowns`, `react-grid`, `react-inputs`, `react-navigations` (pinned), `react-popups`, `react-scheduler`, plus some legacy ej2. **`@syncfusion/react-icons` is NOT installed** — every trips icon is a hand-rolled inline SVG. And **no Syncfusion `Switch`, `NumericTextBox` or `RadioButton` is used anywhere in trips** — every toggle, stepper and radio group is hand-rolled. Verify against the `.d.ts` before assuming a component exists (`frontend-guidelines.md:13-17`).

**Mocks:** 17 trip-related HTML mocks plus previews in `docs/mocks/`. **There is no trip-edit mock, no trip-delete mock, and no confirm-dialog mock anywhere** — a grep for ลบทริป / แก้ไขทริป / edit trip / delete trip returns nothing.

## ADRs that constrain this

- **ADR-012** — the value *is* the affordance; tap it to edit, no separate edit icon. Names the exact props. Flags the cost: *"A plain value is a weak affordance … so the mockup must give it a visible 'editable' treatment"*
- **ADR-013** — **the central precedent.** *"Commit on change… with no intermediate confirm control."* Explicitly rejects hold-local-value-plus-save. Accepts the downside: *"A mis-pick persists without a guard; recovery is to re-pick (cheap, since the value stays editable)."* **That reasoning does not transfer to a field whose mis-pick destroys stops**
- **ADR-137** — **the hardest constraint.** `UpdateTripCommand` is a **full-replace PUT** and *"is left untouched and never reads/writes `IsDaily`, so a start-date/name/day-count edit can never clear the flag."* Rejects threading `IsDaily` onto `UpdateTrip`. Also flags that `TripDto`/`CreateTripCommand`/`UpdateTripCommand` are **positional records** — adding a field shifts every construction site
- **ADR-133** — states the silent-data-loss fact outright, and mandates *"The frontend disables the day-count stepper / multi-day end-date editor for daily trips and surfaces the reject as a user-facing error, never a raw 500"*
- **ADR-042** — every other trip mutation invalidates `TripItinerary`, and that is correct for anything schedule-changing (`updateTrip` already does — `api.ts:1365`)
- **ADR-065** — the naming discipline for destructive labels: *"labelled 'เอาออกจากทริปนี้' (not 'ลบสถานที่') to reflect that only this trip's copy goes"*
- **ADR-085 §4** — *"single field -> autosave, no separate Save button."* **A multi-field trip-edit form fails that tie-breaker**
- **ADR-009** — cited by `UpdateTripHandler.cs:41` as the source of "add/remove trailing days"

**The inverted risk ordering worth carrying into `shrink-data-loss` and `delete-ux`:** shrinking `dayCount` **hard-deletes** stops with no server guard and no undo, while deleting the trip itself is a **soft** delete (`DeleteTripHandler.cs:20`). The *edit* path is the destructive one; the *delete* path is recoverable in the DB.

## Uncertainties / could not determine

- **Whether `ConfirmProvider`'s Dialog renders above the trips z-index stack.** `trips-tokens.css:774` puts `.itin-reorder-overlay` at `z-index: 1200` and `.capture-overlay` at `1100`; the provider's Dialog uses Syncfusion's default, which was not read. Verify interactively before assuming a confirm appears above a capture overlay
- **Whether `ConfirmOptions.destructive` actually renders red in this app's theme.** It maps to `Color.Error`, but the app's Syncfusion theme CSS was not read. `FamilyPage.tsx:623-626` sidesteps `Color.Error` with an inline `var(--color-danger)`, which is weak evidence it may not have looked right
- **Nothing was verified in a running browser** — every CSS and portal claim above is read from source only

<!-- decision-map:resolution:end -->
