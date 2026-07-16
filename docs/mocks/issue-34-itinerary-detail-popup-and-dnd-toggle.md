# Handoff — Issue #34: Itinerary stop card → compact card + detail popup + DnD toggle

**Date:** 2026-07-16
**Status:** Proposed (design confirmed via mockup)
**Issue:** [#34](https://github.com/ThodsaphonSonthiphin/MenuNest/issues/34) — "ดีเทลมันเยอะไป ทำ popup detail ดีกว่า และ ปุ่ม dnd ควรมี toggle เปิดปิดโหมด dnd กดเปิดแล้วค่อยแสดง"
**Relates to:** ADR-040 (visited presentation), ADR-043/044 (dnd-kit reorder + drag handle), ADR-046 (vertical-axis drag, legs hidden), ADR-052 (review icon/popover), ADR-028/029/032 (per-stop weather)
**Mockup:** `Itinerary Redesign.dc.html` (frames **1a** current, **1b** proposed — interactive: tap a card → detail popup; toggle "จัดลำดับ" → drag handles appear)

---

## Problem

The itinerary Stop card (`ItineraryStopCard.tsx`) is over-dense on mobile. Each card currently renders,
inline and all at once:

`check | rail(arr→dep) | body(name + dwell chip + weather-now chip + weather-arr chip + flag-note) | review icon | nav icon | drag-handle`

Two owner requests:

1. **Too much detail in the card.** Move the detail out into a **popup** — keep the card compact.
2. **Drag-and-drop is always on.** Add a **toggle to enter/leave reorder mode**; the drag handles
   only appear once the mode is on (`กดเปิดแล้วค่อยแสดง`).

A third refinement from review: **weather forecast is the primary weather signal, temperature is
secondary** — lead the card/popup weather with condition + rain %, show temp small and muted.

---

## Decision

### 1. Compact card + detail popup

The card collapses to: `rail(arr) | body(name + one-line summary) | trailing affordance`.

- **Summary line** (muted, one line): lead with the **arrival forecast** (`⛅ มีเมฆบางส่วน`,
  `🌧 ฝนตก 60%`), then dwell (`· อยู่ 1 ชม. 30 นาที`). If the stop has a timing flag, show a small
  severity dot + short label (`ปิดช่วงพัก`) instead of / in addition to weather.
- Removed from the card: the dwell chip, both weather chips, the full `flag-note` banner, the review
  icon column, the per-stop nav icon. **All of these move into the detail popup.**
- **Trailing affordance:** a chevron `›` in normal mode (signals "opens detail"); replaced by the
  **drag handle** in reorder mode (see §2).
- **Visited flag** (`ADR-040`): keep the left check column, or fold "มาแล้ว" into the popup as a
  primary action button — implementer's call; the mockup moves it into the popup to maximise card
  compactness.

**Detail popup = a bottom sheet** opened by tapping `.stop-body`. Contents:

- Header: place name + category chip (dot tinted from `catColor`) + `วัน N · จุดที่ i`
- Arrival → depart times (mono, large)
- Dwell
- **Weather, forecast-forward:** two panels `ตอนนี้` / `ไปถึง`, each = condition icon (large) +
  condition label (bold) + rain % (`ไปถึง` panel) with **temperature small and muted underneath**
- Timing flag note (`ADR-019` reason + fix), when present
- Actions: **นำทาง** (primary), **รีวิว**, **แก้ไข**, **ทำเครื่องหมายว่ามาแล้ว**

> Reuse over rebuild: `StopEditorDialog.tsx` already renders a body-portaled Syncfusion `Dialog`.
> Prefer adding a **read/detail mode** (or a sibling `StopDetailSheet`) rather than a brand-new
> popup mechanism. Tapping the card opens the **detail** view; the **แก้ไข** action inside it opens
> the existing editor form (today's `setStopEditor(stop.id)` behaviour).

### 2. Reorder-mode toggle

- Add a **"จัดลำดับ / เสร็จ"** toggle button in a toolbar row above the stop list in
  `ItineraryTab.tsx` (next to a "จุดแวะ · N จุด" label). Off = bordered teal pill; on = filled teal
  pill labelled "เสร็จ".
- New state `reorderMode: boolean` — local `useState` in `ItineraryTab`, or add to `tripsSlice`
  (persist per session if desired). Toggling **off** should also close any open detail popup.
- When **on**, show a hint banner: `โหมดจัดลำดับ — ลากที่จับด้านขวาเพื่อย้ายจุดแวะ`.
- Pass `reorderMode` down to `ItineraryStopCard`. The card renders `.stop-drag-handle` **only when
  `reorderMode` is true**; otherwise it renders the chevron and the whole `.stop-body` stays
  tap-to-open-detail.
- **DndContext / sensors / SortableContext stay exactly as they are** (`ADR-043`) — nothing about the
  drop logic changes. `useSortable` can remain mounted; only the visible activator (the handle) is
  gated. Simplest correct approach: keep `useSortable` always mounted, conditionally render the
  handle. If you want to skip sortable wiring entirely when off, guard `attributes/listeners` behind
  `reorderMode` — but keeping it mounted avoids remount churn.
- In reorder mode, suppress tap-to-open-detail on `.stop-body` (a drag shouldn't also fire a detail
  open); the mockup keeps cards non-interactive except the handle while reordering.

### 3. Weather forecast-forward

`ADR-029` already fetches two readings (now + on-arrival) and `ADR-032` gives an `iconBaseUri` SVG +
`description`. In `WeatherChip.tsx` / the new popup panels, make **`description` (condition) + rain %
primary and `tempC` secondary/muted**. No new data or endpoints needed.

### Rejected

- **Keep everything inline, just shrink fonts** — does not solve density; owner explicitly asked for a
  popup.
- **Always-on drag handles with a smaller grip** — owner explicitly wants an opt-in mode
  (`กดเปิดแล้วค่อยแสดง`) to reduce accidental drags and visual noise.
- **Temperature-primary weather** — owner: `ไม่ค่อยสนใจอุณหภูมิ เอาพยากรณ์อากาศมาแสดงหลัก`.

---

## Implementation checklist (frontend)

`frontend/src/pages/trips/`

- [ ] `components/ItineraryStopCard.tsx`
  - Reduce card body to name + one-line summary (arrival forecast → dwell / flag dot).
  - Remove inline dwell/weather chips, `FlagNote`, review column, nav column from the card.
  - Add `reorderMode: boolean` prop. Render chevron when off, `.stop-drag-handle` when on.
  - `onClick` on `.stop-body` → open detail popup (new callback prop, e.g. `onOpenDetail`).
- [ ] `components/StopDetailSheet.tsx` **(new)** — or a read-mode branch of `StopEditorDialog.tsx`.
  - Sections per §1. Weather forecast-forward. Actions: นำทาง / รีวิว / แก้ไข / มาแล้ว.
  - แก้ไข → existing `dispatch(setStopEditor(stop.id))`.
- [ ] `components/ItineraryTab.tsx`
  - `reorderMode` state + toolbar toggle button + hint banner.
  - Pass `reorderMode` to each `ItineraryStopCard`; wire `onOpenDetail` to the new sheet.
  - Toggling off closes the detail sheet.
- [ ] `components/WeatherChip.tsx` — condition + rain % primary, temp muted (reused inside the sheet).
- [ ] `tripsSlice.ts` *(optional)* — hold `reorderMode` / `detailStopId` if you prefer Redux over local state.
- [ ] Styles in `trips-tokens.css` / `TripDetailPage.css`:
  - New `.stop-card` compact variant (rail width ~52, single summary line, chevron column).
  - `.reorder-toggle` pill (off/on), `.reorder-hint` banner.
  - `.stop-detail-sheet` bottom-sheet (scrim + rounded top, grip bar) — or extend the
    `.stop-editor-dialog` scope for the read view.
  - Keep `.stop-drag-handle`, `.stop-list.dragging .travel-leg { visibility:hidden }` (ADR-046) as-is.

**Palette (existing tokens):** teal `#0e8f9e` / deep `#0b7a87` / soft `#e3f5f6`; ink `#0f172a`;
now `#0b7a87`/`#e3f5f6`, arr `#1863b7`/`#e9f1fb`; flag warn `#7a5310`/`#fff4e0`, bad `#b42318`/`#fdeceb`;
review `#c2255c`/`#fdeaf1`; visited `#15803d`/`#e7f6ec`. Fonts: Noto Sans Thai (UI), Spline Sans Mono (times).

**Tests to update:** `lib/reorder.test.ts` is drop-logic only — unaffected. Add coverage for the
reorder-mode gate (handle hidden when off) and that tapping a card opens the detail, not the editor.
E2E (`frontend/e2e/`) selectors: `data-testid="stop-drag-handle"` now only exists in reorder mode.

## Out of scope

No backend/API/MCP changes. Reorder persistence (`useReorderStopsMutation`) and the visited write
path are unchanged.
