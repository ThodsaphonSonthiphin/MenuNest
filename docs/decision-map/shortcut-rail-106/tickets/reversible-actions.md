---
title: Reversible actions - which budget mutations join the undo stack, and which deliberately do not?
type: grilling
mode: HITL
status: closed
assignee: reversible-actions-1730
blocked_by: [undo-semantics]
gist: Undo covers five money-placement acts - assign, move, cover, quick-assign, everyday marks - and nothing else. Excluding transactions retires the hard-delete problem entirely.
---

## Question

Of the budget mutations that exist - set assigned amount, move money, cover overspending, quick-assign fill-targets, quick-assign equally, transaction create, transaction edit, transaction delete, account create/edit/delete - which ones does Undo cover? Name each one in or out, with the reason. Bulk actions matter most: quick-assign touches many envelopes in one press, so decide whether undoing it is one stack entry or many, and whether a destructive action like account delete should be undoable at all or should keep a confirm dialog instead.

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["reversible-actions (this ticket)"]
    P0["undo-semantics"] --> ME
```
<!-- decision-map:graph:end -->

<!-- decision-map:resolution:start -->
## Resolution

Undo covers five money-placement acts - assign, move, cover, quick-assign, everyday marks - and nothing else. Excluding transactions retires the hard-delete problem entirely.

Detail: docs/adr/menunest-196-undo-covers-money-placement-not-transactions-or-structure.md

```mermaid
flowchart TD
    UNDO["Undo covers 5 act types<br/>- all of them money moving between pots"]
    UNDO --> I1["set assigned amount"]
    UNDO --> I2["move money"]
    UNDO --> I3["cover overspending"]
    UNDO --> I4["quick-assign<br/>ONE row, reverses every envelope"]
    UNDO --> I5["everyday marks<br/>+ added beyond YNAB"]

    OUT["OUT - and each has a better guard already"]
    OUT --> O1["transaction create/edit/delete<br/>-> the row's own Edit button"]
    OUT --> O2["balance correction<br/>-> it IS a Budget transaction"]
    OUT --> O3["account / Envelope / group CRUD<br/>-> a confirm dialog BEFORE the act"]

    UNDO ==> KILL["RETIRES the map's most expensive fact:<br/>no soft-delete flag, no migration,<br/>no filter on every transaction query"]

    BUG["Pre-existing, NOT caused by undo:<br/>quick-assign already commits N sequential<br/>requests with no atomicity. 7 of 12 fails today<br/>= half-assigned, silently"]

    style UNDO fill:#dcfce7,stroke:#16a34a
    style KILL fill:#dcfce7,stroke:#16a34a
    style I5 fill:#fef3c7,stroke:#d97706
    style BUG fill:#fee2e2,stroke:#dc2626
```

Recorded in **menunest-196**, which holds the reasoning and the rejected options.
This ticket records only what the answer changes.

## The biggest thing this ticket did was remove work

The map has carried this note since `undo-semantics`: deleting a **Budget transaction** is a
hard delete (`_db.BudgetTransactions.Remove(tx)`), with no soft-delete flag, unlike `Trip`,
`Drug`, `Photo` and `WritingEntry`. It was described as *the single most expensive fact on the
map*.

Putting transactions out of scope **retires it**. No soft-delete column, no migration, no
filter on every transaction query — and the thing it would have bought is a second way to fix
a mistyped transaction, next to the Edit button that already sits on the row.

## What decided each line

| act | in / out | why |
|---|---|---|
| set assigned amount, move money, cover overspending | **in** | money between pots; the inverse is symmetric by construction |
| quick-assign | **in**, as ONE row | one press should be one undo |
| everyday marks | **in** | departs from YNAB deliberately: it is a Budgeting event, so a stray toggle silently moves the Daily allowance, and its inverse is one boolean |
| transaction create / edit / delete | **out** | the row already has Edit; and delete is the hard-delete problem above |
| balance correction | **out** | it IS a Budget transaction (CONTEXT.md), so this falls out of the rule rather than being a separate call |
| account / Envelope / group CRUD | **out** | structural and destructive - the right guard is a confirm before, not an undo after |

## A defect found while deciding, which is NOT this feature's

`QuickAssignDialog.tsx:122` commits the plan as `for (const a of plan) { await setAssigned(...) }`
— one request per envelope, no batch endpoint, no transaction around them. **Today**, if
request 7 of 12 fails, the user is half-assigned and nothing says so.

Undo inherits this exposure and adds none: a reversal loop is no less atomic than the forward
loop already is. Named so that nobody later reads a partial undo as an undo bug. Fixing it
means a batch endpoint, which belongs in its own issue rather than on this map.

## Confirming exchange

- The line — **"ใช่ เส้นนี้"**, on money-in-envelopes plus everyday marks, with transactions
  and structural CRUD both out.
- Bulk handling — **"หนึ่งรายการ ย้อนทีละซอง"**, chosen over a new atomic batch endpoint and
  over splitting the press into N rows.

## What this leaves for other tickets

- `stale-undo` gets a bounded problem: five act types, none of which create or destroy a row.
- `build-ship` needs no schema change for undo, and inherits the quick-assign reversal loop
  with its known non-atomicity.
- `whose-acts` and `keyboard-bindings` are untouched by this.

<!-- decision-map:resolution:end -->
