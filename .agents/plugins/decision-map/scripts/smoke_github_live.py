#!/usr/bin/env python3
r"""smoke_github_live.py — run the GitHub backend against the real API, once.

`test_github_map_ops.py` drives an in-memory fake, which is where all the
interesting logic is tested. This exists to answer the one question a fake
cannot: **is the fake faithful?** It charts a small map for real, exercises
every subcommand, and then asserts the property that no offline test can
establish — that a re-chart against live GitHub round-trips **byte-identically**,
which is what proves `norm_eol`, the region merge and the plan all agree with
what the API actually returns rather than with what the fake pretends.

DESTRUCTIVE and deliberately awkward to run:

    python smoke_github_live.py --repo OWNER/REPO --yes [--keep]

It creates issues in that repo and deletes them again in a `finally`. `--repo`
is never inferred and `--yes` is required, because the failure mode of a script
like this is charting a throwaway map into somebody's real backlog. Use a
private throwaway repo you own.
"""
import argparse
import json
import sys
import traceback

import github_map_ops as gh
import map_core

SLUG = "dm-smoke"

INPUT = {
    "target": {"slug": SLUG},
    "map": {
        "title": "decision-map live smoke (delete me)",
        "destination": "prove the GitHub backend works against the real API",
        "notes": "created by smoke_github_live.py; safe to delete",
        "notYetSpecified": ["whether the fake is faithful"],
        "outOfScope": ["the ADO backend"],
    },
    "tickets": [
        {"key": "first-decision", "title": "First decision — does the join hold?",
         "type": "grilling", "question": "does the key marker survive?",
         "blocks": ["second-decision"]},
        {"key": "second-decision", "title": "Second decision — blocked by the first",
         "type": "task", "question": "does the dependency read back?"},
    ],
}

CHECKS = []


