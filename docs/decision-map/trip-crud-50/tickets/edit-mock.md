---
title: Visual mock for the trip edit and delete surface, approved before any code
type: prototype
mode: HITL
status: open
assignee: 
blocked_by: [edit-surface, delete-ux]
gist: 
---

## Question

Produce the visual mock for the trip edit and delete surface, once the surface, the shrink confirmation and the delete confirmation are decided, and get it approved before any code is written. Every trip UI in this repo is mockup-backed - there are more than fifteen trip mocks in docs/mocks and none of them covers edit or delete - and the review gates are blind to visual fidelity, so a task can pass every automated and agent gate and still ship visibly wrong. The mock is the artifact the implementation is diffed against before merge.
