---
title: Build and ship - implement the rail and undo/redo, cover it, put it in prod
type: task
mode: HITL
status: open
assignee: 
blocked_by: [undo-semantics, rail-architecture, mock-signoff, change-history-view, whose-acts, stale-undo, keyboard-bindings]
gist: 
---

## Question

Implement the decided rail and the undo/redo engine, then ship to prod. Must include: a Playwright smoke spec for the rail on /budget (the only automatic gate that can catch a rendering bug, per CLAUDE.md), an interactive check on a real phone against the approved mock before pushing, and any EF entity plus its EF configuration landing in the SAME commit if history-storage chose a server-side store. Prod deploys on push to main, so the interactive check is not optional.

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["build-ship (this ticket)"]
    P0["change-history-view"] --> ME
    P1["keyboard-bindings"] --> ME
    P2["mock-signoff"] --> ME
    P3["rail-architecture"] --> ME
    P4["stale-undo"] --> ME
    P5["undo-semantics"] --> ME
    P6["whose-acts"] --> ME
```
<!-- decision-map:graph:end -->
