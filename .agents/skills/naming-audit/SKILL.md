---
name: naming-audit
description: "Verify a source list of claimed labels/values/mappings (a naming audit, data dictionary, translation/i18n file, style guide, config, or any list of \"X should be Y\" findings) against a system's AUTHORITATIVE source of record — one item at a time. For each claim, trace the implementation label to the actual field/record it binds to, pull the authoritative value from the LIVE system (not from the source doc, which is often wrong or from the wrong entity), compare wording, and rule: source-of-truth wins. Stack-agnostic — implementation = web/app/code; authority = CRM/Dataverse, DB schema, API contract, design tokens, option sets. Output is a fixed per-item card with a verdict and the exact app+code path to check. Trigger on naming audit, label audit, wording/terminology check, \"does the Portal label match CRM\", \"compare these labels to the source of truth\", \"verify this naming spreadsheet\", field-name reconciliation, CRM-wins / canonical-label checks. Do NOT use for broad capability coverage (\"how far are we from X\", feature parity) — that's fit-gap-analysis; or for turning findings into tickets — that's the backlog plugins."
effort: max
---

# Naming Audit

Take a **source** — a list of claimed labels/values/mappings ("the Portal shows *X*, CRM says *Y*, rename to *Y*") — and verify each item against the **authoritative system of record**, one at a time. The output per item is a short card with a verdict and the exact path to check.

The whole skill rests on one discipline you must not skip: **trace the binding before you judge the wording.** A label is only "wrong" relative to the *specific field it actually writes to/reads from* — not relative to a similarly-named field elsewhere. The source doc's "authoritative" column is frequently from the wrong entity; do not trust it.

## When to invoke
"naming audit" · "label audit" · "check wording/terminology" · "does the Portal/app label match CRM/DB/API?" · "verify this naming spreadsheet" · "reconcile field names" · "is this the canonical label?" · "CRM/source-of-truth wins — fix the UI to match". Any time the user has a *list of specific claimed names/values* to check item-by-item.

**Not this skill:** broad "what's missing / how far are we" capability comparison → `fit-gap-analysis`. Turning the confirmed fixes into work items → the backlog plugins (`ado-backlog` / `github-backlog`).

## The three roles (name them first)
- **Source** — the document making claims (the audit sheet, dictionary, i18n file, finding list). Treated as *unverified hypotheses*, including its "authoritative" column.
- **Implementation** — where the label is shown (UI code, a report, an export). What you're auditing.
- **Authority (system of record)** — the live system that defines the correct value (CRM/Dataverse metadata, DB `information_schema`, API/OpenAPI contract, design-token source, option-set definitions). **This wins, always.**

## Core rule (the one the user taught)
For every claimed label:
1. **Find the entity + field** the implementation label binds to — the actual record/field this value is inserted into / read from. Trace it through the code/payload, not by matching a similar-sounding field.
2. **Compare the wording to *that* field's authoritative value.** If they differ → **change the implementation to match the authority. The authority always wins.**

## Per-item method
1. **Locate the implementation label** — literal string AND dynamic. Grep the literal first, then trace anything rendered from a variable, option set, static JSON snapshot, or a hardcoded override (these are invisible to literal grep — see Traps).
2. **Trace the binding** — which entity + field does this label map to? (backend mapper, RTK-Query field, payload key, `@odata.bind`, FK.) Write it down: `entity.field`.
3. **Pull the authoritative value from the LIVE system** — the field's display name, or the option-set's value→label pairs. Not from the source doc. Script it if it repeats.
4. **Compare** — for free-text labels, compare the display name. For dropdowns/enums, **match by VALUE (the integer/key) within the bound option set**, then compare labels — never label-hunt across option sets.
5. **Verdict + path** — render the card (below).

## Output format (use verbatim, one card per item)

```
## Item <N> — <field / label>

- **Source says:** "<claimed value>"
- **<Authority> says:** "<authoritative value>"   (<entity.field>)

**What I found:**
- <where the implementation shows it; the bound field>
- <wording comparison>

**Verdict:** <one of the four below>

**Where to check:**
- In the app: <navigation path to the screen/modal>
- In code: <file:line → file:line chain>
```

**Verdicts:**
- ✅ **Already correct** — implementation wording matches the authority. No change.
- 🔧 **Fix needed** — wording differs; change the implementation to the authoritative value. (Give the exact edit.)
- ⚪ **Not in implementation** — the field/value isn't surfaced anywhere (the source claim has no target). Confirm it exists in the authority, then mark N/A.
- ❓ **Needs decision** — binding is ambiguous, crosses entities, or the right target is a product call. State the specific question.

Always include the **Where to check** block — the user verifies in the running app, so give the navigation path, not just the file.

## Traps (hard-won — each has burned this audit before)
- **The source's "authoritative" column is often from the wrong entity.** A value in the quote flow must be checked against the *quote's* field, not a same-named field on another table. Trace, don't assume.
- **Literal grep is not enough.** Labels come from static JSON snapshots, option-set fetches, and hardcoded JSX overrides (e.g. `label={value === X ? "Vehicles" : opt.label}`). Trace render logic; check bundled `*.json` option snapshots against the live system (they drift).
- **Verify against the LIVE authority, not docs/exports/the source sheet.** They go stale.
- **Compare enums by value, not label.** The same word can be two different option-set members; match the integer/key within the bound field first.
- **"Not in implementation" must be earned** — prove the label is absent (grep literal + dynamic + the bound option set), don't infer from one pass.
- **Internal identifiers ≠ labels.** Variable/field/prop names (`portPairId`, `highHeavy`) are not user-facing; the authority-wins rule applies to *displayed* text only. Don't rename code identifiers.
- **Casing/qualifier nuance is a styling call** — surface it, let the user decide (e.g. CRM "HIGH & HEAVY" vs UI "High & Heavy").

## Recording dispositions back to the source
If the source is a spreadsheet, write each item's outcome back to it (don't overwrite the original claim columns — use/added columns): a reviewed flag, a verdict flag (e.g. matched / not-in-impl / fixed), and a short reason citing the bound field + live authoritative value. If the file is open/locked, ask the user to close it, then write. Keep reasons short and plain unless asked for code-level detail.

## If the output is requested as a document

The default output is per-item cards in chat plus spreadsheet write-back —
no diagrams. But if the user asks for the audit as a Markdown report file,
follow the diagram convention in
`${CLAUDE_PLUGIN_ROOT}/references/diagram-convention.md` (overview diagram of
verdict counts per area, as a `graph TD`; the cards stay as-is).

## After the audit
The 🔧 fixes are real change requests. Hand them off — apply directly for small label edits (then typecheck/build), or route the batch into the backlog plugins (`ado-backlog` / `github-backlog`) to create tickets. Keep ⚪ and ❓ items distinct from real fixes so the count of genuine work stays honest.

## Pace
Default to **one item at a time** and stop, unless the user asks for a batch. Lead with the verdict; keep cards short. If the user says they can't keep up, reset with a plain status (what's done, where you are) and slow to strictly one card per turn.
