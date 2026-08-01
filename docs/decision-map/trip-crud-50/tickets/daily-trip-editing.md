---
title: Daily trips - what does the edit surface offer when IsDaily is set?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: [edit-surface]
gist: 
---

## Question

What does the edit surface do for a daily trip (IsDaily, #49)? Daily trips are in scope for editing, but the daily on/off toggle stays where it is on the detail header, so the edit form never sets IsDaily. Decide which fields the form offers for a daily trip, and how it renders the ones that do not apply: dayCount is pinned to 1 and the start date is projected to today, and CreateTripDialog already hides or overrides them when isDaily is set. Decide too what happens if a multi-day regular trip is toggled to daily from the header while the edit surface is open.
