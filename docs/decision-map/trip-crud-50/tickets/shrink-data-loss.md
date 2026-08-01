---
title: Shrinking the day count destroys scheduled stops - confirm, block, or allow?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [field-change-effects]
gist: 
---

## Question

Reducing a trip's day count deletes the trailing ItineraryDays and Stop rows cascade with them. UpdateTripHandler names this as silent data loss and says outright that an edit UI must confirm before shrinking a trip that has stops on the days being removed. Decide the policy: confirm and proceed, naming exactly what will be lost; hard-block the shrink until the user clears those days themselves; or allow it with an undo path. Decide whether the at-risk stop count is shown to the user, and what happens when that count cannot be known because the surface has not loaded the itinerary. This policy is a hard constraint on the edit surface's commit semantics, so settle it before the surface.
