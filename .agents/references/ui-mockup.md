# UI mockup-in-the-loop (a grilling aid)

A way to grill a UI decision by *showing* it, not describing it. Used from inside a
grilling session (grill-then-plan, grill-with-docs) — never a standalone step.

## When it applies

Only when a grilling question touches a UI surface the user pictures on a screen:
layout, where an element sits, what appears on screen at once, the flow between
states. Skip entirely for designs with no visual surface (data model, config, CLI,
backend pipeline). If a whole session raises no UI question, no mockup is ever made.

## Why show, not tell

Text agreement on UI is false agreement: you and the user both believe you
understand each other and picture different screens — because neither saw it. A
mockup is the disambiguation device. It makes "that's not what I meant" happen
*now*, while the decision is cheap to change, instead of after the feature is built.
A re-render is far cheaper than a mis-built feature, so never skip a render to save
cost.

## The rule — render when seeing would change the answer

Render or update the mockup when the open question can only be resolved by looking:

- where an element sits / the overall layout
- what appears on the screen at once
- the flow between screens or states

Do NOT render for what text already pins down — a label's exact words, a validation
rule, a copy tweak. The gate is "does seeing it change the answer?" — not a fixed
checkpoint, and not "render after every answer." Update the SAME card/file on each
later render; never spawn a new one per edit (hygiene, not a reason to render less
often).

## Where the mockup lives — a Claude Design design-system project (preferred)

When the harness can reach Claude Design (claude.ai/design) through a design-sync
mechanism (Claude Code's `DesignSync` tool), the mockup — and the design baseline it
is built from — live as **cards in a Claude Design design-system project**, not as a
throwaway file. That project is the reusable home the user browses across tools and
future sessions push into, so keeping the mock there (rather than a one-off Artifact
or a local `.html`) is the default. One design-system project per real project;
reuse it, never spawn a second.

Fallbacks, in strict order, only when the preferred path is genuinely unavailable:

1. **Claude Design design-system project** via design-sync (preferred).
2. A rendered **Artifact** (a harness like Claude Code that renders artifacts).
3. A single self-contained **`.html`** (inline CSS/JS) in the working dir, path given
   to the user.

Never default to ad-hoc styling or a personal "local HTML in a mocks folder + browser
devtools" habit — that is the last resort (option 3), never the mechanism.

## Establish the design language first — don't wireframe around it

A mockup is only as consistent as the design language behind it. Source it from the
project's real design language: existing CSS/theme, a component library, a
Tailwind/token config, or a Figma design system (pull tokens via the harness's Figma
mechanism).

If the project has no design system at all, do NOT paper over the gap with ad-hoc
styling — every mockup would drift, and so would the build. Surface it as a decision:
offer a *minimal* starter (palette, type scale, spacing scale, a handful of base
components) so it never blocks the session; the user approves or edits it. Persist the
approved baseline both as a file in the repo (the anchor the build inherits) **and**,
via design-sync, as **Foundations / Components cards in the same Claude Design
design-system project** the mockup screens live in. Capture the baseline as an ADR like
any other decision. Only once a baseline exists do you render.

## How to render

1. Gather the set of UI decisions resolved so far for this surface (not just the
   latest answer); the mockup reflects the whole accumulated set.
2. Source the design language (above). Found none → STOP and establish a baseline
   first — never ad-hoc styling.
3. Produce the mockup in the Claude Design design-system project (the preferred path):
   - **Reuse or create the project.** Find it via the design-sync mechanism (Claude
     Code: `DesignSync list_projects` — it lists only design-**system** projects, so a
     regular design project will not appear). Reuse the one that belongs to this repo;
     create it only when none exists.
   - **Build each surface as a standalone `.html`** styled in the project's design
     language, whose **first line** is a card marker
     (`<!-- @dsCard group="Screens" -->`; baseline files use `group="Foundations"` /
     `"Components"`). The Design System pane builds its card index from that marker.
   - **Push via the two-step plan:** `finalize_plan` (it needs **both** `writes` and
     `deletes`, even if `deletes` is `[]`, plus the local dir the files sit in) →
     `write_files`. Updating a mock = rewrite the same card path and push again. A **new** card also needs its `{path, group}` added to `_ds_manifest.json` `cards[]` and pushed -- the `@dsCard` marker is not auto-compiled on push, so a new screen stays invisible until the manifest lists it.
   - Fall back to an Artifact, then a self-contained `.html`, only when the preferred
     path is unavailable (see "Where the mockup lives").
4. Ask it as a grilling question, with the mockup as your recommended answer: "Here's
   how I read the decisions so far as a screen — is this what you mean? Anything in the
   wrong place or missing?" If corrected → grill the point, update the SAME card
   (re-push), ask again. Loop until confirmed.

## Carry it to the spec

Reference the final mockup in the design spec — the Claude Design project + card name
(or the Artifact URL / `.html` path when a fallback was used) — so the plan and the
implementer see the same screen you confirmed: one source of truth, no drift between
words and picture.
