# sa-doc professional profile

Additions to the core template for a work-deliverable SDD:

- After **7. Data & class model**: **Security design** — table from `security`
  (concern → control), REQUIRED non-empty (validator E8). Render each concern
  and control exactly as the model states it; add no mechanism the model does
  not name (no assumed hashing, no assumed gateway-vs-stored choice). A `control`
  of `TBD` renders as TBD — E8 requires the section to exist, not an invented
  mechanism.
- After **8. Architecture**: **Deployment view** — render the
  `architecture.deployment` string as-is, plus a `graph TD` of only the
  environments it names; if it names none, do not invent Dev/Staging/Prod tiers.
- At the end, before the traceability matrix: **Test-case seed** — one table
  row per use case main flow and per extension (id `TC-<uc>-<n>`, steps
  summary, expected result **from the use case's `postconditions` / matching
  `extensions.flow`; if neither states an outcome, `TBD` — never invent an
  assertion**). Close the section with a pointer: for the full evidence-grounded
  suite, run the `generating-test-cases` skill from this plugin.

Everything else follows the core template unchanged.
