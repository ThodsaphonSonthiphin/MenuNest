---
name: debug-mantra
description: Four-mantra debugging discipline — reproduce, trace the fail path, falsify the hypothesis, cross-reference every breadcrumb. Recite the mantra block verbatim at the start of any debugging session, then apply the four steps in order before proposing any fix. Trigger on /debug-mantra and proactively whenever debugging starts — user reports a bug, says something is broken/throwing/failing, asks to debug/diagnose/investigate an issue, or pastes a stack trace or error log.
effort: max
---

# Debug Mantra

Four-step discipline for any debug session. Recite verbatim, then apply in order.

## Recite this — verbatim, as the first thing in your first response

> **Mantra:**
> 1. **First is reproducibility.** Can the issue be reproduced reliably?
> 2. **Know the fail path.** Debugger first; then source trace + knob enumeration; then in-code instrumentation; then production telemetry (ask first).
> 3. **Question your hypothesis.** What would disprove it?
> 4. **Every run is a breadcrumb.** Cross-reference all of them.

Then, in that same first response, emit this process diagram **verbatim** inside
a fenced code block — it is fixed text, part of the recital; do not re-author,
paraphrase, or reflow it:

```
DEBUG MANTRA — four steps, strictly in order
─────────────────────────────────────────────

  ① REPRODUCE
  │   reliable repro?  ·  flaky → raise the rate
  │   no repro at all → ■ STOP (don't hypothesise)
  │   got repro? → what changed?  git diff / blame / bisect
  ▼
  ② FAIL PATH   (escalate only when the prior fails)
  │   1. attach a debugger
  │   2. source trace + knob enumeration
  │   3. in-code instrumentation
  │   4. production telemetry / logs - ask the user first
  ▼
  ③ FALSIFY
  │   3–5 ranked hypotheses  ·  run the DISPROOF first
  ▼
  ④ BREADCRUMBS
  │   ledger every run  ·  cross-reference all of them
  │
  └─▶ contradiction with a past run?  ──  back to ③
```

Then begin work.

---

## 1. Reproduce reliably

Build a runnable repro before anything else.

- **Reliable repro** → capture the exact steps, inputs, and environment as a runnable artifact: failing test, curl script, CLI invocation, replay harness.
- **Flaky repro** → the bug is not yet debuggable. Raise the rate first: loop the trigger, parallelise, add stress, narrow timing windows, inject sleeps. 50% flake is debuggable; 1% is not.
- **No repro at all** → stop. Say so explicitly. Ask the user for env access, captured artifacts (HAR, log dump, core), or permission to instrument. Do **not** proceed to hypothesise.

**Provenance before analysis - for anything not running on your machine.** A screenshot, a log
line, or "it's broken in prod" is evidence from one *specific build*, and you cannot read the code
until you know WHICH. Ask **local or deployed** first; if deployed, get the branch and commit the
CI/CD system actually shipped and trace only that. Never assume the default branch: a pipeline that
is manual-trigger, `checkout: self`, or gated on an operator's choice ships whatever ref was queued.
Then check that ref against the tree you are about to read - a repo with several worktrees or
long-lived branches will hand you the wrong copy without complaint.

Target: a fast (1–5 s), deterministic pass/fail signal. Pin time, seed the RNG, freeze network, isolate filesystem.

Once a reliable repro exists, ask **what changed**. Most bugs are regressions — `git log` / `diff` / `blame` the suspect area, and if the repro is scriptable, `git bisect` it against that pass/fail signal to pin the exact commit. The offending diff is often the whole answer.

## 2. Know the fail path

Once reproducible, find *where* the code breaks and *what stops it from breaking*. The differential narrows the search. Try in this order — escalate only when the prior tactic fails.

1. **Attach a debugger.** If the env supports it, attach and step to the failure site. One breakpoint beats ten logs. Do this **before** turning any knobs.
2. **Source trace + knob enumeration.** If no debugger (or it can't reach the bug), trace the code path end-to-end and list every knob that can influence the outcome:
   - config flags, env vars, feature toggles
   - branch conditions, input shape
   - timing, concurrency, build options
   Each knob is a candidate axis to flip in the differential. Flip one at a time.
3. **In-code instrumentation.** If outside knobs can't move the failure, go inside: `printf` / log statements at the suspected fail site, dump the relevant internal state. Tag every probe with a unique prefix (e.g. `[DBG-a4f2]`) so cleanup is a single grep. Let the trace show where reality diverges from your model.

4. **Production telemetry / log stores — ask first.** For a deployed system that emits telemetry (Application Insights / Log Analytics, centralised logs, an error tracker, APM traces), captured exceptions, failed dependencies, and request/result-code history are strong fail-path evidence — and often a reproduction substitute for intermittent or slow ("after an hour", "only in prod") bugs. But do NOT jump straight to it or silently query external log stores: run the normal tactics above first, then **ask the user whether to go look at the logs** — it is their system, their cost, their call. Query only once they say yes.

## 3. Falsify the hypothesis

When a candidate root cause surfaces, scrutinise it **before** testing it.

- Does it actually explain the symptom end-to-end? Walk it through.
- What is the simplest **proof**? What is the cleanest **disproof**?
- Run the **disproof first**. If the hypothesis survives, it's real. If it dies, you saved yourself from chasing a phantom.
- Generate 3–5 ranked hypotheses, not one. Single-hypothesis thinking anchors on the first plausible idea.
- Rank on evidence verified for the EXACT path in question. A comment or docstring is evidence
  about the one path it annotates, never its siblings - "this action does not do X" sitting beside
  another action that does is enough to demote the true cause and send you chasing config and
  permissions instead.

## 4. Every run is a breadcrumb

Maintain a running **ledger** of every experiment in this session. Each entry: what changed, what happened, what it ruled in or out.

- When a new hypothesis surfaces, walk the ledger. Does it hold for **every** prior observation, not just the most recent?
- If any past run contradicts it, the hypothesis is wrong or incomplete — refine or discard.
- When in doubt, design the **single experiment** whose outcome makes it certain. Run that next, instead of churning on adjacent runs.
- Update the ledger after every run. It is your memory across the session.

---

## Operating rules

- Recite the mantra block **once** per debug session, in your first response. Do not re-recite mid-session.
- Recite **verbatim**. Never paraphrase, shorten, or skip lines of the recital.
- Emit the **process diagram verbatim** right after the recital, in that same first response — it is part of the recital. Never re-author, paraphrase, or reflow it.
- If the user says "skip the mantra" → skip the recital **and the diagram** but still apply the four steps silently.
- Apply the four steps **in order**:
  - Do not propose a fix before #1 is satisfied (reliable repro exists).
  - Do not start testing hypotheses before #2 has narrowed the fail path.
  - Do not commit to a hypothesis before #3 has tried to disprove it.
  - Do not declare a hypothesis correct until #4 confirms it against every prior breadcrumb.
- If you catch yourself proposing a fix without a reliable repro, stop and return to step 1.
- The mantra is a constraint **you** carry through the session — not advice to deliver back to the user.

---

This skill follows the **terminal-diagram** convention — canonical wording in
`.agents/references/diagram-convention.md`.
