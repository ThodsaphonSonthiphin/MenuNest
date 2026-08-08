# Workflow script template — study-design-verify

A ready-to-adapt skeleton for the Claude Code `Workflow` tool. Replace every `{{PLACEHOLDER}}`.
Everything here is generic; the comments mark the spots that carry the method's safety rules.

Two text blocks do the heavy lifting and are injected into every relevant agent prompt:

- **`CURRENT_STATE`** — the *verified* how-it-works-today story from Phase 0 (with file:line / attribute citations). Readers need it to know what matters; designers need it as the baseline; never make agents re-derive it.
- **`LIVE_RULES`** — the read-only contract for the live system: environment URL, auth recipe, "GET/SELECT only — no create/update/delete, hard rule", the evidence folder path, and every platform gotcha you already know (quirky filter support, encoding rules, pagination limits). Gotchas you withhold get rediscovered at fan-out prices.

```javascript
export const meta = {
  name: '{{kebab-name}}',
  description: '{{one line: study X + design how A should convert/map to B}}',
  phases: [
    { title: 'Study', detail: 'parallel readers over docs + live system + usage data' },
    { title: 'Design', detail: '3 independent design lenses' },
    { title: 'Feasibility', detail: 'adversarial check against live schema + code' },
  ],
}

const CURRENT_STATE = `{{verified current-state story, with citations}}`
const LIVE_RULES = `{{read-only contract: env URL, auth recipe, hard no-write rule,
evidence folder path, platform gotchas}}`

// ---------- Phase 1: Study ----------
const FACTS_SCHEMA = {
  type: 'object',
  properties: {
    summary: { type: 'string' },
    keyFindings: { type: 'array', items: { type: 'string' },
      description: 'load-bearing facts, each with its evidence (file:line, attribute name, or the query that produced the number)' },
    openQuestions: { type: 'array', items: { type: 'string' } },
  },
  required: ['summary', 'keyFindings'],
}

phase('Study')
// One reader per evidence source. The standard five — drop what doesn't exist, add what does:
//   business-context | target-side schema | source-side schema | usage-patterns | comparison-flow
// Prompts must contain: the pinned question, CURRENT_STATE, the exact paths/endpoints to read
// (scouted in Phase 0 — don't make readers guess), LIVE_RULES for live readers, FACTS_SCHEMA.
// Ask precise questions: "does {{target}} have ANY link back to {{source}} — list all FK/lookup
// attributes and their targets" beats "study the target schema".
const READERS = [
  { key: 'business-context',  prompt: `{{...}}` },
  { key: 'target-schema',     prompt: `{{...}} ${LIVE_RULES}` },
  { key: 'source-schema',     prompt: `{{...}} ${LIVE_RULES}` },
  { key: 'usage-patterns',    prompt: `{{aggregates + tiny samples only; every number with its query}} ${LIVE_RULES}` },
  { key: 'comparison-flow',   prompt: `{{how does the native/legacy/competing path do this same job?}}` },
]

const study = (await parallel(READERS.map(r => () =>
  agent(r.prompt, { label: 'study:' + r.key, phase: 'Study', schema: FACTS_SCHEMA })
    .then(res => ({ key: r.key, ...res }))
))).filter(Boolean)
log('Study done: ' + study.map(s => s.key).join(', '))

// Merge mechanically — no agent needed for a concatenation.
const digest = study.map(s =>
  '### ' + s.key + '\n' + s.summary +
  '\nKey findings:\n' + s.keyFindings.map(k => '- ' + k).join('\n') +
  (s.openQuestions?.length ? '\nOpen questions:\n' + s.openQuestions.map(q => '- ' + q).join('\n') : '')
).join('\n\n')

// ---------- Phase 2: Design panel ----------
const DESIGN_SCHEMA = {
  type: 'object',
  properties: {
    designName: { type: 'string' },
    philosophy: { type: 'string', description: 'one paragraph: what this design optimizes for' },
    fieldMapping: { type: 'array', items: { type: 'object', properties: {
      source: { type: 'string' },
      target: { type: 'string', description: 'existing element, NEW element proposal, or related record' },
      rule:   { type: 'string', description: 'exact transform rule — no hand-waving' },
      isNewSchema: { type: 'boolean' },
    }, required: ['source', 'target', 'rule', 'isNewSchema'] } },
    // One dedicated field per awkward sub-question the user actually asked
    // (e.g. "what happens to the surcharges?"). Forces every design to answer it head-on.
    {{specialTopic}}Handling: { type: 'string' },
    schemaChanges: { type: 'array', items: { type: 'string' } },
    codeChanges:   { type: 'array', items: { type: 'string' } },
    pros: { type: 'array', items: { type: 'string' } },
    cons: { type: 'array', items: { type: 'string' } },
    risks: { type: 'array', items: { type: 'string' }, description: 'what existing behavior could break' },
    effort: { type: 'string', description: 'S/M/L with one-line justification' },
  },
  required: ['designName', 'philosophy', 'fieldMapping', 'schemaChanges', 'codeChanges',
             'pros', 'cons', 'risks', 'effort'],
}

phase('Design')
// Same digest to all three; they cannot see each other. Rename lenses to fit the domain.
const LENSES = [
  { key: 'fidelity',   brief: 'FIDELITY-FIRST: no information entered upstream may be lost downstream; full traceability; accept schema growth but stay idiomatic to the existing model.' },
  { key: 'consumer',   brief: 'CONSUMER-FIRST: design for what the downstream process actually consumes ({{capacity math / billing / search / ...}}); map into the CORRECT existing buckets; reject new categories that break consumers.' },
  { key: 'pragmatic',  brief: 'MINIMAL-CHANGE PRAGMATIST: smallest change that removes the worst pain; rank losses by business pain USING THE LIVE NUMBERS from the study; prefer code-only over schema; propose a phased plan.' },
]

const designs = (await parallel(LENSES.map(l => () =>
  agent(`You are designing how {{source}} SHOULD convert/map to {{target}}. Perspective: ${l.brief}

