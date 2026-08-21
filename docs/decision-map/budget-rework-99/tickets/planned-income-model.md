---
title: Planned income - how is expected salary represented, and how is it kept out of real money?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [ynab-parity-research]
gist: 
---

## Question

A future month needs to answer "salary X expected, Y budgeted, Z left" before the salary has arrived - which YNAB deliberately refuses to do. Decide how expected income is stored: a per-month figure per family, or a recurring salary rule that generates months, and does it carry an expected date? Decide the hard boundary: planned income must never enter the real current-month Ready-to-Assign, so what exactly does it feed, what happens to the planned figure when the actual salary transaction lands (is it reconciled, replaced, or left to drift), and what happens to a future month's assignments if the salary is smaller than planned or never arrives?

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["planned-income-model (this ticket)"]
    P0["ynab-parity-research"] --> ME
    ME --> C0["conversational-budget-jobs"]
    ME --> C1["future-month-view"]
```
<!-- decision-map:graph:end -->
