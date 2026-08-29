---
title: Reversible actions - which budget mutations join the undo stack, and which deliberately do not?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [undo-semantics]
gist: 
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
