# sa-doc academic profile

Additions to the core template for a course-report shaped document
(insert positions relative to the core skeleton):

- After **1. ที่มาและปัญหา**: **แผนการดำเนินงาน** — phase list + a Markdown
  Gantt-style table from `plan.phases` (validated contiguous by E7);
  **งบประมาณ** — table from `budget` with a total row (sum only stated amounts;
  if any `amount` is `TBD`, show the total as `TBD` — never treat `TBD` as 0);
  **ประโยชน์ที่คาดว่าจะได้รับ** — from `problem.benefits` with B→O links stated.
- After the plan sections: **วรรณกรรมที่เกี่ยวข้อง** — one subsection per
  `literature` entry: topic, source, and the *relevance* sentence taken from
  `literature.relevance` as written — do not synthesize a design-link the model
  does not state, and never paste vendor marketing text.
- At the end: **บรรณานุกรม** — every `literature.source` rendered verbatim in a
  citation list; do not expand a terse source into an invented
  author/year/publisher.

Everything else follows the core template unchanged.