def check(name, ok, detail=""):
    CHECKS.append({"check": name, "ok": bool(ok), "detail": str(detail)[:400]})
    print(f"  {'PASS' if ok else 'FAIL'}  {name}"
          + (f"   {detail}" if detail and not ok else ""))
    return ok


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--repo", required=True, help="OWNER/REPO — a throwaway you own")
    ap.add_argument("--yes", action="store_true",
                    help="required: this creates and deletes real issues")
    ap.add_argument("--keep", action="store_true",
                    help="leave the issues behind for inspection")
    a = ap.parse_args()
    if not a.yes:
        print("refusing to run without --yes: this writes real issues", file=sys.stderr)
        return 2

    api = gh.GhApi()
    ops = gh.GitHubOps(api, a.repo)
    created = []

    try:
        print("1. chart --dry-run (must write nothing)")
        plan = gh.chart(ops, json.loads(json.dumps(INPUT)), real=False)
        check("dry run reports a plan", plan.get("dryRun") is True)
        check("plan names the map and both tickets",
              {"<map>", "first-decision", "second-decision"}
              <= {e["path"] for e in plan["planned"]})
        # Every label the run would create must be announced -- and NOT one it
        # would not. A previous smoke run leaves the labels behind (labels are
        # repo-wide and are never deleted), so on a repo that has been smoked
        # before the correct plan announces none. Asserting "at least one label
        # entry" was wrong for exactly that case: it failed on a re-run while the
        # backend was right.
        have = ops.existing_labels()
        want = {gh.MAP_LABEL, gh.TICKET_LABEL} | {
            gh.TYPE_LABEL % t["type"] for t in INPUT["tickets"]}
        announced = {e["path"][len("label:"):] for e in plan["planned"]
                     if e["path"].startswith("label:")}
        check("the plan announces exactly the labels that do not exist yet",
              announced == (want - have),
              f"announced={sorted(announced)} expected={sorted(want - have)}")

        print("2. chart --real")
        out = gh.chart(ops, json.loads(json.dumps(INPUT)), real=True)
        map_number = int(out["map"]["id"])
        created = [map_number] + [int(t["id"]) for t in out["tickets"]]
        check("map came back with our slug as its key", out["map"]["key"] == SLUG)
        check("both tickets exist, key-ascending",
              [t["key"] for t in out["tickets"]]
              == ["first-decision", "second-decision"])
        second = next(t for t in out["tickets"] if t["key"] == "second-decision")
        check("the native dependency read back as a key",
              second["blockedBy"] == ["first-decision"], second["blockedBy"])
        check("id and dbId are both present and different",
              second["dbId"] != int(second["id"]))

        print("3. the byte-identical no-op — the whole point of this script")
        before = {n: ops.api.rest(f"repos/{a.repo}/issues/{n}")["body"]
                  for n in created}
        again = gh.chart(ops, json.loads(json.dumps(INPUT)), real=True)
        after = {n: ops.api.rest(f"repos/{a.repo}/issues/{n}")["body"]
                 for n in created}
        check("an identical re-chart changed no byte of any body",
              before == after,
              [n for n in before if before[n] != after[n]])
        check("an identical re-chart reported no divergence",
              again["divergence"] == [], again["divergence"])

        print("4. frontier")
        f = gh.frontier(ops, SLUG)
        check("the blocker is on the frontier",
              [e["key"] for e in f["frontier"]] == ["first-decision"], f)
        check("the blocked ticket is blocked",
              [e["key"] for e in f["blocked"]] == ["second-decision"], f)
        check("frontier ids match map.json ids for the same ticket",
              all(e["id"] == {t["key"]: t["id"] for t in out["tickets"]}[e["key"]]
                  for e in f["frontier"] + f["blocked"]), f)

        print("5. claim, then release")
        me = api.whoami()
        gh.claim(ops, SLUG, "second-decision", me)
        f = gh.frontier(ops, SLUG)
        check("a claimed-and-blocked ticket lands in claimed only",
              [e["key"] for e in f["claimed"]] == ["second-decision"]
              and not f["blocked"], f)
        check("the assignee is a real login, never the literal 'me'",
              f["claimed"][0]["assignee"] == me, f["claimed"])
        gh.claim(ops, SLUG, "second-decision", "")

        print("6. resolve")
        gh.resolve(ops, SLUG, "first-decision",
                   "the marker survives and the join holds",
                   "docs/adr/0062-decision-map-github-backend.md",
                   "## Detail\n\nrecorded by the live smoke test")
        m = gh.read_map(ops, SLUG)
        first = next(t for t in m["tickets"] if t["key"] == "first-decision")
        check("the ticket closed", first["status"] == "closed", first)
        check("the gist round-tripped out of its region",
              first["gist"] == "the marker survives and the join holds",
              first["gist"])
        map_body = ops.api.rest(f"repos/{a.repo}/issues/{map_number}")["body"]
        idx = map_core.region_body(map_core.norm_eol(map_body),
                                   map_core.DECISIONS_START,
                                   map_core.DECISIONS_END)
        check("the decisions index was projected into the map",
              "the marker survives and the join holds" in (idx or ""), idx)
        check("the index links to the issue, not a page fragment",
              "/issues/" in (idx or "") and "](#" not in (idx or ""), idx)

        print("7. resolving the blocker released its dependent")
        f = gh.frontier(ops, SLUG)
        check("the dependent is now actionable",
              [e["key"] for e in f["frontier"]] == ["second-decision"], f)

        print("8. a comment, and block() on an edge that already exists")
        gh.comment(ops, SLUG, "second-decision", "a plain note from the smoke test")
        edge = gh.block(ops, SLUG, "second-decision", "first-decision")
        by_number = gh.claim(ops, SLUG, second["id"], me)
        gh.claim(ops, SLUG, second["id"], "")
        check("--ticket accepts the issue number map.json reported",
              by_number["claimed"] == "second-decision", by_number)
        check("block on an existing edge returns it without a write",
              edge["blockedBy"] == ["first-decision"], edge)

        print("9. resolution comment is on the ticket, not the map")
        first_number = int(first["id"])
        comments = ops.api.rest(f"repos/{a.repo}/issues/{first_number}/comments")
        check("the human-facing resolution is a native comment",
              any("## Resolution" in (c.get("body") or "") for c in comments),
              len(comments))

    except Exception:
        traceback.print_exc()
        check("no unhandled exception", False, "see traceback above")
    finally:
        if created and not a.keep:
            print("cleanup: deleting the issues this run created")
            for n in created:
                try:
                    node = ops.api.rest(f"repos/{a.repo}/issues/{n}")["node_id"]
                    api.graphql(
                        "mutation($id:ID!){deleteIssue(input:{issueId:$id})"
                        "{clientMutationId}}", {"id": node})
                    print(f"  deleted #{n}")
                except Exception as e:      # closing is the fallback
                    print(f"  could not delete #{n} ({e}); closing instead")
                    try:
                        ops.patch_issue(n, {"state": "closed"})
                    except Exception:
                        pass
        elif created:
            print(f"kept: issues #{', #'.join(str(n) for n in created)}")

    failed = [c for c in CHECKS if not c["ok"]]
    print(f"\n{len(CHECKS) - len(failed)}/{len(CHECKS)} checks passed; "
          f"{api.calls} API calls")
    print(json.dumps({"repo": a.repo, "apiCalls": api.calls,
                      "passed": len(CHECKS) - len(failed),
                      "total": len(CHECKS),
                      "failed": failed}, indent=2))
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main())
