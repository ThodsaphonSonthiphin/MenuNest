#!/usr/bin/env python3
"""validate_model.py — referential-integrity gate for sa-doc's sa-model.yaml.

Usage: python validate_model.py <path-to-sa-model.yaml>
Exit codes: 0 = clean (warnings allowed), 1 = errors, 2 = cannot run.
Every rule exists because the reviewed Project SA.pdf failed it.
"""
import sys

try:
    import yaml
except ImportError:  # pragma: no cover
    print("ERROR: PyYAML is required. Install with: pip install pyyaml")
    sys.exit(2)


class Report:
    def __init__(self):
        self.errors = []    # list[(rule, message)]
        self.warnings = []  # list[(rule, message)]
        self.tbds = []      # list[path-string]

    def error(self, rule, msg):
        self.errors.append((rule, msg))

    def warn(self, rule, msg):
        self.warnings.append((rule, msg))

    @property
    def ok(self):
        return not self.errors


def load_model(path):
    with open(path, encoding="utf-8") as f:
        return yaml.safe_load(f)


def _ids(items):
    return [i.get("id") for i in (items or [])]


def _check_duplicate_ids(model, report):
    """E6 — id uniqueness across every id-carrying list."""
    sections = {
        "actors": model.get("actors"),
        "use_cases": model.get("use_cases"),
        "entities": model.get("entities"),
        "screens": model.get("screens"),
        "nfrs": model.get("nfrs"),
        "security": model.get("security"),
        "scope": model.get("scope"),                # FR ids, when present
        "problem.current_problems": (model.get("problem") or {}).get("current_problems"),
        "problem.objectives": (model.get("problem") or {}).get("objectives"),
        "problem.benefits": (model.get("problem") or {}).get("benefits"),
    }
    for name, items in sections.items():
        ids = _ids(items)
        for dup in sorted({i for i in ids if i and ids.count(i) > 1}):
            report.error("E6", f"duplicate id '{dup}' in {name}")


def _check_dangling_refs(model, report):
    """E6 — dangling id references anywhere (uniqueness is _check_duplicate_ids).

    Deliberately excludes refs already validated elsewhere: actors (E1), scope
    -> use_cases (E2), fk targets (E3), step field refs (E4), transition uc
    (E5).
    """
    problem = model.get("problem") or {}
    current_problem_ids = set(_ids(problem.get("current_problems")))
    objective_ids = set(_ids(problem.get("objectives")))
    entity_ids = set(_ids(model.get("entities")))
    screen_ids = set(_ids(model.get("screens")))
    use_case_ids = set(_ids(model.get("use_cases")))

    for obj in problem.get("objectives") or []:
        for p in obj.get("problems") or []:
            if p not in current_problem_ids:
                report.error("E6", f"objective {obj.get('id')} references "
                                   f"unknown problem '{p}'")

    for ben in problem.get("benefits") or []:
        for o in ben.get("objectives") or []:
            if o not in objective_ids:
                report.error("E6", f"benefit {ben.get('id')} references "
                                   f"unknown objective '{o}'")

    for uc in model.get("use_cases") or []:
        for o in uc.get("objectives") or []:
            if o not in objective_ids:
                report.error("E6", f"{uc.get('id')} references unknown "
                                   f"objective '{o}'")
        for e in uc.get("entities") or []:
            if e not in entity_ids:
                report.error("E6", f"{uc.get('id')} references unknown "
                                   f"entity '{e}'")
        for s in uc.get("screens") or []:
            if s not in screen_ids:
                report.error("E6", f"{uc.get('id')} references unknown "
                                   f"screen '{s}'")

    for sc in model.get("screens") or []:
        for u in sc.get("use_cases") or []:
            if u not in use_case_ids:
                report.error("E6", f"screen {sc.get('id')} references "
                                   f"unknown use case '{u}'")

    # NFRs and security controls may optionally trace to the objectives and use
    # cases they support (surfaced in the traceability matrix). Dangling refs
    # here are the same cross-artifact rot E6 exists to catch.
    for nfr in model.get("nfrs") or []:
        for o in nfr.get("objectives") or []:
            if o not in objective_ids:
                report.error("E6", f"{nfr.get('id')} references unknown "
                                   f"objective '{o}'")
        for u in nfr.get("use_cases") or []:
            if u not in use_case_ids:
                report.error("E6", f"{nfr.get('id')} references unknown "
                                   f"use case '{u}'")
    for sec in model.get("security") or []:
        for o in sec.get("objectives") or []:
            if o not in objective_ids:
                report.error("E6", f"{sec.get('id')} references unknown "
                                   f"objective '{o}'")
        for u in sec.get("use_cases") or []:
            if u not in use_case_ids:
                report.error("E6", f"{sec.get('id')} references unknown "
                                   f"use case '{u}'")

    for group in model.get("states") or []:
        declared = set(group.get("states") or [])
        for t in group.get("transitions") or []:
            frm, to = t.get("from"), t.get("to")
            if frm not in declared:
                report.error("E6", f"transition references unknown state "
                                   f"'{frm}' (not in {group.get('entity')}."
                                   f"{group.get('field')} states)")
            if to not in declared:
                report.error("E6", f"transition references unknown state "
                                   f"'{to}' (not in {group.get('entity')}."
                                   f"{group.get('field')} states)")


