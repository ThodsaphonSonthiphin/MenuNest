---
title: A trip whose start date is already past - may its start date or day count be edited at all?
type: grilling
mode: HITL
status: open
assignee: 
blocked_by: []
gist: 
---

## Question

UpdateTripHandler will happily move a trip into the past: it has NO past-date guard, while RetimeStopToHourHandler - the other writer of Trip.StartDate, using the same DayRealigner - does have one (RetimeStopToHourHandler.cs:40-41). ADR-140 has now established that UpdateTrip can carry domain guards, so the asymmetry is a live choice rather than an oversight. Decide: may a past-dated trip's start date and/or day count be edited, freely or not at all; does UpdateTrip inherit the past-date guard, and if so does it apply to the whole command or only to a backward move; and what does the edit surface show for a past trip - the ADR-139 disable-one-control-with-a-reason pattern is available. Note the silent degradation this interacts with: moving a trip into the past flips every stop's on-arrival weather to 'past' and renders No-data (weather.ts:8-12,57-58), and can add or remove season warnings and opening-hours flags on stops nobody touched.
