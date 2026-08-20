# sa-model.yaml — the sa-doc model contract

The single source of truth for a generated SA&D document. Every section of the
document is derived from this file; prose may explain but never introduce
actors, use cases, entities, fields, or states that are not in the model.
This file is the only place the schema is defined.

## Conventions

- Every object carries a stable `id`. Prefixes: `P`/`O`/`B` (problem/objective/
  benefit), `ACT`, `UC`, `ENT`, `SCR`, `NFR`, `SEC` — e.g. `UC-ORDER`, `ENT-PRODUCT`.
- Cross-references use ids only, never names.
- `TBD` (case-insensitive) is a legal value for every leaf **except**
  `meta.project`/`language`/`profile` (E9 — those are project decisions, not
  domain facts). **Source-or-TBD:** fill a leaf only with a value the input
  states; if the input is silent the value is `TBD`, never a plausible default,
  estimate, or example. This holds even where a warning nags you to fill it
  (W6/W8/W9) or a profile requires a section (E8): satisfy the gate with a
  `TBD`-valued record, never manufactured content. TBDs are inventoried by the
  validator and surfaced in the final summary — never silently invented.
- Language of `name`/`text` values follows the document language.

## Schema

```yaml
meta:                        # E9: project, language, profile are required and
                             # concrete (never TBD) — profile selects the whole
                             # template and the professional security gate (E8)
  project: string            # short slug used in file names (required)
  org: string
  language: th | en          # document language (required, must be th or en)
  profile: academic | professional   # required, must be one of these two
  authors: [string]
  date: YYYY-MM-DD

problem:
  current_problems: [ {id: P1, text} ]
  objectives:       [ {id: O1, text, problems: [P1]} ]     # which problems it answers
  benefits:         [ {id: B1, text, objectives: [O1]} ]

actors: [ {id: ACT-X, name, desc} ]

scope:                       # every capability MUST point at >= 1 use case (E2)
  - id: FR-1                  # optional; give it an id to make the functional
                              # requirement citable + stable across regenerations
                              # (E6 checks uniqueness). Omit and the document
                              # numbers it FR-n at generation time.
    actor: ACT-X
    capability: string
    use_cases: [UC-X]

use_cases:
  - id: UC-X
    name: string
    actors: [ACT-X]                       # must exist (E1)
    objectives: [O1]                      # optional; W1 checks coverage
    preconditions: [string]
    postconditions: [string]              # guaranteed state AFTER success —
                                          # never a trigger like "user clicks save" (W4)
    main_flow:
      - {step: 1, actor: ACT-X, action: string, system_response: string,
         fields: [ENT-X.field]}           # optional machine-checkable refs (E4)
    extensions:
      - {at_step: 3, condition: string, flow: string, fields: []}
    special_reqs: [string]                # only real ones; empty list is fine
    entities: [ENT-X]                     # entities this use case touches
    screens: [SCR-X]

entities:
  - id: ENT-X
    name: string
    operations: [string]                  # optional; methods shown on the class
                                          # diagram (e.g. "calculateTotal()").
                                          # Omit for a pure data entity.
    fields:
      - {name: string, type: string, size: int, desc: string,
         pk: true,                        # optional but W8 warns if an entity
                                          # with fields declares no primary key
         fk: ENT-Y.field,                 # optional; target must exist (E3)
         sample: string}                  # optional; input-stated example only,
                                          # never invented data; W6 checks length

relationships:                            # optional; the class-diagram associations.
  # Direction: `from` = the subordinate/source (child, implementer, part,
  # subject), `to` = the target (parent, interface, whole). For a
  # generalization/realization `to` is therefore the SUPERTYPE — the class the
  # hollow triangle points at — so inheritance never renders backwards.
  - {from: ENT-X, to: ENT-Y,              # both must exist (E10)
     type: association,                   # association | aggregation | composition |
                                          # generalization | dependency | realization (E10)
     from_card: "1", to_card: "*",        # optional UML multiplicity labels
     label: string}                       # optional verb phrase on the line
  - {from: ENT-MANAGER, to: ENT-EMPLOYEE, # "Manager is-a Employee": child=from,
     type: generalization}                # parent(supertype)=to
                                          # No relationships? The class diagram
                                          # derives associations from fk targets.

states:                                   # one group per stateful entity field
  - entity: ENT-X
    field: status                         # must exist on the entity (E5)
    states: [string]                      # field type must be able to hold them (E5)
    transitions: [ {from, to, trigger, uc: UC-X} ]

nfrs: [ {id: NFR-X, category, requirement, metric,   # input-stated metric; else TBD (W9 warns on empty)
         objectives: [O1], use_cases: [UC-X]} ]      # optional trace refs (E6);
                                                      # surface in the traceability matrix

security: [ {id: SEC-X, concern, control,            # REQUIRED non-empty when professional (E8)
             objectives: [O1], use_cases: [UC-X]} ]  # optional trace refs (E6)

architecture:                             # UNVALIDATED by the gate — input-stated
                                          # only; any value the input omits is TBD
  style: string
  components: [ {name, responsibility} ]
  deployment: string

screens: [ {id: SCR-X, name, use_cases: [UC-X]} ]

# academic profile extras
plan:
  phases: [ {name, from: YYYY-MM, to: YYYY-MM} ]   # contiguous + ordered (E7)
budget: [ {item, category, amount} ]      # amount: input-stated price only; else TBD
literature: [ {topic, source, relevance} ]  # source: a real citation the input gives — never fabricate
```

## Validation

`${CLAUDE_PLUGIN_ROOT}/scripts/validate_model.py <path>` — exit 0 clean,
exit 1 on errors. Errors block generation; warnings must be fixed or
explicitly accepted by the user; TBDs are reported, never invented away.
