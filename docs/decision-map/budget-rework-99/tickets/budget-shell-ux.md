---
title: The phone-first budget screen - what is on it, in what order, and what is one tap away?
type: prototype
mode: HITL
status: open
assignee: 
blocked_by: [current-budget-audit, account-balance-input, daily-allowance-formula]
gist: 
---

## Question

The stated reason for the rework is that the flow and UX are wrong, so this is the ticket that fixes it. Produce a docs/mocks/ mock of the reworked phone-first /budget and get it approved. Decide: what the user sees in the first screenful (daily allowance, Ready-to-Assign, account totals, envelopes - in what priority), how month navigation works on a phone, which actions are thumb-reachable one-tap (assign, move money, correct a balance, log a spend) and which are buried, and whether the envelope list stays a list or becomes something else. Desktop must not be broken but comes second.

<!-- decision-map:graph:start -->
```mermaid
graph TD
    ME["budget-shell-ux (this ticket)"]
    P0["account-balance-input"] --> ME
    P1["current-budget-audit"] --> ME
    P2["daily-allowance-formula"] --> ME
    ME --> C0["future-month-view"]
    ME --> C1["rollout-verification-bar"]
    ME --> C2["zero-out-affordance"]
```
<!-- decision-map:graph:end -->