def _check_actor_refs(model, actors, report):
    """E1 — every referenced actor exists."""
    for uc in model.get("use_cases") or []:
        for a in uc.get("actors") or []:
            if a not in actors:
                report.error("E1", f"{uc.get('id')} references unknown actor '{a}'")
        for step in uc.get("main_flow") or []:
            a = step.get("actor")
            if a and a not in actors:
                report.error("E1", f"{uc.get('id')} step {step.get('step')} "
                                   f"references unknown actor '{a}'")
    for sc in model.get("scope") or []:
        if sc.get("actor") not in actors:
            report.error("E1", f"scope '{sc.get('capability')}' references "
                               f"unknown actor '{sc.get('actor')}'")


def _check_scope_use_cases(model, use_cases, report):
    """E2 — every scope capability lists >= 1 existing use case."""
    for sc in model.get("scope") or []:
        refs = sc.get("use_cases") or []
        cap = sc.get("capability")
        if not refs:
            report.error("E2", f"scope capability '{cap}' lists no use case")
        for u in refs:
            if u not in use_cases:
                report.error("E2", f"scope capability '{cap}' references "
                                   f"unknown use case '{u}'")


BOOL_TYPES = {"bool", "boolean", "bit"}
VALID_LANGS = {"th", "en"}
VALID_PROFILES = {"academic", "professional"}
REL_TYPES = {"association", "aggregation", "composition",
             "generalization", "dependency", "realization"}


def _check_meta(model, report):
    """E9 — meta present with a concrete, valid language and profile.

    profile is what selects the professional security gate (E8) and the whole
    template; a typo like 'profesional' used to slip through and silently
    produce a professional document with no security section. language and
    profile are decisions, not leaves — TBD is not acceptable for them.
    """
    meta = model.get("meta")
    if not isinstance(meta, dict):
        report.error("E9", "meta block is missing")
        return
    project = meta.get("project")
    if not project or str(project).strip().lower() == "tbd":
        report.error("E9", "meta.project is required (it names the output files)")
    # isinstance guard first: a malformed scalar-as-list (language: [th]) would
    # otherwise raise TypeError on the set membership and crash the gate whose
    # whole job is to report bad meta cleanly.
    lang = meta.get("language")
    if not isinstance(lang, str) or lang not in VALID_LANGS:
        report.error("E9", f"meta.language must be one of "
                           f"{sorted(VALID_LANGS)}, got {lang!r}")
    profile = meta.get("profile")
    if not isinstance(profile, str) or profile not in VALID_PROFILES:
        report.error("E9", f"meta.profile must be one of "
                           f"{sorted(VALID_PROFILES)}, got {profile!r}")


