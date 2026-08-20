---
name: grill-then-plan
description: Like sp-grill-with-doc (domain-aware grilling, glossary sharpening, inline CONTEXT.md/ADR capture) BUT continues into the superpowers planning pipeline by handing off to superpowers:writing-plans at the end. Use ONLY when the user wants both the grilling AND a written implementation plan produced afterward; if they want grilling/docs alone, use sp-grill-with-doc instead. Requires the superpowers plugin.
effort: max
---

<what-to-do>

Run a domain-aware design session, then hand off to the superpowers planning
pipeline. Do NOT write code, scaffold, or invoke any implementation skill until
the design spec is approved and you have invoked `superpowers:writing-plans`.

</what-to-do>

## Step 0 — Preflight: ensure superpowers is installed

This skill delegates its final step to the `superpowers:writing-plans` skill.
Check that dependency FIRST, so the user never spends a whole session only to hit
a wall at handoff. This skill runs on more than one harness (Claude Code,
Antigravity); detect and install the way *this* harness does it.

1. **Detect** superpowers. The harness-agnostic signal is **skill availability**:
   check whether the superpowers skills (`writing-plans`, `brainstorming`) appear
   in your surfaced skill list or can be loaded. If your harness also exposes an
   install registry, you may consult it:
   - **Claude Code:** read `~/.claude/plugins/installed_plugins.json` for the key
     `superpowers@claude-plugins-official`, or a directory matching
     `~/.claude/plugins/cache/claude-plugins-official/superpowers/*/`.
   - **Antigravity:** look for the superpowers skills in your skills dir
     (`~/.gemini/config/skills/`, `~/.gemini/antigravity-cli/skills/`, or the
     project's `.agents/skills/`).
2. **If present** → continue to Step 1.
3. **If missing** → tell the user superpowers is required, then offer to install
   it with the command for their harness:
   - **Claude Code:** confirm the marketplace is registered (`/plugin marketplace
     list`; if absent, `/plugin marketplace add anthropics/claude-plugins-official`),
     then `/plugin install superpowers@claude-plugins-official`.
   - **Antigravity:** install a superpowers skills port (e.g. the community
     `superpowers-antigravity`) into the harness's skills dir, then reload.
4. **Wait for the user to confirm the install completed**, then re-verify using
   the detection in (1). Plugin/skill installation is not instantaneous and may run
   through an interactive UI — do NOT re-verify in the same turn you issued the
   install, or you will read stale state and wrongly conclude it failed. Ask the
   user to confirm (or to re-run this skill) first.
5. **If now present** → continue to Step 1.
6. **If still missing or the install could not complete** → STOP. Do not start
   grilling. Tell the user explicitly that the handoff to `superpowers:writing-plans`
   can't run without superpowers, name the install command for their harness, and
   ask them to install it and re-run. Never fail silently and never start a session
   you cannot finish.

## Step 1 — Explore context

Read the codebase, recent commits, and existing docs: `CONTEXT.md` /
`CONTEXT-MAP.md` at the repo root, and `docs/adr/`. If a `CONTEXT-MAP.md` exists,
the repo has multiple contexts — infer which one the topic relates to (ask if
unclear).

### Step 1a — Verify the cause first when planning a fix

If the plan exists **to fix something that currently misbehaves** — a bug, a
failure, wrong output, "it keeps breaking" — and the **root cause is not yet
verified**, do not start grilling. Grilling a fix design on top of an unverified
guess about *why* it breaks plans on sand. Hand off to **debug-mantra** to
establish the confirmed cause first, then return here and grill the fix design
against that verified truth.

Skip this guard — proceed straight to Step 2 — when **either** holds:

- The work is new (feature, refactor, redesign) with no malfunction behind it.
- The cause is **already verified** — e.g. you completed debug-mantra and it
  confirmed the cause (do not re-diagnose). Merely *entering* debug-mantra without
  a confirmed cause does not exempt you.

This is the symmetric partner to the forward debug chain (ADR 0003):
grill-then-plan verifies the cause first when planning a fix (ADR 0011). Either
way the invariant holds — *never plan a fix on an unverified cause.*

## Step 2 — Grill relentlessly, one question at a time

Interview the user about every aspect of the plan until you reach shared
understanding. Walk down each branch of the design tree, resolving dependencies
between decisions one-by-one. For each question, provide your recommended answer.
Ask one question at a time and wait for feedback before continuing. If a question
can be answered by exploring the codebase, explore the codebase instead of asking.

## Step 2.5 - Ask in the user's terms, not the model's

The person answering knows the product, not necessarily the schema. Pose every
question in what they can see and do -- which screen, what they press, what
happens next -- and only then give the model-level backing. When two or more
paths are in play, a small table beats prose:

| | where | what you press | what it writes |
|---|---|---|---|
| A | the screen that exists today | the control already on it | a row owned by its parent |
| B | the screen we are changing | no such control exists yet - it is what we are adding | a row owned by nobody |

The table is the SHAPE, not the scope. The same framing fits a queue, a schema,
a CLI flag or a cron job: name the surface the user would actually observe,
whatever that surface is.

Then make the stake observable with one concrete walk-through: "you save an
item while working inside project X; three months later you delete project X;
today the item disappears from your library too." A user who cannot picture
the consequence cannot choose between the options.

Two tells that the FRAMING was wrong rather than the explanation: the answer
comes back as a question ("what do you mean?", "which step of the app is
this?"), or you needed entity names and ADR numbers just to state the options.
Re-pose it - do not re-explain it at greater length.

Put trade-off reasoning in the message body where it can actually be read;
keep option labels to a few words.

## Step 3 — Stay domain-aware while grilling

- **Challenge against the glossary.** If a term conflicts with `CONTEXT.md`, call
  it out: "Your glossary defines X as A, but you seem to mean B — which is it?"
- **Sharpen fuzzy language.** Propose a precise canonical term for vague or
  overloaded words: "You're saying 'account' — Customer or User?"
- **Discuss concrete scenarios.** Invent edge-case scenarios that force precision
  about boundaries between concepts.
- **Cross-reference with code.** When the user states how something works, check
  the code agrees; surface any contradiction.
- **Blast radius of a new requirement.** When the design adds a *required* input, a
  new validation, or otherwise tightens the contract of an **existing**
  endpoint / tool / shared function, grill what happens to the callers and code
  paths that don't use the new feature. Default to enforcing the requirement
  **only where the new input is actually consumed** (validate lazily) rather than
  globally/eagerly — eager enforcement silently regresses unrelated callers. Make
  the global-vs-scoped choice explicit; don't let it default to global.

## Step 3.5 — Mock the UI when seeing would change the answer

Some grilling questions are about a UI surface — layout, where an element sits,
what's on screen, the flow between states. Words agree falsely here: you and the
user both think you understand each other and picture different screens, because
neither saw it. When a question can only be resolved by looking, **make the mockup
the question** — render it from the UI decisions resolved so far, in the project's
own design language, and ask the user to confirm or correct, then loop. If the
project has no design system, first have the user establish a minimal design
baseline (you propose the starter) — never wireframe ad-hoc, or the mockups and the
build drift apart. Skip this entirely for designs with no visual surface. Put the mockup in the project's Claude Design design-system project via `DesignSync` (PREFERRED). A rendered Artifact, then a self-contained `.html`, are fallbacks used ONLY when DesignSync/Artifact are unavailable; never default to a local `.html` even if a prior `.html` mockup already exists in the repo. Full mechanism (when to render vs not, how, and carrying it to the spec) is in `${CLAUDE_PLUGIN_ROOT}/references/ui-mockup.md` -- read it before the first render.

## Step 4 — Capture inline as decisions crystallize

- **Update CONTEXT.md inline** the moment a term resolves — don't batch. Keep it
  a glossary only; no implementation detail. Create it lazily on the first
  resolved term if it doesn't exist. Use the format in
  [CONTEXT-FORMAT.md](./CONTEXT-FORMAT.md).
- **Always create an ADR for every design decision** — one ADR per decision, the
  moment the decision is made. Do not batch or defer. Create `docs/adr/` lazily
  on the first ADR. Use the format in [ADR-FORMAT.md](./ADR-FORMAT.md). Every
  ADR opens with a small Mermaid decision diagram (chosen vs rejected paths) —
  see `${CLAUDE_PLUGIN_ROOT}/references/diagram-convention.md`, Rule 3. A
  decision qualifies if the user chose one option over another — architectural
  shape, technology choice, naming, scope boundary, safety mechanism. When in
  doubt, write the ADR. A short ADR is better than a missing one.

## Step 4.5 — Recap & confirm

When grilling converges, **before writing the spec**, play the design back as a
**terminal recap** so the user can confirm it is captured correctly. Render it as a
terminal diagram per the *Terminal diagrams* family in
`${CLAUDE_PLUGIN_ROOT}/references/diagram-convention.md` (Unicode box-drawing,
vertical, ≲ 50 columns, inside a fenced block — never Mermaid, which does not render
live in a terminal):

- **Emit a flowchart of the grilled decisions — mandatory.** One box per decision in
  the order they were resolved, showing the chosen option, connected top-to-bottom.
  Every grilling session produces decisions, so this diagram always appears.
- **Emit a sequence of the runtime interaction — optional.** Show it only when the
  design has a genuine interaction (≥ 2 actors exchanging messages). Omit it for a
  pure data-model or config design — never force a one-actor diagram.
- **Point to the UI mockup if one exists.** If Step 3.5 produced a confirmed mockup,
  include its artifact URL / `.html` path in the recap, so the decision set and the
  screen are confirmed together.

Then ask: **"Does this capture the design?"**

- If the user **confirms**, continue to Step 5.
- If the user **corrects** anything, return to Step 2, grill the disputed point, then
  re-run this recap. Loop until confirmed.

This is a cheap checkpoint on the *decision set* before the spec exists; it is
distinct from Step 5's gate, which approves the *written spec*. Do not write the spec
until the recap is confirmed.

## Step 5 — Write the design spec

Once understanding is shared, write the design to
`docs/superpowers/specs/YYYY-MM-DD-<topic>-design.md` (`<topic>` is a
lowercase-kebab slug). The spec is a Markdown document — follow the diagram
convention in `${CLAUDE_PLUGIN_ROOT}/references/diagram-convention.md` (one
overview Mermaid diagram at the top; type-matched diagrams per section).
If a UI mockup was produced in Step 3.5, reference its final artifact URL / `.html`
path in the spec, so the plan and the implementer work from the same screen you
confirmed — one source of truth.
Run a self-review for placeholders, internal consistency,
scope, and ambiguity; fix inline. Ask the user to review the spec and approve
before proceeding. If they do NOT approve, return to Step 2 and grill on the
disputed points, then revise the spec — do not proceed to handoff until approved.

## Step 5a - Verify load-bearing claims before approval

If the spec's plan rests on claims about how EXISTING code or a live system
behaves (coupling, dependencies, "X already does Y", "path Z is ungated"),
verify those load-bearing claims against the actual code / live system BEFORE
asking for spec approval - a prose self-review cannot catch a false premise.
Correct the spec with what you find. A wrong assumption about the current
system silently breaks the plan built on it.

## Step 6 — Hand off

After the user approves the spec, invoke `superpowers:writing-plans` to produce
the implementation plan. This is the terminal state — do NOT invoke any other
implementation skill.
