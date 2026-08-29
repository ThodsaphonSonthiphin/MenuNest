---
title: Keyboard - what do Ctrl+Z and Ctrl+Shift+Z do when focus is somewhere awkward?
type: grilling
mode: HITL
status: closed
assignee: keyboard-bindings-1759
blocked_by: [undo-semantics]
gist: Ctrl+Z and Cmd+Z fire on the budget page but are inert inside inputs and while any dialog is open. The rail labels show the binding on desktop only.
---

## Question

Desktop keyboard shortcuts are in scope. Decide: which element must be focused for the binding to fire, and what happens when the caret is inside a text input or a number field where the browser's own native undo is expected; what happens while one of the budget dialogs is open (MoveMoney, QuickAssign, CoverOverspending, Transaction, AddAccount); whether Cmd+Z must work on macOS alongside Ctrl+Z; and whether the rail must visibly show the binding so the two surfaces do not feel like separate features.

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["keyboard-bindings (this ticket)"]
    P0["undo-semantics"] --> ME
    ME --> C0["build-ship"]
```
<!-- decision-map:graph:end -->

<!-- decision-map:resolution:start -->
## Resolution

Ctrl+Z and Cmd+Z fire on the budget page but are inert inside inputs and while any dialog is open. The rail labels show the binding on desktop only.

Detail: docs/adr/menunest-200-ctrl-z-is-inert-in-inputs-and-while-a-dialog-is-open.md

```mermaid
flowchart TD
    KEY["Ctrl+Z / Cmd+Z on desktop"]
    KEY --> LIVE["FIRES on the budget page"]
    KEY --> D1["INERT in input / textarea / contenteditable<br/>-> the browser's own undo wins"]
    KEY --> D2["INERT while any budget dialog is open<br/>-> the dialog shows figures the undo would move"]

    D1 --> ESC["Leaves EnvelopeCard's existing Escape=revert alone.<br/>Escape discards an edit NOT YET SENT.<br/>Undo reverses one ALREADY COMMITTED."]

    KEY --> LBL["Rail labels read 'Undo (Cmd)Z' on DESKTOP only<br/>- platform-aware, and the only thing making<br/>the button and the key feel like ONE feature"]

    D2 --> OPEN["Build wrinkle, NOT decided here:<br/>'is a dialog open' is not centrally known -<br/>the 5 dialogs are local useState"]

    style KEY fill:#dcfce7,stroke:#16a34a
    style ESC fill:#dcfce7,stroke:#16a34a
    style OPEN fill:#fef3c7,stroke:#d97706
```

Recorded in **menunest-200**, which holds the reasoning and the rejected options.
This ticket records only what the answer changes.

## One part of the ticket was not a real question

The ticket asked whether Cmd+Z must work on macOS "alongside" Ctrl+Z. It was not put to the
user as a choice: the developer's own machine is a Mac, so Ctrl-only would ship a feature that
does not work where it is written. Stated as forced rather than asked.

## What decided the rest

Two facts from the code:

- **This is the app's first global keyboard shortcut.** Everything today is local — Enter to
  send in the AI chat, Enter to add a checklist item, Escape to leave add-place mode,
  Enter/Escape on the envelope's inline amount input. A precedent, though a much smaller one
  than menunest-198's first permission role.
- **`EnvelopeCard.tsx:92` already binds Escape to revert** that inline input. Escape discards
  an edit **not yet sent**; Undo reverses one **already committed**. Keeping Ctrl+Z out of
  inputs is what stops the two from arguing — and it means the in-field need was already met
  before this feature existed.

## Confirming exchange

- When it is inert — **"ในช่องกรอก + ตอนกล่องเปิด"**, chosen over inputs-only and over firing
  everywhere.
- The rail labels — **"บอก เฉพาะเดสก์ท็อป"**, so the button and the shortcut read as one
  feature on desktop and the hint is not noise on a phone.

## What this leaves for build-ship

**"Is a dialog open?" is not centrally known.** The five budget dialogs are local `useState`
inside their own components and nothing tracks them. Either the handler checks the DOM for an
open `.budget-modal-overlay`, or the dialogs register with the `ShortcutRailProvider` that
menunest-199 is adding anyway. The second is probably right for exactly that reason, but the
ADR deliberately leaves it to the build.

Also inherited: the label must be platform-aware, ⌘ on macOS and Ctrl elsewhere.

<!-- decision-map:resolution:end -->
