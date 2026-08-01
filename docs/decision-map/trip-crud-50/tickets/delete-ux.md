---
title: Delete a trip - affordance, confirmation, and where the user lands afterwards
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [existing-edit-patterns]
gist: 
---

## Question

Where does deleting a trip live, how is it confirmed, and where does the user land afterwards? Deletion is final from the user's point of view - no undo toast and no trash bin - so the confirmation is the only safety net. Decide the affordance and its placement, what the confirmation says (whether it names the trip, its day count, or the number of stops that disappear with it), and the post-delete destination and feedback. The API and the RTK hook already exist and are entirely unused: useDeleteTripMutation has zero call sites in the SPA.