def _check_relationships(model, entity_ids, report):
    """E10 — explicit class-model relationships reference real entities and use
    a known UML relationship type."""
    for rel in model.get("relationships") or []:
        for end in ("from", "to"):
            eid = rel.get(end)
            if not isinstance(eid, str) or eid not in entity_ids:
                report.error("E10", f"relationship '{end}' references unknown "
                                    f"entity '{eid}'")
        t = rel.get("type")
        # TBD is a legal, inventoried value for any leaf (see the contract); an
        # in-progress relationship type must not block generation.
        if t and str(t).lower() != "tbd" and str(t).lower() not in REL_TYPES:
            report.error("E10", f"relationship {rel.get('from')}->"
                                f"{rel.get('to')} has unknown type '{t}' "
                                f"(expected one of {sorted(REL_TYPES)})")


def _fields_by_entity(model):
    return {e.get("id"): {f.get("name") for f in e.get("fields") or []}
            for e in model.get("entities") or []}


def _check_fk_targets(model, report):
    """E3 — every fk targets an existing entity.field."""
    fields = _fields_by_entity(model)
    for ent in model.get("entities") or []:
        for f in ent.get("fields") or []:
            fk = f.get("fk")
            if not fk:
                continue
            target_entity, _, target_field = str(fk).partition(".")
            if target_entity not in fields or target_field not in fields[target_entity]:
                report.error("E3", f"{ent.get('id')}.{f.get('name')} fk '{fk}' "
                                   f"targets a non-existent field")


def _check_step_field_refs(model, report):
    """E4 — step field refs exist on the use case's entities; extension
    at_step anchors an existing main_flow step."""
    fields = _fields_by_entity(model)
    for uc in model.get("use_cases") or []:
        allowed = set()
        for eid in uc.get("entities") or []:
            allowed |= {f"{eid}.{name}" for name in fields.get(eid, set())}
        steps = (uc.get("main_flow") or []) + (uc.get("extensions") or [])
        for step in steps:
            for ref in step.get("fields") or []:
                if ref not in allowed:
                    report.error("E4", f"{uc.get('id')} references '{ref}' which "
                                       f"is not on its entities")
        step_numbers = {s.get("step") for s in uc.get("main_flow") or []}
        for ext in uc.get("extensions") or []:
            at_step = ext.get("at_step")
            if at_step not in step_numbers:
                report.error("E4", f"{uc.get('id')} extension at_step "
                                   f"{at_step!r} does not match any "
                                   f"main_flow step")


def _check_states(model, entities, use_cases, report):
    """E5 — state groups are storable and transitions reference real use cases."""
    for group in model.get("states") or []:
        eid, fname = group.get("entity"), group.get("field")
        ent = entities.get(eid)
        if ent is None:
            report.error("E5", f"states group references unknown entity '{eid}'")
            continue
        field = next((f for f in ent.get("fields") or []
                      if f.get("name") == fname), None)
        if field is None:
            report.error("E5", f"states group for {eid} names missing field '{fname}'")
            continue
        n_states = len(group.get("states") or [])
        if str(field.get("type", "")).lower() in BOOL_TYPES and n_states > 2:
            report.error("E5", f"{eid}.{fname} is boolean but must hold "
                               f"{n_states} states")
        for t in group.get("transitions") or []:
            uc = t.get("uc")
            if uc and uc not in use_cases:
                report.error("E5", f"transition {t.get('from')}->{t.get('to')} "
                                   f"references unknown use case '{uc}'")


def _month_index(ym):
    year, _, month = str(ym).partition("-")
    month = int(month)
    if month < 1 or month > 12:
        raise ValueError(f"month out of range 1-12: {month}")
    return int(year) * 12 + month


