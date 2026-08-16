---
title: MCP tool contract - what does the WritingTools MCP class expose?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [ai-correction-invocation]
gist: 
---

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["mcp-tool-contract (this ticket)"]
    P0["ai-correction-invocation"] --> ME
    ME --> C0["pending-correction-visibility"]
```
<!-- decision-map:graph:end -->

## Question

What MCP tools does a new WritingTools class (mirroring the existing TripTools pattern) expose for the writing-practice feature - submit/fetch today's entry, record a correction, get/set the active target rule - and what fields does each carry (date, text, elapsed seconds, target rule, hit/miss counts, bracketed stuck-words, words-per-minute)?
