# ARCHITECTURE.md template — assembly guide

Assemble the final document from the `fragments/` files in THIS reading order.
User stories come first because they answer "why does this org exist" — the
context that makes every technical section meaningful. The chapter names inside
Parts 3–4 should reuse the SiteMap Area/Group names (the business's own
taxonomy), not invented ones.

```markdown
# <Org / App Name> — Architecture Reference

> One-paragraph summary: what the business does with this system, in plain words.

## Table of Contents
(clickable anchors; avoid `&`, `--`, `/`, `*`, `()` in headings — they break anchors)

(overview Mermaid diagram here — the convention's mandatory document thumbnail)

---
## Part 1 — User Stories                          ← from fragments/09-processes.md
One subsection per persona (≈ security role) and key process:
### US-XX: <title>
AS A <role> I WANT TO <action> SO THAT <outcome>
(mermaid sequenceDiagram of the lifecycle: form → JS → automation → status)

---
## Part 2 — Overview
- What this system does (3 sentences max)
- Apps and solutions (names, publishers, managed/unmanaged)
- Component counts table (entities / forms / web resources / PCF / workflows /
  plugins / flows / commands / roles)                ← from fragments/01-inventory.md

---
## Part 3 — Data Model                             ← from fragments/02-entities.md + 03-data-model.md
- Entity universe table: entity | display name | custom/standard-customized |
  in navigation? | record count | business area (sitemap group)
- mermaid erDiagram (transactional core + master data; mark master data)
- Data dictionary per business area
- Option-set values table — every status/category/type WITH its label
  (this is the business vocabulary; do not skip values)

---
## Part 4 — Business Logic and Rules              ← from fragments/04-automation.md + 05-client-side.md
Per business area, then per layer:
- Validation rules (table: field | rule | layer [JS/business rule/plugin] | why)
- Cascade and auto-population rules
- mermaid flowchart TD for non-trivial decision logic
- Nullability / silent fallbacks (what happens when data is missing)

---
## Part 5 — Automation Inventory                   ← from fragments/04-automation.md
One properties table per item (workflow / plugin step / flow / webhook):
| Property | Value |
|---|---|
| Type | classic workflow / business rule / plugin / cloud flow / webhook |
| Trigger | entity + message + stage + filtering attributes |
| Mode | sync / async |
| What it does | one paragraph |
| **Why** | what business goal it serves — what breaks without it |
The Why row is mandatory. "Updates field X" is a What, not a Why.

---
## Part 6 — Client-Side Map                        ← from fragments/05-client-side.md
- Form → event → library.function table (per entity, per form)
- React/PCF components: name | kind (PCF virtual/standard, HTML-hosted) |
  tables it reads/writes | where used | source repo (if found)
- Web resource inventory grouped by name prefix

---
## Part 7 — Command Bar Map                        ← from fragments/06-commands.md
| Bar (entity + location) | Button | Generation | What runs | When shown | Why |
Classic and modern in ONE table — readers should not need to care which
generation a button is, only what it does.

---
## Part 8 — Security Model                         ← from fragments/07-security.md
- Roles table: role | persona description | key privileges | app access
- Business unit tree (mermaid graph TD)
- Field security: which columns are locked from whom

---
## Part 9 — Analytics (if present)                 ← from fragments/07b-analytics.md
- Embedded Power BI reports: report | dataset | Dataverse tables feeding it
```

## Rules

1. **Every automation/API/command entry has a Why row** — the business reason,
   not the technical trigger. Test: "what breaks or becomes impossible without
   this?" If the answer isn't written, the entry is incomplete.
2. **Mermaid everywhere** — sequenceDiagram for lifecycles, erDiagram for data,
   flowchart TD for decision logic, graph TD for hierarchies. Implements the
   marketplace diagram convention
   (`${CLAUDE_PLUGIN_ROOT}/references/diagram-convention.md`), including its
   mandatory overview diagram at the top of ARCHITECTURE.md.
3. **No speculation.** Only what the extracted files and queries showed.
   Unclear behavior is documented as *unclear*, with what was checked.
4. **Tables for scannable facts, prose for meaning.** Option-set values,
   validation rules, command maps → tables. Why a process exists → prose.
5. **Horizontal rules between Parts**; quick-pass studies (steps 1–4) emit
   Parts 2, 3, and 5 only — the entity map and automation inventory.