def _check_plan(model, report):
    """E7 — plan phases: from <= to, ordered, no gap between phases."""
    phases = (model.get("plan") or {}).get("phases") or []
    prev_to = None
    for ph in phases:
        try:
            start, end = _month_index(ph.get("from")), _month_index(ph.get("to"))
        except (ValueError, TypeError):
            report.error("E7", f"phase '{ph.get('name')}' has a malformed month")
            continue
        if start > end:
            report.error("E7", f"phase '{ph.get('name')}' starts after it ends")
        if prev_to is not None and start > prev_to + 1:
            report.error("E7", f"phase '{ph.get('name')}' leaves a gap in the plan")
        if prev_to is not None and start < prev_to - 12:
            report.error("E7", f"phase '{ph.get('name')}' jumps backwards")
        prev_to = max(end, prev_to) if prev_to is not None else end


def _check_profile(model, report):
    """E8 — professional profile requires a non-empty security section."""
    profile = (model.get("meta") or {}).get("profile")
    if profile == "professional" and not model.get("security"):
        report.error("E8", "profile=professional requires a non-empty "
                           "security section")


MONEY_HINTS = ("amount", "price", "total", "cost", "salary")
TRIGGER_PREFIXES = ("กด", "คลิก", "ป้อน", "click", "press", "enter", "select")


def _check_coverage(model, report):
    """W1 — orphan use cases / uncovered objectives. W2 — screen coverage."""
    scoped = {u for sc in model.get("scope") or [] for u in sc.get("use_cases") or []}
    covered_objectives = {o for uc in model.get("use_cases") or []
                          for o in uc.get("objectives") or []}
    screens_with_uc = {s.get("id") for s in model.get("screens") or []
                       if s.get("use_cases")}
    ucs_with_screen = {u for s in model.get("screens") or []
                       for u in s.get("use_cases") or []}
    for uc in model.get("use_cases") or []:
        if uc.get("id") not in scoped:
            report.warn("W1", f"{uc.get('id')} has no scope capability pointing at it")
        if uc.get("id") not in ucs_with_screen and not uc.get("screens"):
            report.warn("W2", f"{uc.get('id')} has no screen")
    for obj in (model.get("problem") or {}).get("objectives") or []:
        if obj.get("id") not in covered_objectives:
            report.warn("W1", f"objective {obj.get('id')} has no use case")
    for s in model.get("screens") or []:
        if s.get("id") not in screens_with_uc:
            report.warn("W2", f"screen {s.get('id')} is linked to no use case")


def _check_money_types(model, report):
    """W3 — money-hinted fields must share one type."""
    seen = {}
    for ent in model.get("entities") or []:
        for f in ent.get("fields") or []:
            name = str(f.get("name", "")).lower()
            if any(h in name for h in MONEY_HINTS):
                seen[f"{ent.get('id')}.{f.get('name')}"] = str(f.get("type", "")).lower()
    if len(set(seen.values())) > 1:
        detail = ", ".join(f"{k}:{v}" for k, v in sorted(seen.items()))
        report.warn("W3", f"money fields use inconsistent types ({detail})")


def _check_postconditions(model, report):
    """W4 — postconditions must be guarantees, not triggers."""
    for uc in model.get("use_cases") or []:
        posts = uc.get("postconditions") or []
        if not posts:
            report.warn("W4", f"{uc.get('id')} has no postcondition")
        for p in posts:
            if str(p).strip().lower().startswith(TRIGGER_PREFIXES):
                report.warn("W4", f"{uc.get('id')} postcondition '{p}' is a "
                                  f"trigger, not a guaranteed state")


def _check_copy_paste(model, report):
    """W5 — identical extension text across >= 3 use cases."""
    seen = {}
    for uc in model.get("use_cases") or []:
        for ext in uc.get("extensions") or []:
            key = str(ext.get("flow", "")).strip()
            if key:
                seen.setdefault(key, set()).add(uc.get("id"))
    for text, ucs in seen.items():
        if len(ucs) >= 3:
            report.warn("W5", f"extension text repeated in {len(ucs)} use cases "
                              f"({', '.join(sorted(ucs))}): '{text[:60]}...'")


