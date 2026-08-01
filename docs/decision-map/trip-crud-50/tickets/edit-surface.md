---
title: Edit surface - where does editing an existing trip live, and how does it commit?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [existing-edit-patterns, shrink-data-loss]
gist: 
---

## Question

Where does editing an existing trip live, and how does it commit? The candidates: reuse or extend CreateTripDialog as a save/cancel edit dialog; extend the existing in-place commit-on-change header pattern field by field; or a dedicated edit route. Decide the entry point or points - a trip card on TripsPage, the trip detail header, or both - given that a card is currently one large tap target that navigates. Decide the fate of TripDateEditor: replaced, kept alongside, or subsumed. The shrink-data-loss policy constrains this choice directly, because an immediate-commit day stepper would fire a confirmation on every tap of the minus button.
