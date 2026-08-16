---
title: Pending-correction visibility - how does the writer see which nights still need a correction pass?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [done-day-redefinition, mcp-tool-contract]
gist: 
---

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["pending-correction-visibility (this ticket)"]
    P0["done-day-redefinition"] --> ME
    P1["mcp-tool-contract"] --> ME
```
<!-- decision-map:graph:end -->

## Question

Now that correction is decoupled from the 7-minute writing session (done-day-redefinition), how does the writer see, days later, which entries are still waiting for a correction pass via Claude Code - a badge/count, a list, or nothing at all?
