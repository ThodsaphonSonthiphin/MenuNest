---
title: Keyboard - what do Ctrl+Z and Ctrl+Shift+Z do when focus is somewhere awkward?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [undo-semantics]
gist: 
---

## Question

Desktop keyboard shortcuts are in scope. Decide: which element must be focused for the binding to fire, and what happens when the caret is inside a text input or a number field where the browser's own native undo is expected; what happens while one of the budget dialogs is open (MoveMoney, QuickAssign, CoverOverspending, Transaction, AddAccount); whether Cmd+Z must work on macOS alongside Ctrl+Z; and whether the rail must visibly show the binding so the two surfaces do not feel like separate features.

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["keyboard-bindings (this ticket)"]
    P0["undo-semantics"] --> ME
```
<!-- decision-map:graph:end -->
