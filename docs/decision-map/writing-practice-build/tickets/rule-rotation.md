---
title: Target-rule rotation - who flips the monthly target grammar rule?
type: grilling
mode: HITL
status: closed
assignee: 
blocked_by: []
gist: The writer flips the active target grammar rule by hand -- not an automatic calendar rotation.
---

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["rule-rotation (this ticket)"]
```
<!-- decision-map:graph:end -->

## Question

Does the active target rule (e.g. third-person -s, then articles) rotate automatically on a calendar schedule, or does the writer change it by hand?

<!-- decision-map:resolution:start -->
## Resolution

The writer flips the active target grammar rule by hand -- not an automatic calendar rotation.

Detail: docs/adr/174-the-target-rule-rotates-by-hand-so-the-app-must-read-and-set-it.md

```mermaid
flowchart TD
    Q["Who flips the monthly target rule?"]
    Q -.->|"DECLINED"| A["automatic, by calendar month<br/>counted from night 1, no screen for it"]
    Q ==>|"CHOSEN"| B["manual -- he flips it by hand<br/>more control, one more thing to remember"]
    B ==> C["CONSEQUENCE: mcp-tool-contract needs a way<br/>to read/set the active rule,<br/>not purely calendar-derived"]
```

# Target-rule rotation

**Chosen: manual.** The writer changes the active target grammar rule by hand (e.g. from the
progress screen or a settings control), rather than the app switching it automatically on a
calendar schedule.

## The trade-off, as put to the writer

- **Automatic, by calendar month (recommended, declined)** - no screen for it, changes quietly
  once a month counted from night 1.
- **Manual (chosen)** - more control over exactly when a rule change happens, at the cost of one
  more thing to remember.

## Consequence

The `WritingTools` MCP contract (`mcp-tool-contract`) needs a way for Claude Code to read (and
for the writer to set) the currently active target rule, since it is no longer purely
calendar-derived.

## His answer

Manual - "You flip it by hand."

<!-- decision-map:resolution:end -->
