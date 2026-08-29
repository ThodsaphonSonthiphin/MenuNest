---
title: History - where does the undo/redo stack live, and does it survive a refresh?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [undo-semantics]
gist: 
---

## Question

Where is the undo/redo history kept: React/Redux memory only (dies on refresh, simplest), localStorage or IndexedDB (survives refresh on one device, can go stale against the server), or a server-side record so it follows the user across devices (most work, needs a new entity - the domain has none today). Decide the store, how deep the stack goes, and what happens to it when the user switches month on the MonthStrip or navigates away from /budget.

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["history-storage (this ticket)"]
    P0["undo-semantics"] --> ME
    ME --> C0["change-history-view"]
```
<!-- decision-map:graph:end -->
