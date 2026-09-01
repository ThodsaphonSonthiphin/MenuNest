---
title: Is redo the same rule as undo, and may a member redo what the family head undid?
type: grilling
mode: HITL
status: closed
assignee: redo-symmetry-108
blocked_by: [canundo-consumers-audit]
gist: The head's undo STICKS. Undo is governed by who AUTHORED the change, redo by who UNDID it - both widening to the head. The flag splits into CanUndo + CanRedo, and RedoChangeHandler's own check moves to UndoneByUserId.
---

## Question

`BudgetChangeDto` carries one flag, `CanUndo`, and `ChangeHistorySheet` disables **both** the
ยกเลิก and the ทำซ้ำ button on it. So whatever this fix puts in that flag lands on redo too,
whether or not anyone chose it.

That matters because of a case that is **live today** and that the fix will leave as the one
cross-member control still enabled:

> The head undoes my change. The row's `UserId` is still mine, so it stays `canUndo: true`.
> I press ทำซ้ำ. `RedoChangeHandler`'s own check passes — it is my change. The head presses
> ยกเลิก again.

menunest-201 gave the head exactly one power and never said whether that power survives a
redo. Decide:

1. **Does the head's undo stick?** Either it does — a row the head undid is not redoable by
   its author, only by the head — or it does not, and undo/redo between two people is a
   contest the app declines to arbitrate. (A third reading: it sticks *once*, which nothing in
   the model can express today.)
2. **Does the flag split?** If undo and redo ever differ, `CanUndo` must become `CanUndo` +
   `CanRedo` with two reasons, and `latestRedoable` stops reading the undo flag. If they never
   differ, the flag stays single and the DTO comment must say so, because the next reader will
   otherwise assume it was an oversight.

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["redo-symmetry (this ticket)"]
    P0["canundo-consumers-audit"] --> ME
    ME --> C0["fix-and-verify"]
```
<!-- decision-map:graph:end -->

## What makes this worth a ticket rather than an implementation detail

It is not a new defect — an ordinary member can redo the head's undo in prod right now. The
fix does not create it. What the fix does is **remove everything around it**: once the other
enabled controls on foreign rows go dark, this becomes the single place where two members'
authority visibly collides, on a row the sheet has just finished labelling as somebody's.

Deciding it as a side effect of "which boolean goes on the DTO" is how it would get decided by
default.

## Evidence

- `BudgetChangeDto.cs` — one flag, `CanUndo`, one reason, `BlockedReason`; no `CanRedo`
- `ChangeHistorySheet.tsx:71` and `:79` — `disabled={!r.canUndo || busy}` on both buttons
- `latestUndoable.ts:17` — `latestRedoable` filters on `canUndo`
- `RedoChangeHandler.cs:34-43` — the same ownership-or-head check as undo, with its own
  message *"You can only redo your own changes."*
- `RedoChangeHandler` class comment — the near-duplication of `UndoChangeHandler` is
  deliberate, "the two are short, and the duplication reads more clearly than the abstraction"
- menunest-201 — the head has exactly one power plus handing the role over
- menunest-198 — undoing someone else's act "removes it from the current state, needs
  authority"; a redo restores it, which the ADR does not address
- `UndoChangeHandler.cs:52-75` — the author is push-notified when someone else undoes their
  work. There is no equivalent notice when someone redoes over the head, which the answer here
  may or may not want to change.

<!-- decision-map:resolution:start -->
## Resolution

The head's undo **sticks**. Undo is governed by who AUTHORED the change, redo by who UNDID
it — both widening to the head. The flag splits into `CanUndo` + `CanRedo`, and
`RedoChangeHandler`'s own check moves from `change.UserId` to `change.UndoneByUserId`.

Detail: `docs/adr/menunest-216-canundo-carries-both-rules-and-redo-belongs-to-whoever-undid-it.md`

```mermaid
flowchart TD
    RULE["You may reverse what YOU did.<br/>The head may reverse anyone's."]
    RULE --> U["UNDO governed by<br/>row.UserId - who authored it"]
    RULE --> R["REDO governed by<br/>row.UndoneByUserId - who undid it"]

    OLD["Issue's formula: BOTH on row.UserId"]
    OLD --> LOOP["head undoes my change<br/>-> row is still MINE<br/>-> I redo it<br/>-> head undoes it again"]
    LOOP --> DEAD["the head's one power lasts<br/>until the author presses ทำซ้ำ"]

    style RULE fill:#dcfce7,stroke:#16a34a
    style DEAD fill:#fee2e2,stroke:#dc2626
```

## The rule that came out of it is smaller than the question

The ticket asked two things and the answer collapses them into one sentence: **you may
reverse what you did, and the head may reverse anyone's.** Undo reverses an authoring, so it
reads `UserId`; redo reverses an undoing, so it reads `UndoneByUserId`. The head's widening
applies to both, unchanged.

`BudgetChange.UndoneByUserId` is already stored, already projected into the DTO as
`UndoneByDisplayName`, and already written by `MarkUndone`. So the whole cost is a field
comparison — no entity change, no migration.

## Both sub-questions, answered

**1. Does the head's undo stick? Yes.** A row the head undid is redoable by the head and by
nobody else. The contest reading — undo/redo between two people is something the app declines
to arbitrate — was rejected because menunest-201 gave the head exactly one power, and on the
author-governed formula that power expires the moment the author presses ทำซ้ำ. A power that
can be reversed by the person it was used on is not a power.

**2. Does the flag split? Yes, it has to.** `CanUndo` cannot express answer 1: after the head
undoes my change the row is mine (so undo-permission would say yes) but not mine to redo.
`BudgetChangeDto` gains `CanRedo`, and `latestRedoable` stops reading the undo flag —
`latestUndoable.ts:17` is the one-word change.

`BlockedReason` stays a single field: a row shows either ยกเลิก or ทำซ้ำ, never both, so one
sentence always has exactly one button to explain.

## This is a live defect, not only a DTO decision

An ordinary member can redo the head's undo in prod **today** — the fix does not create it.
What the fix does is strip away everything around it: once the other enabled controls on
foreign rows go dark, this becomes the single visible place where two members' authority
collides. Deciding it as a side effect of "which boolean goes on the DTO" is exactly how it
would have been decided by default.

## What this hands on

- `fix-and-verify` changes **`RedoChangeHandler`**, not only `ListChangesHandler`. Its check
  moves to `UndoneByUserId` and its message needs rewording — *"You can only redo your own
  changes"* stops being true of the rule it now enforces.
- The backend test owed is the loop that no longer closes: the head undoes the member's
  change, the member lists history and gets `canRedo:false` on it, and a redo call throws.
  `HeadUndoesAnyoneTests` seeds every actor this needs.
- Not decided here, and now on the map's fog: whether the author is told their redo was
  refused. `UndoChangeHandler` push-notifies the author when someone else undoes their work;
  there is no equivalent notice in the other direction.
<!-- decision-map:resolution:end -->