${CURRENT_STATE}

STUDY FINDINGS:
${digest}

Design rules:
- Ground every mapping in elements the study proved exist, or explicitly mark isNewSchema.
- Exact transform rules; cover what is dropped, what is defaulted, idempotency/retries.
- {{the user's specific sub-questions}} get their own dedicated answers.
- You may read code/files to double-check, but do NOT touch the live system.`,
    { label: 'design:' + l.key, phase: 'Design', schema: DESIGN_SCHEMA })
))).filter(Boolean)

// ---------- Phase 3: Adversarial feasibility ----------
const FEAS_SCHEMA = {
  type: 'object',
  properties: {
    perDesign: { type: 'array', items: { type: 'object', properties: {
      designName: { type: 'string' },
      verifiedOk: { type: 'array', items: { type: 'string' } },
      problems:   { type: 'array', items: { type: 'string' },
        description: 'targets nonexistent elements, breaks existing consumers, contradicts live data, relies on unimplemented conventions — with evidence' },
      fixes:      { type: 'array', items: { type: 'string' } },
    }, required: ['designName', 'verifiedOk', 'problems'] } },
    crossCutting: { type: 'array', items: { type: 'string' },
      description: 'digest errors / facts ALL designs missed or got wrong' },
    recommendation: { type: 'string', description: 'which design or hybrid survives, with which fixes — 1 paragraph' },
  },
  required: ['perDesign', 'crossCutting', 'recommendation'],
}

phase('Feasibility')
const feasibility = await agent(
  `You are an adversarial feasibility reviewer. Attack each design below. Do NOT trust the
study digest — re-verify primary sources yourself: re-open the code, re-query the live
schema (read-only, per these rules: ${LIVE_RULES}).
Checklist: (1) does every referenced element exist? (2) what existing consumers break —
verify in code, not intuition; (3) do live distributions support each mapping?
(4) which documented conventions does the design rely on that the live system contradicts?
(5) is the named change-site where the behavior actually lives?
Then recommend the strongest design or hybrid.

DESIGNS:
${designs.map(d => JSON.stringify(d, null, 1)).join('\n\n---\n\n')}

STUDY DIGEST:
${digest}`,
  { label: 'feasibility:attack', phase: 'Feasibility', schema: FEAS_SCHEMA })

return { study, designs, feasibility }
```

## Adaptation notes

- **Fewer sources than five?** Run fewer readers. The phase matters, not the count.
- **Current state unknown?** Run a separate readers+verifier workflow *first* and feed its
  verified output in as `CURRENT_STATE`. Don't merge the two concerns into one run — designs
  built on an unverified current state inherit its errors twice.
- **`{{specialTopic}}Handling`** — replace with a real key (e.g. `surchargeHandling`,
  `auditTrailHandling`). One per sub-question the user explicitly asked; it's the difference
  between designs that answer the question and designs that answer *around* it.
- **Schema syntax note:** the `{{specialTopic}}Handling` line above is a placeholder — JSON
  keys can't be templated literally; rename the key before running.
- **Result handling:** the workflow returns `{ study, designs, feasibility }` — read
  `feasibility.recommendation` and `crossCutting` first, then the winning design's mapping,
  then synthesize per Phase 4 of the SKILL. Pull the full result from the task output file
  if the notification truncates it.
