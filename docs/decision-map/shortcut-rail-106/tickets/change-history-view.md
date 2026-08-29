---
title: Change history - what does the third slot show, and how far back?
type: grilling
mode: HITL
status: closed
assignee: change-history-view-1715
blocked_by: [history-storage]
gist: A sheet over /budget, not a route. Every row carries its own Undo and Redo, and an undone row stays on the list so it can be redone.
---

## Question

Shipping Change history in v1 (menunest-191) means building a screen that does not exist today. Decide what it lists and how far back it reaches. Concretely: does it show only acts the user can still undo, or every act including ones now beyond reach; does it cover only budget mutations or also transaction create/edit/delete; how far back does it go (a session, a day, the viewed month, forever); is each row itself actionable - tap to undo that specific act, or is it read-only with undo staying strictly last-in-first-out; and is it a full route like /budget/transactions or a sheet over the budget page. Note it is NOT the /budget/transactions list, which holds only Budget transactions - assigning, moving money and covering overspending appear in neither place today.

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["change-history-view (this ticket)"]
    P0["history-storage"] --> ME
    ME --> C0["build-ship"]
```
<!-- decision-map:graph:end -->

## Comment

## User input, 2026-08-29 — rows are individually actionable

Stated directly by the user while `undo-semantics` was being worked:

> "ควรมีให้กดเลือกได้ว่าจะรีดูหรืออันดูอันไหนเฉพาะในหน้า history"

So the Change history screen is **not** a read-only list, and undo is **not** strictly
last-in-first-out. Each row carries its own Undo / Redo, and the user picks the row.

This matches YNAB, which MenuNest copies deliberately: its iOS "Recent Moves" page lets
you swipe left to undo **any** recent move, not only the most recent one.

**Not yet decided, and this input creates it:** selective undo can produce a state a
strict stack never could. Undo entry #3 while #4 still stands, and #4 may depend on #3 -
assign B300 to an Envelope, then move B200 out of it, then undo only the assign, and the
Envelope lands at -B200.

MenuNest already renders an overspent Envelope as a first-class state (the budget page
carries an "Overspent" filter chip), so a negative result is displayable rather than
catastrophic - but whether to allow it, refuse it, or cascade the undo forward is an open
question this ticket owes.

Recorded as a comment rather than a resolution: this ticket is still blocked behind
`history-storage`, and nobody has claimed it.


## Comment

## User input, 2026-08-29 — an out-of-order undo may leave an Envelope negative, and that is allowed

Follow-up to the comment above. Selective undo can produce a state a strict stack never
could: assign B300 to an Envelope, move B200 out of it, then undo only the assign, and
the Envelope lands at -B200.

Put to the user with three options - allow it, refuse the row with an explanation, or
cascade the undo forward through everything after it.

Answer: **allow it.** The Envelope simply shows as overspent.

The reasoning offered and accepted: MenuNest already treats an overspent Envelope as an
ordinary, first-class state - the budget page carries an "Overspent" filter chip - so a
negative figure the user can see and fix themselves beats a row whose Undo button refuses
to work, or an undo that silently reverses acts the user did not select.

Still a comment, not a resolution: this ticket remains blocked behind `history-storage`
and unclaimed. Whoever claims it inherits these two answers.

<!-- decision-map:resolution:start -->
## Resolution

A sheet over /budget, not a route. Every row carries its own Undo and Redo, and an undone row stays on the list so it can be redone.

Detail: docs/adr/menunest-195-change-history-is-a-sheet-with-per-row-undo-and-redo.md

```mermaid
flowchart TD
    RAIL["Shortcut rail, slot 3"] --> SHEET["SHEET over /budget<br/>on the existing budget-modal scaffolding"]

    SHEET --> ROWS["One row per recorded act"]
    ROWS --> R1["Row carries its OWN Undo"]
    ROWS --> R2["Row carries its OWN Redo"]
    R1 --> STAY["An undone row STAYS, marked -<br/>otherwise per-row redo has nowhere to live"]

    R1 --> NEG["Out-of-order undo may leave<br/>an Envelope NEGATIVE - allowed.<br/>Overspent is already a first-class state"]

    SHEET --> COST["COST: the sheet covers the numbers.<br/>You do not see RTA move until you close it"]

    ROWS -.window from menunest-194.-> WIN["min(7 days, since the 1st)"]
    ROWS -.NOT decided here.-> OPEN["which acts appear -> reversible-actions<br/>whose acts, and naming them -> whose-acts"]

    style SHEET fill:#dcfce7,stroke:#16a34a
    style STAY fill:#dcfce7,stroke:#16a34a
    style COST fill:#fee2e2,stroke:#dc2626
    style OPEN fill:#fef3c7,stroke:#d97706
```

Recorded in **menunest-195**, which holds the reasoning and the rejected options.
This ticket records only what the answer changes.

## Three of this ticket's five questions were already answered elsewhere

They were not re-asked:

| the ticket asked | answered by |
|---|---|
| how far back the list reaches | **menunest-194** — min(7 days, since the 1st of the month) |
| whether each row is actionable, or undo stays last-in-first-out | **the user, directly**, recorded as a comment on this ticket |
| whether it covers transactions as well as budget mutations | **`reversible-actions`** — a different ticket, deliberately left there |

So the only open question this session was the surface: sheet or route.

## What decided the surface

The rail exists for fast correction without losing your place. Opening a whole route from a
floating button is a heavy gesture for a light action, and menunest-194 already bounds the
list to a few days, so it does not need a page. The project has both patterns live in this
same feature — `/budget/transactions` is a route, `EverydayMarksSheet` is a sheet on the
`budget-modal-overlay` / `budget-modal` scaffolding — so the sheet reuses what exists.

## Confirming exchange

Two answers carried in from earlier in the session, both quoted on this ticket's comments:

- **"ควรมีให้กดเลือกได้ว่าจะรีดูหรืออันดูอันไหนเฉพาะในหน้า history"** — rows are individually
  actionable, both ways.
- Allowing an out-of-order undo to leave an Envelope negative, chosen over refusing the row
  and over cascading the undo forward.

And one this session:

- The surface — **"แผ่นซ้อนทับ"**, after the occlusion cost was named.

## One inference, flagged as such

**An undone row stays on the list, marked, carrying a Redo.** Nobody stated this; it follows
from per-row redo having nowhere to live if undone rows vanish. Recorded as an inference so a
later session can overturn it cheaply rather than treating it as a settled answer.

## What this leaves for other tickets

- `reversible-actions` — which acts appear as rows at all.
- `whose-acts` — whether other Family members' acts appear, and whether a row names who did it.
- `build-ship` — the sheet covers the numbers, so if that occlusion bites in real use, the
  escape named in menunest-195 is a partial bottom sheet, which the project would have to
  build from scratch.

<!-- decision-map:resolution:end -->
