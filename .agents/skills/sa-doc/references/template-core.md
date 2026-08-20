# sa-doc core template

Section order and rules shared by both profiles. Every slot names the model
path that fills it — prose may connect and explain, never introduce facts that
are not in `sa-model.yaml`. Where a field below has no model path, the rule is
the same: use only what the input states, else "—"/`TBD` — never invent
(Source-or-TBD, see SKILL.md). The document language is `meta.language`.

## Document furniture (render markers)

The renderer (`render_doc.py`) turns three HTML-comment markers into document
furniture that both HTML and PDF need — emit them literally in the Markdown:

- `<!-- sa-doc:toc -->` — replaced by an auto-numbered table of contents built
  from the headings. Place it once, after the title block.
- `<!-- sa-doc:pagebreak -->` — forces a page break in the PDF. Put one after
  the title block and one after the TOC so the cover and contents each own a page.
- Figures (Mermaid) and tables are auto-numbered with localized captions
  ("รูปที่ N" / "Figure N", "ตารางที่ N" / "Table N") — do not hand-number them.

## Document skeleton (in order)

1. **Title block** — `meta.project`, `meta.org`, `meta.authors`, `meta.date`.
   Follow it with `<!-- sa-doc:pagebreak -->`, then `<!-- sa-doc:toc -->`, then
   `<!-- sa-doc:pagebreak -->` (cover page, then a contents page).
2. **Overview diagram** (diagram convention Rule 1) — one Mermaid `flowchart TD`
   (≤ 15 nodes): actors → the system → major use-case groups → key entities.
3. **1. ที่มาและปัญหา / Background & problem** — `problem.current_problems`,
   `problem.objectives` (with the P→O links stated), scope table from `scope`
   (one row per actor: capability list + the use cases that implement it).
4. **2. Requirements** — FR table: one row per scope capability. Use the
   capability's `scope.id` as the FR id when the model gives one (so it stays
   stable and other artifacts can cite it); otherwise number `FR-n` in scope
   order. Column linking to its use cases. NFR table from `nfrs` (id, category,
   requirement, measurable metric).
5. **3. Use cases** — one `flowchart TD` overview (actors ↔ use cases), then
   per use case the 13-field description (below).
6. **4. Interactions** — one Mermaid `sequenceDiagram` per use case from
   `main_flow` (participants: the actors + the system + external actors).
7. **5. Activity** — one `flowchart TD` per use case that has branches
   (extensions render as decision diamonds); skip use cases with a linear flow.
8. **6. States** — one Mermaid `stateDiagram-v2` per `states` group; a table of
   transitions (from, to, trigger, use case).
9. **7. Data & class model** — two type-matched diagrams from the same
   `entities`, because they answer different questions (see the diagram
   convention: `classDiagram` for the OO/domain view, `erDiagram` for the
   database view):
    - **7.1 Class model** — one Mermaid `classDiagram`: one class per entity
      with its attributes (`+field : type`, PK/FK noted) and any `operations`.
      Draw relationships from the `relationships` list. `from` is the
      subordinate/source, `to` is the target/parent (see the contract). For the
      symmetric-looking edges keep `from` on the left:
      `{from} --> {to}` association, `{from} ..> {to}` dependency,
      `{from} o-- {to}` aggregation, `{from} *-- {to}` composition. For the
      **direction-sensitive** edges put the parent/interface (`to`) on the left
      so the hollow triangle lands on it: `{to} <|-- {from}` generalization,
      `{to} <|.. {from}` realization. Multiplicity from `from_card`/`to_card`.
      If the model lists no relationships, derive one association per `fk`.
    - **7.2 ER model** — one Mermaid `erDiagram` from `entities` (PK/FK marked,
      one relationship per `fk`); then the data dictionary: one table per
      entity (field, type(size), description, key).
10. **8. Architecture** — `architecture.style` + components table +
    `graph TD` of the components; deployment paragraph.
11. **9. Screens** — table from `screens` (id, name, use cases served).
12. **10. Traceability matrix** — generated from ids only, zero manual upkeep:
    - **Requirement chain:** P → O → UC → ENT/SCR, one row per problem chain.
    - **Quality chain:** each `nfrs`/`security` entry that carries `objectives`
      or `use_cases` refs gets a row linking the NFR/SEC id to what it supports;
      list any NFR/SEC with no ref under "system-wide" so nothing is silently
      untraced.

## The 13-field use case description (semantics enforced)

| # | Field | Rule |
|---|---|---|
| 1 | Use case name | from `name` |
| 2 | Scope | the SYSTEM under design (`meta.project`), not the use case name |
| 3 | Level | user goal / subfunction; if the input gives no basis to classify, "—" |
| 4 | Primary actor | from `actors` |
| 5 | Stakeholders & interests | the actors from `scope` rows pointing here; state an interest ONLY if the input states it, else "—" — never invent a goal/interest |
| 6 | Preconditions | from `preconditions` |
| 7 | Success guarantee | from `postconditions` — a guaranteed state, NEVER a button press |
| 8 | Main success scenario | numbered steps from `main_flow` — one goal per use case |
| 9 | Extensions | from `extensions`, each anchored `<step>a.` to a main-flow step; no generic "system crashed → restart" boilerplate |
| 10 | Special requirements | from `special_reqs`; empty = "—" |
| 11 | Technology & data variations | only variations stated in the input/model, else "—" — never invent a variation |
| 12 | Frequency | a frequency stated in the input/model, else "—" — never invent a number |
| 13 | Open issues | unresolved questions from the model's TBD inventory + the input; none = "—" — never invent plausible-sounding issues |

## Diagram rules

Follow `${CLAUDE_PLUGIN_ROOT}/references/diagram-convention.md`: overview
diagram first (Rule 1), type-matched section diagrams (Rule 2 — including the
`classDiagram` row for the OO/domain view and the `stateDiagram-v2` row for
entity lifecycles), every diagram introduced by at least one sentence.
