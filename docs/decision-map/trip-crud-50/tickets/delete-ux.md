---
title: Delete a trip - affordance, confirmation, and where the user lands afterwards
type: grilling
mode: HITL
status: closed
assignee: me
blocked_by: [existing-edit-patterns]
gist: Delete is a footer action of EditTripDialog - matching both existing trips-delete affordances, which also live in a dialog footer - so TripsPage is never touched and its card never needs unwrapping. Confirmed via the shared useConfirm (destructive:true) naming three things: the trip by name, 'N วัน · M จุดแวะ' for identity (free, since ADR-139 already requires the itinerary cached), and the Discover consequence - deleting a trip also removes its places from ไปไหนดี, because deleteTrip invalidates MyPlaces and ListMyPlacesHandler filters DeletedAt == null. The copy must NOT claim the stops are deleted: DeleteTripHandler is a pure soft delete and every day, stop, place and checklist entry survives. On success navigate to /trips with no toast (there is no shared toast system, and the trip's absence is the feedback); staying put would hit TripDetailPage's existing not-found guard, which reads as an error. The button reads as destructive, deliberately breaking the muted .se-delete precedent, because unlike removing a stop or place this is not recoverable.
---

## Question

Where does deleting a trip live, how is it confirmed, and where does the user land afterwards? Deletion is final from the user's point of view - no undo toast and no trash bin - so the confirmation is the only safety net. Decide the affordance and its placement, what the confirmation says (whether it names the trip, its day count, or the number of stops that disappear with it), and the post-delete destination and feedback. The API and the RTK hook already exist and are entirely unused: useDeleteTripMutation has zero call sites in the SPA.

<!-- decision-map:resolution:start -->
## Resolution

Delete is a footer action of EditTripDialog - matching both existing trips-delete affordances, which also live in a dialog footer - so TripsPage is never touched and its card never needs unwrapping. Confirmed via the shared useConfirm (destructive:true) naming three things: the trip by name, 'N วัน · M จุดแวะ' for identity (free, since ADR-139 already requires the itinerary cached), and the Discover consequence - deleting a trip also removes its places from ไปไหนดี, because deleteTrip invalidates MyPlaces and ListMyPlacesHandler filters DeletedAt == null. The copy must NOT claim the stops are deleted: DeleteTripHandler is a pure soft delete and every day, stop, place and checklist entry survives. On success navigate to /trips with no toast (there is no shared toast system, and the trip's absence is the feedback); staying put would hit TripDetailPage's existing not-found guard, which reads as an error. The button reads as destructive, deliberately breaking the muted .se-delete precedent, because unlike removing a stop or place this is not recoverable.

Detail: docs/adr/143-trip-delete-lives-in-the-edit-dialog-footer-and-lands-on-trips.md

Resolved HITL via `grill-with-docs` on 2026-08-01. Four questions, each answered by the user.

## Where the decision actually lives

- **ADR-143** — [`docs/adr/143-trip-delete-lives-in-the-edit-dialog-footer-and-lands-on-trips.md`](../../adr/143-trip-delete-lives-in-the-edit-dialog-footer-and-lands-on-trips.md)

## The four answers, as given

| # | Question | User's answer |
|---|---|---|
| 1 | Where does the delete affordance live? | **"footer ของ EditTripDialog"** |
| 2 | What does the confirmation say? | **"ชื่อ + ตัวเลข + บรรทัด Discover"** |
| 3 | Where does the user land, and see what? | **"ไป /trips ไม่มี toast"** |
| 4 | Muted or destructive styling? | **"ให้อ่านว่าอันตราย"** |

## Verified during the grill, not assumed

- `DeleteTripHandler` is `trip.SoftDelete()` and **nothing else** — no days, stops, places or
  checklist entries are removed. So the confirmation must not claim the stops are deleted; they
  are not. It says they disappear from the app, which is true and is what the user cares about.
- `deleteTrip` invalidates `MyPlaces` (`api.ts:1373`) and `ListMyPlacesHandler` filters
  `t.DeletedAt == null` — so **deleting a trip removes its places from Discover (ไปไหนดี)**.
  Unguessable from the words "ลบทริป", hence its own line in the confirm.
- `TripDetailPage:92-99` **already** renders "ไม่พบทริปนี้ — อาจถูกลบ หรือลิงก์ไม่ถูกต้อง" for a
  deleted trip. Correct for a stale deep link, but it means staying put after a delete shows what
  reads as an error — which is what forces the navigation to `/trips`.
- **There is no shared toast system.** `shared/components/` holds only AppLayout,
  ConfirmProvider, FamilyRequiredRoute, HomeRedirect, NavBar, ProtectedRoute. The repo's only
  toast is `pages/budget/components/TransactionUndoToast.tsx` — feature-local *and* an undo
  toast, which this map ruled out of scope. A post-delete toast would be new shared
  infrastructure, so it was rejected rather than assumed free.

## A correction this ticket forced on an already-committed ADR

ADR-138's rejection of the hard-block option claimed *"No other MenuNest surface blocks like
this."* **That was false.** `DeleteTripPlaceHandler.cs:26-27` blocks in exactly the same shape —
*"ลบไม่ได้ — สถานที่นี้ถูกจัดลงตารางแล้ว ลบจุดในแผนก่อน"* — and ADR-133 does the same for
daily-enable. ADR-138 has been corrected in this session's commit: the rejection now rests on
**cost** (six unconfirmed destructive taps to clear a shrink's way) rather than on a novelty
claim that was simply wrong. **The Shrink decision itself is unchanged** — the cost argument was
always the load-bearing one.

## What this closes

`#50`'s destination is now fully covered by decisions: every create-dialog field is editable
(ADR-141), the destructive shrink is guarded (ADR-138/139/140), and a trip can be deleted
(ADR-143) without either path silently destroying stops.

Notably, **#50 now changes nothing on `TripsPage` at all** — ADR-141 put edit in the detail
header, ADR-143 put delete in the dialog footer, so the trips card is never unwrapped.

## Risks carried forward, unverified in a browser

1. **Flash of the not-found guard** between the `TripDetail` invalidation and the navigation to
   `/trips`. Would look like an error on a successful delete. No rendering test can catch it.
2. `useConfirm`'s Dialog was never verified to render above `.itin-reorder-overlay`
   (`z-index: 1200`) — carried over from `shrink-data-loss` and now applies to two confirms.

<!-- decision-map:resolution:end -->