def _check_field_shapes(model, report):
    """W6 — empty entities; sample values exceeding declared size."""
    for ent in model.get("entities") or []:
        if not ent.get("fields"):
            report.warn("W6", f"entity {ent.get('id')} has zero fields")
        for f in ent.get("fields") or []:
            sample, size = f.get("sample"), f.get("size")
            if (sample and size and "string" in str(f.get("type", "")).lower()
                    and len(str(sample)) > int(size)):
                report.warn("W6", f"{ent.get('id')}.{f.get('name')} sample is "
                                  f"{len(str(sample))} chars but size is {size}")


def _check_entity_coverage(model, report):
    """W7 — an entity no use case touches is drawn in the ER/class diagram but
    never appears in the traceability matrix: exactly the kind of orphan the
    model is meant to make impossible."""
    used = {e for uc in model.get("use_cases") or []
            for e in uc.get("entities") or []}
    for ent in model.get("entities") or []:
        if ent.get("id") not in used:
            report.warn("W7", f"entity {ent.get('id')} is touched by no use "
                              f"case (orphan in the data/class model)")


def _check_primary_keys(model, report):
    """W8 — an entity with fields but no primary key cannot be keyed in the
    data dictionary or drawn as a PK in the ER diagram."""
    for ent in model.get("entities") or []:
        fields = ent.get("fields") or []
        if fields and not any(f.get("pk") for f in fields):
            report.warn("W8", f"entity {ent.get('id')} has fields but no "
                              f"primary key (mark one field pk: true)")


def _check_nfr_metrics(model, report):
    """W9 — a non-functional requirement with no metric cannot be verified;
    the requirements section asks for a measurable target."""
    for nfr in model.get("nfrs") or []:
        metric = nfr.get("metric")
        if metric is None or str(metric).strip() == "":
            report.warn("W9", f"{nfr.get('id')} has no measurable metric")


def _collect_tbds(node, path, out):
    if isinstance(node, dict):
        for k, v in node.items():
            _collect_tbds(v, f"{path}.{k}" if path else str(k), out)
    elif isinstance(node, list):
        for i, v in enumerate(node):
            _collect_tbds(v, f"{path}[{i}]", out)
    elif isinstance(node, str) and node.strip().lower() == "tbd":
        out.append(path)


def validate(model):
    report = Report()
    actors = {a.get("id") for a in model.get("actors") or []}
    use_cases = {u.get("id") for u in model.get("use_cases") or []}
    entities = {e.get("id"): e for e in model.get("entities") or []}
    _check_meta(model, report)
    _check_duplicate_ids(model, report)
    _check_dangling_refs(model, report)
    _check_actor_refs(model, actors, report)
    _check_scope_use_cases(model, use_cases, report)
    _check_fk_targets(model, report)
    _check_step_field_refs(model, report)
    _check_states(model, entities, use_cases, report)
    _check_relationships(model, set(entities), report)
    _check_plan(model, report)
    _check_profile(model, report)
    _check_coverage(model, report)
    _check_entity_coverage(model, report)
    _check_primary_keys(model, report)
    _check_nfr_metrics(model, report)
    _check_money_types(model, report)
    _check_postconditions(model, report)
    _check_copy_paste(model, report)
    _check_field_shapes(model, report)
    _collect_tbds(model, "", report.tbds)
    return report


def print_report(report):
    for rule, msg in report.errors:
        print(f"ERROR   {rule}: {msg}")
    for rule, msg in report.warnings:
        print(f"WARNING {rule}: {msg}")
    if report.tbds:
        print(f"TBD ({len(report.tbds)}):")
        for path in report.tbds:
            print(f"  - {path}")
    print(f"{len(report.errors)} error(s), {len(report.warnings)} warning(s), "
          f"{len(report.tbds)} TBD(s)")


def main(argv):
    # error/warning messages quote Thai model text; force UTF-8 so a Windows
    # cp1252 console does not crash while reporting a finding.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):  # pragma: no cover
        pass
    if len(argv) != 2:
        print(__doc__)
        return 2
    report = validate(load_model(argv[1]))
    print_report(report)
    return 0 if report.ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
