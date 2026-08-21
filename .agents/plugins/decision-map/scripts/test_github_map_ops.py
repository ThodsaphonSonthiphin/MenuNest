#!/usr/bin/env python3
r"""test_github_map_ops.py — the GitHub backend, against an in-memory API.

The transport is injected (see fake_github.py, whose every behaviour was
verified live before being written), so these tests cover the parts that are
actually this backend's job: the key join and its loud failures, the additive
plan, the byte-identical no-op, bucket precedence, the escaping invariant, and
the call budgets the contract asks to be written down.

Each test names the harm it guards against, not just the behaviour it asserts.
"""
import copy
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
import contextlib
from pathlib import Path

import github_map_ops as gh
import local_map_ops
import map_core
from fake_github import FakeGitHub

REPO = "acme/widgets"

INPUT = {
    "target": {"owner": "acme", "repo": "widgets", "slug": "billing"},
    "map": {
        "title": "Decision map — billing",
        "destination": "ship metered billing without breaking existing plans",
        "notes": "consult the pricing skill",
        "notYetSpecified": ["how to migrate legacy plans"],
        "outOfScope": ["multi-currency"],
    },
    "tickets": [
        {"key": "auth-model", "title": "Auth model — shared or per-tenant keys?",
         "type": "grilling", "question": "which key scheme?",
         "blocks": ["rollout-order"]},
        {"key": "rollout-order", "title": "Rollout order",
         "type": "task", "question": "what order do tenants migrate in?"},
    ],
}


def ops_for(fake):
    return gh.GitHubOps(fake, REPO)


@contextlib.contextmanager
def captured_stderr():
    buf = io.StringIO()
    with contextlib.redirect_stderr(buf):
        yield buf


class Base(unittest.TestCase):
    def setUp(self):
        self.fake = FakeGitHub(repo=REPO)
        self.ops = ops_for(self.fake)

    def chart(self, inp=None, **kw):
        with captured_stderr():
            return gh.chart(self.ops, copy.deepcopy(inp or INPUT), real=True, **kw)

    def dry(self, inp=None, **kw):
        with captured_stderr() as err:
            out = gh.chart(self.ops, copy.deepcopy(inp or INPUT), real=False, **kw)
        return out, err.getvalue()

    def map_number(self):
        return int(gh.read_map(self.ops, "billing")["map"]["id"])


# ---------------------------------------------------------------------------
class TestChartCreates(Base):
    def test_initial_chart_creates_map_tickets_labels_and_the_edge(self):
        out = self.chart()
        self.assertEqual(out["backend"], "github")
        self.assertEqual([t["key"] for t in out["tickets"]],
                         ["auth-model", "rollout-order"])
        # the map issue carries the map label and its own key marker
        m = self.fake.issue(int(out["map"]["id"]))
        self.assertIn(gh.MAP_LABEL, m["labels"])
        self.assertIn(map_core.KEY_MARKER % "billing", m["body"])
        # both tickets are real sub-issues of it, with type labels
        self.assertEqual(len(self.fake.children_of(m["number"])), 2)
        auth = next(t for t in out["tickets"] if t["key"] == "auth-model")
        self.assertIn(gh.TYPE_LABEL % "grilling",
                      self.fake.issue(int(auth["id"]))["labels"])
        # direction: the input says auth-model BLOCKS rollout-order, so it is
        # rollout-order that comes back blockedBy auth-model
        rollout = next(t for t in out["tickets"] if t["key"] == "rollout-order")
        self.assertEqual(rollout["blockedBy"], ["auth-model"])
        self.assertEqual(auth["blockedBy"], [])

    def test_map_json_carries_both_handles_because_mutations_need_the_db_id(self):
        """`id` alone is not enough: every sub-issue and dependency mutation is
        keyed on the database id, which is an unrelated value. Emitting only one
        of the two costs an extra resolve call per write."""
        out = self.chart()
        for t in out["tickets"]:
            issue = self.fake.issue(int(t["id"]))
            self.assertEqual(t["dbId"], issue["id"])
            self.assertNotEqual(t["dbId"], int(t["id"]),
                                "db id must not be mistakable for the number")

    def test_mode_is_derived_from_type(self):
        out = self.chart()
        by_key = {t["key"]: t for t in out["tickets"]}
        self.assertEqual(by_key["auth-model"]["mode"], "HITL")
        self.assertEqual(by_key["rollout-order"]["mode"], "HITL")
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [{"key": "survey", "title": "Survey", "type": "research",
                           "question": "what exists?"}]
        inp["target"] = dict(inp["target"], slug="research-map")
        out2 = self.chart(inp)
        self.assertEqual(out2["tickets"][0]["mode"], "AFK")


class TestDryRun(Base):
    def test_dry_run_writes_nothing_and_lists_labels_map_and_tickets(self):
        out, err = self.dry()
        self.assertEqual(self.fake.write_count, 0,
                         "a dry run must not write, ever")
        self.assertTrue(out["dryRun"])
        paths = [e["path"] for e in out["planned"]]
        self.assertIn("<map>", paths)
        self.assertIn("auth-model", paths)
        # Label provisioning is repo-wide and IS a write, so the gate must show
        # it. Leaving it out contradicts "nothing the run writes may be missing".
        self.assertIn(f"label:{gh.MAP_LABEL}", paths)
        self.assertIn(f"label:{gh.TYPE_LABEL % 'grilling'}", paths)
        self.assertTrue(all(e["action"] == "create" for e in out["planned"]),
                        f"everything is new here: {out['planned']}")
        self.assertIn("DRY RUN", err)

    def test_dry_run_plan_is_json_on_stdout_and_human_on_stderr(self):
        """`chart --input x | jq` must work, so stdout carries JSON or nothing."""
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "in.json"
            p.write_text(json.dumps(INPUT), encoding="utf-8")
            buf, err = io.StringIO(), io.StringIO()
            with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(err):
                rc = gh.main(["chart", "--repo", REPO, "--input", str(p)],
                             api=self.fake)
            self.assertEqual(rc, 0)
            json.loads(buf.getvalue())          # parses, so nothing leaked in
            self.assertIn("DRY RUN", err.getvalue())

    def test_every_merge_entry_names_what_it_adds(self):
        """A `merge` with detail None asks the user to approve an unnamed write."""
        self.chart()
        inp = copy.deepcopy(INPUT)
        inp["map"]["notYetSpecified"] = ["how to migrate legacy plans", "pricing tiers"]
        out, _ = self.dry(inp)
        merges = [e for e in out["planned"] if e["action"] == "merge"]
        self.assertTrue(merges, f"expected a map merge: {out['planned']}")
        for e in merges:
            self.assertTrue(e["detail"], f"merge with no detail: {e}")

    def test_skip_exists_writes_nothing_on_the_real_run_either(self):
        self.chart()
        self.fake.reset_counters()
        self.chart()
        self.assertEqual(self.fake.write_count, 0,
                         "an identical re-chart must be a no-op, not merely "
                         "equivalent -- every write here would show in the "
                         "issue timeline of a shared tracker")


class TestAdditive(Base):
    def test_rechart_with_a_new_fog_line_merges_the_map_and_touches_no_ticket(self):
        self.chart()
        inp = copy.deepcopy(INPUT)
        inp["map"]["notYetSpecified"] = ["how to migrate legacy plans", "pricing tiers"]
        self.fake.reset_counters()
        self.chart(inp)
        patched = [w for w in self.fake.writes if w[0] == "PATCH"]
        self.assertEqual(len(patched), 1, f"only the map body: {self.fake.writes}")
        body = self.fake.body_of(self.map_number())
        self.assertIn("- how to migrate legacy plans", body)
        self.assertIn("- pricing tiers", body)

    def test_a_line_the_input_omits_is_never_removed(self):
        self.chart()
        inp = copy.deepcopy(INPUT)
        inp["map"]["notYetSpecified"] = ["pricing tiers"]   # drops the original
        self.chart(inp)
        body = self.fake.body_of(self.map_number())
        self.assertIn("- how to migrate legacy plans", body,
                      "additive means union: an omitted line is not a deletion")

    def test_new_edge_unions_into_an_existing_ticket_and_preserves_its_state(self):
        """ADR 0058: the ticket gains one edge and NOTHING else. Dropping the
        edge instead left frontier() reporting a ticket as actionable while a
        just-created ticket was meant to block it."""
        self.chart()
        gh.claim(self.ops, "billing", "rollout-order", "octocat")
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        inp = copy.deepcopy(INPUT)
        inp["tickets"].append({"key": "fog-graduate", "title": "Fog graduate",
                               "type": "grilling", "question": "newly specified?",
                               "blocks": ["rollout-order"]})
        out = self.chart(inp)
        by_key = {t["key"]: t for t in out["tickets"]}
        self.assertEqual(sorted(by_key["rollout-order"]["blockedBy"]),
                         ["auth-model", "fog-graduate"])
        self.assertEqual(by_key["rollout-order"]["assignee"], "octocat",
                         "the claim must survive an additive re-chart")
        self.assertEqual(by_key["auth-model"]["status"], "closed")
        self.assertEqual(by_key["auth-model"]["gist"], "shared keys",
                         "the recorded gist must survive an additive re-chart")

    def test_an_edge_may_name_an_existing_ticket_not_relisted_in_tickets(self):
        self.chart()
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [{"key": "late", "title": "Late", "type": "task",
                           "question": "?", "blocks": ["rollout-order"]}]
        out, _ = self.dry(inp)
        entry = next(e for e in out["planned"] if e["path"] == "rollout-order")
        self.assertEqual(entry["action"], "merge")
        self.assertIn("late", entry["detail"])

    def test_an_edge_that_already_exists_is_absent_from_the_plan(self):
        self.chart()
        out, _ = self.dry()
        rollout = next(e for e in out["planned"] if e["path"] == "rollout-order")
        self.assertEqual(rollout["action"], "skip (exists)")
        self.assertIsNone(rollout["detail"])

    def test_an_identical_rechart_reports_no_divergence_at_all(self):
        """A divergence for text nobody touched is worse than none: it trains
        the user to skip past the list, which is where the real ones appear.
        The title lives in the issue's title field, so checking it against the
        body reported one on every single re-chart."""
        self.chart()
        out = self.chart()
        self.assertEqual(out["divergence"], [], out["divergence"])

    def test_divergent_scalars_are_reported_not_applied(self):
        self.chart()
        inp = copy.deepcopy(INPUT)
        inp["map"]["destination"] = "COMPLETELY DIFFERENT"
        out = self.chart(inp)
        self.assertTrue(any("destination" in d for d in out["divergence"]),
                        out["divergence"])
        self.assertNotIn("COMPLETELY DIFFERENT",
                         self.fake.body_of(self.map_number()))

    def test_force_advice_states_what_force_destroys(self):
        self.chart()
        inp = copy.deepcopy(INPUT)
        inp["map"]["destination"] = "DIFFERENT"
        out = self.chart(inp)
        advice = [d for d in out["divergence"]
                  if "--force" in d or "re-chart" in d.lower()]
        self.assertTrue(advice, out["divergence"])
        for message in advice:
            low = message.lower()
            self.assertTrue(any(w in low for w in ("discard", "destroy")), message)
            for lost in ("resolution", "claim", "blocking edge"):
                self.assertIn(lost, low, message)


class TestForce(Base):
    def test_force_labels_overwrite_and_discards_recorded_state(self):
        self.chart()
        gh.claim(self.ops, "billing", "rollout-order", "octocat")
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        out, _ = self.dry(force=True)
        actions = {e["path"]: e["action"] for e in out["planned"]}
        self.assertEqual(actions["<map>"], "OVERWRITE")
        self.assertEqual(actions["auth-model"], "OVERWRITE")
        real = self.chart(force=True)
        by_key = {t["key"]: t for t in real["tickets"]}
        self.assertEqual(by_key["auth-model"]["status"], "open")
        self.assertIsNone(by_key["auth-model"]["gist"])
        self.assertIsNone(by_key["rollout-order"]["assignee"])
        # the edges are reset and re-wired from this input, not accumulated
        self.assertEqual(by_key["rollout-order"]["blockedBy"], ["auth-model"])

    def test_force_rewrites_the_map_title_too_not_only_the_body(self):
        """--force regenerates the whole map document, and on a tracker the name
        is a field rather than a line of the body. Patching only the body left
        the title stale, and the next additive run then reported that title as a
        divergence the user had already asked to apply."""
        self.chart()
        inp = copy.deepcopy(INPUT)
        inp["map"]["title"] = "Decision map — billing v2"
        self.chart(inp, force=True)
        self.assertEqual(self.fake.issue(self.map_number())["title"],
                         "Decision map — billing v2")
        self.assertEqual(self.chart(inp)["divergence"], [])

    def test_force_leaves_a_ticket_the_input_does_not_name_untouched(self):
        """--force means 'rewrite the items in this input', not 'reset the map'.
        A backend that deletes and re-charts would destroy this ticket."""
        self.chart()
        inp = copy.deepcopy(INPUT)
        inp["tickets"].append({"key": "extra", "title": "Extra", "type": "task",
                               "question": "?"})
        self.chart(inp)
        gh.resolve(self.ops, "billing", "extra", "kept", None, None)
        narrow = copy.deepcopy(INPUT)          # does not mention `extra`
        out = self.chart(narrow, force=True)
        by_key = {t["key"]: t for t in out["tickets"]}
        self.assertEqual(by_key["extra"]["status"], "closed")
        self.assertEqual(by_key["extra"]["gist"], "kept")


class TestJoin(Base):
    def test_a_labelled_child_with_no_key_marker_fails_loudly(self):
        """The worst failure this design can produce: ignoring it hides the
        ticket from the join, so chart labels it `create` and a map whose markers
        were stripped is silently re-created and shown as approvable lines."""
        self.chart()
        n = self.map_number()
        stray = self.fake.add_issue(title="hand-made", body="no marker here",
                                    labels=[gh.TICKET_LABEL], parent=(REPO, n))
        with self.assertRaises(map_core.JoinIntegrityError) as cm:
            gh.read_map(self.ops, "billing")
        self.assertIn(str(stray), str(cm.exception))

    def test_two_key_markers_in_one_body_fails_rather_than_picking_one(self):
        self.chart()
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        self.fake.issue(auth)["body"] += "\n" + (map_core.KEY_MARKER % "other")
        with self.assertRaises(map_core.JoinIntegrityError):
            gh.read_map(self.ops, "billing")

    def test_two_children_with_the_same_key_fails(self):
        self.chart()
        n = self.map_number()
        self.fake.add_issue(title="dup", body=map_core.KEY_MARKER % "auth-model",
                            labels=[gh.TICKET_LABEL], parent=(REPO, n))
        with self.assertRaises(map_core.JoinIntegrityError):
            gh.read_map(self.ops, "billing")

    def test_an_unrelated_child_with_neither_label_nor_marker_is_ignored(self):
        self.chart()
        n = self.map_number()
        self.fake.add_issue(title="someone else's issue", body="hello",
                            parent=(REPO, n))
        out = gh.read_map(self.ops, "billing")
        self.assertEqual([t["key"] for t in out["tickets"]],
                         ["auth-model", "rollout-order"])

    def test_the_key_marker_is_authoritative_and_the_label_is_decorative(self):
        """Keying membership on the label means one bulk label edit hides every
        ticket from the join and the whole map is re-created."""
        self.chart()
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        self.fake.issue(auth)["labels"] = []      # label stripped by hand
        out, _ = self.dry()
        actions = {e["path"]: e["action"] for e in out["planned"]}
        self.assertEqual(actions["auth-model"], "skip (exists)",
                         "a keyed child is still a ticket without its label")

    def test_a_null_body_is_not_an_empty_body(self):
        """GitHub returns body: null for an issue created with no description --
        verified on two live issues. A regex on it throws."""
        self.chart()
        n = self.map_number()
        self.fake.add_issue(title="no body at all", body=None, parent=(REPO, n))
        out = gh.read_map(self.ops, "billing")
        self.assertEqual(len(out["tickets"]), 2)

    def test_a_truncated_sub_issue_page_refuses_rather_than_re_creating(self):
        self.chart()
        self.fake.sub_issue_page_lie = True
        with self.assertRaises(map_core.JoinIntegrityError) as cm:
            gh.read_map(self.ops, "billing")
        self.assertIn("100", str(cm.exception))

    def test_slug_resolution_uses_a_label_listing_not_a_search(self):
        self.chart()
        paths = [p for _m, p in self.fake.calls if isinstance(p, str)]
        self.assertTrue(any(f"labels={gh.MAP_LABEL}" in p for p in paths))
        self.assertFalse(any("search" in p for p in paths),
                         "code search is eventually consistent; a stale index "
                         "would mean duplicate maps")

    def test_the_map_can_also_be_named_by_issue_number(self):
        self.chart()
        n = self.map_number()
        out = gh.read_map(self.ops, str(n))
        self.assertEqual(out["map"]["key"], "billing")

    def test_two_maps_claiming_one_slug_fails_rather_than_picking_one(self):
        self.chart()
        self.fake.add_issue(title="impostor",
                            body=map_core.KEY_MARKER % "billing",
                            labels=[gh.MAP_LABEL])
        with self.assertRaises(gh.MapNotFoundError) as cm:
            gh.read_map(self.ops, "billing")
        self.assertIn("claim the map slug", str(cm.exception))


class TestLineEndings(Base):
    def test_crlf_bodies_produce_no_spurious_divergence_or_write(self):
        """A human's web-UI edit can flip a region to CRLF. Without normalising,
        the next chart sees every line as changed and emits divergences for text
        nobody touched -- breaking the byte-identical no-op guarantee."""
        self.chart()
        self.fake.crlf = True                 # every read now comes back CRLF
        self.fake.reset_counters()
        out = self.chart()
        self.assertEqual(out["divergence"], [],
                         f"CRLF must not read as a change: {out['divergence']}")
        self.assertEqual(self.fake.write_count, 0)


class TestFrontier(Base):
    def test_claimed_and_blocked_lands_in_claimed_only(self):
        """Bucket precedence must be identical across backends or the same state
        yields two different frontier.json documents."""
        self.chart()
        gh.claim(self.ops, "billing", "rollout-order", "octocat")
        f = gh.frontier(self.ops, "billing")
        self.assertEqual([e["key"] for e in f["claimed"]], ["rollout-order"])
        self.assertEqual([e["key"] for e in f["blocked"]], [])
        self.assertEqual([e["key"] for e in f["frontier"]], ["auth-model"])

    def test_resolving_a_blocker_releases_its_dependent(self):
        self.chart()
        self.assertEqual([e["key"] for e in gh.frontier(self.ops, "billing")["blocked"]],
                         ["rollout-order"])
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        f = gh.frontier(self.ops, "billing")
        self.assertEqual([e["key"] for e in f["frontier"]], ["rollout-order"])
        self.assertEqual(f["blocked"], [])

    def test_closed_tickets_appear_in_no_bucket(self):
        self.chart()
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        f = gh.frontier(self.ops, "billing")
        everything = [e["key"] for b in f.values() for e in b]
        self.assertNotIn("auth-model", everything)

    def test_map_json_keeps_closed_blockers_but_frontier_does_not(self):
        """Deliberately different, and the two must not be made to agree:
        map.json is the durable graph, frontier answers 'why can I not pick this
        up right now'."""
        self.chart()
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        m = gh.read_map(self.ops, "billing")
        rollout = next(t for t in m["tickets"] if t["key"] == "rollout-order")
        self.assertEqual(rollout["blockedBy"], ["auth-model"])
        self.assertEqual(gh.frontier(self.ops, "billing")["blocked"], [])

    def test_a_de_parented_blocker_still_blocks_and_is_named(self):
        """ADR 0061: an absence is not a resolution. Wrongly holding a ticket
        back costs one question; wrongly releasing it starts a session on work
        the map says is not ready."""
        self.chart()
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        self.fake.issue(auth)["parent"] = None      # moved out of the map
        with captured_stderr():
            f = gh.frontier(self.ops, "billing")
        blocked = f["blocked"]
        self.assertEqual([e["key"] for e in blocked], ["rollout-order"])
        self.assertEqual(blocked[0]["missingBlockers"], ["auth-model"])
        self.assertIn("auth-model", blocked[0]["blockedBy"])

    def test_a_foreign_blocker_is_ignored_but_never_silently(self):
        """A dependency on an issue that is not a decision-map ticket at all is
        a cross-map edge this design does not model. Ignoring it is right;
        ignoring it quietly reads to the user as an unblocked ticket."""
        self.chart()
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        outsider = self.fake.add_issue(title="unrelated", body="nothing here")
        self.fake.issue(auth)["blockedBy"].append((REPO, outsider))
        with captured_stderr() as err:
            f = gh.frontier(self.ops, "billing")
        self.assertIn("auth-model", [e["key"] for e in f["frontier"]])
        self.assertIn("warning", err.getvalue())
        self.assertIn(str(outsider), err.getvalue())

    def test_frontier_on_a_missing_map_fails_instead_of_reporting_done(self):
        """Three empty buckets and exit 0 is byte-identical to a map whose every
        decision is resolved -- and that is what a flow skill renders."""
        with self.assertRaises(gh.MapNotFoundError):
            gh.frontier(self.ops, "no-such-map")

    def test_buckets_are_key_ascending_not_tracker_order(self):
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [
            {"key": "zeta", "title": "Z", "type": "task", "question": "?"},
            {"key": "alpha", "title": "A", "type": "task", "question": "?"},
            {"key": "mid", "title": "M", "type": "task", "question": "?"},
        ]
        self.chart(inp)
        out = gh.read_map(self.ops, "billing")
        self.assertEqual([t["key"] for t in out["tickets"]],
                         ["alpha", "mid", "zeta"])
        self.assertEqual([e["key"] for e in gh.frontier(self.ops, "billing")["frontier"]],
                         ["alpha", "mid", "zeta"])


class TestResolve(Base):
    def test_resolve_comments_closes_writes_the_gist_and_reindexes(self):
        self.chart()
        n = self.map_number()
        out = gh.resolve(self.ops, "billing", "auth-model",
                         "shared keys, rotated per release",
                         "docs/adr/0007.md", "## Rationale\n\nwhy")
        self.assertEqual(out["gist"], "shared keys, rotated per release")
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        # the comment is the record a human reads
        self.assertTrue(any("## Resolution" in c for c in self.fake.comments_on(auth)))
        self.assertTrue(any("docs/adr/0007.md" in c for c in self.fake.comments_on(auth)))
        self.assertTrue(any("## Rationale" in c for c in self.fake.comments_on(auth)))
        # the gist region is the field a machine reads
        self.assertIn("shared keys, rotated per release", self.fake.body_of(auth))
        self.assertEqual(self.fake.issue(auth)["state"], "closed")
        # the map index is a projection
        self.assertIn("shared keys, rotated per release", self.fake.body_of(n))
        # the index links to the ISSUE, not to a same-page `#N` fragment that
        # goes nowhere when clicked from the map body
        self.assertIn(f"/issues/{auth})", self.fake.body_of(n))
        self.assertNotIn(f"]({'#'}{auth})", self.fake.body_of(n))

    def test_resolve_costs_four_calls(self):
        """The contract asked for the per-subcommand budget in writing before
        implementing, because resolve is the expensive one, not the cheapest."""
        self.chart()
        self.fake.reset_counters()
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        self.assertEqual(len(self.fake.calls), 5, self.fake.calls)   # 1 slug list
        self.assertEqual(self.fake.write_count, 3, self.fake.writes)

    def test_body_and_state_move_in_one_call(self):
        """Two calls leave a window where the ticket is closed with no gist,
        which `read` reports as a resolved decision with no answer."""
        self.chart()
        self.fake.reset_counters()
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        patches = [w for w in self.fake.writes if w[0] == "PATCH"]
        ticket_patch = [p for p in patches if "state" in p[2]]
        self.assertEqual(len(ticket_patch), 1)
        self.assertIn("body", ticket_patch[0][2])

    def test_re_resolving_replaces_rather_than_accumulates(self):
        self.chart()
        gh.resolve(self.ops, "billing", "auth-model", "first answer", None, None)
        gh.resolve(self.ops, "billing", "auth-model", "second answer", None, None)
        n = self.map_number()
        body = self.fake.body_of(n)
        self.assertEqual(body.count("second answer"), 1)
        self.assertNotIn("first answer", body,
                         "the index is a projection, not accumulated state")
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        self.assertEqual(self.fake.body_of(auth).count(map_core.GIST_START), 1)

    def test_the_index_is_ordered_by_key_not_by_resolution_order(self):
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [
            {"key": "zeta", "title": "Z", "type": "task", "question": "?"},
            {"key": "alpha", "title": "A", "type": "task", "question": "?"},
        ]
        self.chart(inp)
        gh.resolve(self.ops, "billing", "zeta", "z first", None, None)
        gh.resolve(self.ops, "billing", "alpha", "a second", None, None)
        body = self.fake.body_of(self.map_number())
        inner = map_core.region_body(body, map_core.DECISIONS_START,
                                     map_core.DECISIONS_END)
        self.assertLess(inner.index("a second"), inner.index("z first"),
                        "re-running the projection must never reorder it")

    def test_a_gist_region_a_human_stripped_is_appended_not_guessed_at(self):
        self.chart()
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        body = self.fake.issue(auth)["body"]
        self.fake.issue(auth)["body"] = (
            body.replace(map_core.GIST_START, "").replace(map_core.GIST_END, ""))
        gh.resolve(self.ops, "billing", "auth-model", "recovered", None, None)
        self.assertIn("recovered", self.fake.body_of(auth))
        self.assertIn("## Question", self.fake.body_of(auth),
                      "user content must survive")


class GitHubGroupedIndexTest(Base):
    """The same grouping as the local backend, against the tracker (ADR 0103)."""

    def test_resolving_writes_a_grouped_index(self):
        inp = copy.deepcopy(INPUT)
        inp["map"]["milestones"] = [
            {"slug": "mvp", "label": "demo it", "members": ["auth-model"]}]
        self.chart(inp)
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        body = self.fake.body_of(self.map_number())
        inner = map_core.region_body(body, map_core.DECISIONS_START,
                                     map_core.DECISIONS_END)
        self.assertIn("#### mvp — demo it", inner)
        self.assertIn("shared keys", inner)

    def test_an_unmilestoned_map_keeps_the_flat_index(self):
        self.chart()
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        body = self.fake.body_of(self.map_number())
        inner = map_core.region_body(body, map_core.DECISIONS_START,
                                     map_core.DECISIONS_END)
        self.assertNotIn("####", inner)


class TestClaimBlockComment(Base):
    def test_claim_writes_a_real_login_and_empty_releases_it(self):
        self.chart()
        gh.claim(self.ops, "billing", "auth-model", "octocat")
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        self.assertEqual(self.fake.issue(auth)["assignees"], ["octocat"])
        gh.claim(self.ops, "billing", "auth-model", "")
        self.assertEqual(self.fake.issue(auth)["assignees"], [])

    def test_claim_never_writes_the_literal_me(self):
        """The local backend's default is the string "me", which is meaningless
        as a GitHub assignee."""
        self.chart()
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = gh.main(["claim", "--repo", REPO, "--map", "billing",
                          "--ticket", "auth-model"], api=self.fake)
        self.assertEqual(rc, 0)
        self.assertEqual(json.loads(buf.getvalue())["assignee"], self.fake.login)
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        self.assertNotIn("me", self.fake.issue(auth)["assignees"])

    def test_block_does_not_write_when_the_edge_already_exists(self):
        """A redundant link call shows in the issue's timeline. GitHub also
        answers 422 on a duplicate, verified live, so this cannot be left to the
        API to make idempotent."""
        self.chart()
        self.fake.reset_counters()
        out = gh.block(self.ops, "billing", "rollout-order", "auth-model")
        self.assertEqual(out["blockedBy"], ["auth-model"])
        self.assertEqual(self.fake.write_count, 0, self.fake.writes)

    def test_block_rejects_a_self_edge(self):
        self.chart()
        with self.assertRaises(map_core.InvalidEdgeError):
            gh.block(self.ops, "billing", "auth-model", "auth-model")

    def test_block_rejects_a_target_that_is_not_on_this_map(self):
        """A phantom target blocks the ticket for ever, with nothing able to
        close it."""
        self.chart()
        with self.assertRaises(gh.MapNotFoundError):
            gh.block(self.ops, "billing", "auth-model", "nonexistent")

    def test_comment_posts_a_native_comment(self):
        self.chart()
        gh.comment(self.ops, "billing", "auth-model", "a plain note")
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        self.assertIn("a plain note", self.fake.comments_on(auth))

    def test_dry_run_on_a_ticket_subcommand_is_a_stub_with_zero_calls(self):
        """Parity with local, which is the point: a dry run that quietly
        performs lookups is not free, and one that renders a plan where local
        renders a stub is not at parity."""
        self.chart()
        for cmd, extra in (("claim", []), ("comment", []),
                           ("block", ["--blocked-by", "auth-model"]),
                           ("resolve", ["--gist", "x"])):
            with self.subTest(cmd=cmd):
                self.fake.reset_counters()
                buf = io.StringIO()
                args = [cmd, "--repo", REPO, "--map", "billing",
                        "--ticket", "rollout-order", "--dry-run"] + extra
                if cmd == "comment":
                    with tempfile.TemporaryDirectory() as d:
                        p = Path(d) / "b.md"
                        p.write_text("note", encoding="utf-8")
                        args += ["--body-file", str(p)]
                        with contextlib.redirect_stdout(buf):
                            rc = gh.main(args, api=self.fake)
                else:
                    with contextlib.redirect_stdout(buf):
                        rc = gh.main(args, api=self.fake)
                self.assertEqual(rc, 0)
                out = json.loads(buf.getvalue())
                self.assertTrue(out["dryRun"])
                self.assertEqual(out["wouldRun"], cmd)
                self.assertEqual(len(self.fake.calls), 0,
                                 f"{cmd} --dry-run made calls: {self.fake.calls}")


class TestMarkerInvariant(Base):
    def test_a_question_cannot_forge_a_key_marker(self):
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [{"key": "victim", "title": "T", "type": "task",
                           "question": "what about "
                                       "<!-- decision-map:key:evil --> this?"}]
        self.chart(inp)
        victim = int(gh.read_map(self.ops, "billing")["tickets"][0]["id"])
        body = self.fake.body_of(victim)
        self.assertIn(map_core.MARKER_ESCAPED_PREFIX + "key:evil", body)
        self.assertEqual(body.count(map_core.MARKER_PREFIX + "key:"), 1,
                         "exactly one live key marker, the tool's own")
        self.assertEqual([t["key"] for t in gh.read_map(self.ops, "billing")["tickets"]],
                         ["victim"])

    def test_a_fog_line_cannot_forge_a_region_marker(self):
        inp = copy.deepcopy(INPUT)
        inp["map"]["notYetSpecified"] = [
            "<!-- decision-map:fog:end --> escaped?"]
        self.chart(inp)
        body = self.fake.body_of(self.map_number())
        self.assertEqual(body.count(map_core.FOG_END), 1)

    def test_a_multiline_value_is_flattened_before_it_is_escaped(self):
        """Escaping first is a hole: flattening afterwards reconstitutes a live
        marker that no escape ever saw."""
        inp = copy.deepcopy(INPUT)
        inp["map"]["notYetSpecified"] = ["<!--\ndecision-map:fog:end --> x"]
        self.chart(inp)
        body = self.fake.body_of(self.map_number())
        self.assertEqual(body.count(map_core.FOG_END), 1)

    def test_a_gist_cannot_forge_a_marker(self):
        self.chart()
        gh.resolve(self.ops, "billing", "auth-model",
                   "answer <!-- decision-map:gist:end --> here", None, None)
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        self.assertEqual(self.fake.body_of(auth).count(map_core.GIST_END), 1)


class TestValidation(Base):
    def test_a_double_hyphen_key_is_rejected(self):
        """The HTML spec forbids '--' inside comment text, so the marker would
        be malformed and sanitizers rewrite or truncate it -- silently breaking
        the join and re-creating every ticket."""
        inp = copy.deepcopy(INPUT)
        inp["tickets"][0]["key"] = "auth--model"
        with self.assertRaises(map_core.ChartValidationError):
            self.chart(inp)

    def test_a_double_hyphen_map_slug_is_rejected_too(self):
        """On a tracker the map slug is itself a key marker, so a slug the local
        backend accepts and a tracker cannot carry makes a map unmigratable."""
        inp = copy.deepcopy(INPUT)
        inp["target"] = dict(inp["target"], slug="bill--ing")
        with self.assertRaises(map_core.ChartValidationError):
            self.chart(inp)

    def test_nothing_is_written_when_validation_fails(self):
        inp = copy.deepcopy(INPUT)
        inp["tickets"][1].pop("question")
        with self.assertRaises(map_core.ChartValidationError):
            self.chart(inp)
        self.assertEqual(self.fake.write_count, 0,
                         "a malformed input must not leave a half-charted map")

    def test_an_edge_to_an_unknown_ticket_is_rejected(self):
        inp = copy.deepcopy(INPUT)
        inp["tickets"][0]["blocks"] = ["nobody"]
        with self.assertRaises(map_core.ChartValidationError):
            self.chart(inp)

    def test_a_self_edge_is_rejected(self):
        inp = copy.deepcopy(INPUT)
        inp["tickets"][0]["blocks"] = ["auth-model"]
        with self.assertRaises(map_core.ChartValidationError):
            self.chart(inp)

    def test_the_hundred_ticket_ceiling_fails_before_any_write(self):
        """Creating up to the cap and then failing halfway leaves a half-charted
        map, and the failure arrives after the user approved the plan."""
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [{"key": f"t{i:03d}", "title": f"T{i}", "type": "task",
                           "question": "?"} for i in range(101)]
        with self.assertRaises(map_core.ChartValidationError) as cm:
            self.chart(inp)
        self.assertIn("100", str(cm.exception))
        self.assertEqual(self.fake.write_count, 0)

    def test_a_traversal_payload_in_ticket_is_rejected(self):
        self.chart()
        with self.assertRaises(map_core.UnsafeIdentifierError):
            gh.claim(self.ops, "billing", "../../../VICTIM", "octocat")

    def test_a_missing_type_label_falls_back_to_hitl_and_says_so(self):
        """Getting the type wrong decides whether a session runs unattended.
        HITL is the safe direction: a human is asked."""
        self.chart()
        auth = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                        if t["key"] == "auth-model")["id"])
        self.fake.issue(auth)["labels"] = [gh.TICKET_LABEL]
        with captured_stderr() as err:
            out = gh.read_map(self.ops, "billing")
        t = next(x for x in out["tickets"] if x["key"] == "auth-model")
        self.assertEqual(t["mode"], "HITL")
        self.assertIn("warning", err.getvalue())


class TestCliContract(Base):
    def test_a_known_failure_is_one_stderr_line_exit_2_and_empty_stdout(self):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            rc = gh.main(["read", "--repo", REPO, "--map", "no-such-map"],
                         api=self.fake)
        self.assertEqual(rc, gh.EXIT_ERROR)
        self.assertEqual(out.getvalue(), "",
                         "a caller piping stdout must never see half a document")
        self.assertEqual(len(err.getvalue().strip().splitlines()), 1,
                         err.getvalue())

    def test_missing_required_args_are_a_usage_error_not_a_traceback(self):
        for args in (["read", "--repo", REPO],
                     ["claim", "--repo", REPO, "--map", "billing"],
                     ["chart", "--repo", REPO]):
            with self.subTest(args=args):
                err = io.StringIO()
                with contextlib.redirect_stderr(err):
                    rc = gh.main(args, api=self.fake)
                self.assertEqual(rc, gh.EXIT_ERROR)
                self.assertIn("needs", err.getvalue())

    def test_repo_is_never_inferred(self):
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            rc = gh.main(["read", "--map", "billing"], api=self.fake)
        self.assertEqual(rc, gh.EXIT_ERROR)
        self.assertIn("--repo", err.getvalue())

    def test_chart_may_take_the_repo_from_the_input_target(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "in.json"
            p.write_text(json.dumps(INPUT), encoding="utf-8")
            out, err = io.StringIO(), io.StringIO()
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                rc = gh.main(["chart", "--input", str(p)], api=self.fake)
            self.assertEqual(rc, 0, err.getvalue())

    def test_output_file_gets_the_same_document_as_stdout(self):
        with tempfile.TemporaryDirectory() as d:
            outp = Path(d) / "map.json"
            buf, err = io.StringIO(), io.StringIO()
            with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(err):
                rc = gh.main(["chart", "--repo", REPO, "--input",
                              self._input_file(d), "--real",
                              "--output", str(outp)], api=self.fake)
            self.assertEqual(rc, 0, err.getvalue())
            self.assertEqual(json.loads(outp.read_text(encoding="utf-8")),
                             json.loads(buf.getvalue()))

    def _input_file(self, d):
        p = Path(d) / "in.json"
        p.write_text(json.dumps(INPUT), encoding="utf-8")
        return str(p)

    def test_the_subcommand_surface_matches_the_local_backend(self):
        """The flow skills are backend-neutral: the subcommands, flags and JSON
        shapes do not change when a tracker lands, only which script is called."""
        import local_map_ops as local
        local_cmds = set(local._REQUIRED_CLI_ARGS)
        self.assertEqual(set(gh._REQUIRED_CLI_ARGS), local_cmds)
        for cmd in local_cmds:
            with self.subTest(cmd=cmd):
                self.assertEqual(
                    [flag for _a, flag in gh._REQUIRED_CLI_ARGS[cmd]],
                    [flag for _a, flag in local._REQUIRED_CLI_ARGS[cmd]])


class TestThrottle(unittest.TestCase):
    def test_a_small_burst_never_sleeps(self):
        slept = []
        t = gh._Throttle(per_minute=70, sleep=slept.append, clock=lambda: 0.0)
        for _ in range(10):
            t.note_write()
        self.assertEqual(slept, [], "a 3-ticket chart must not wait")

    def test_crossing_the_window_sleeps_just_enough(self):
        slept, now = [], [0.0]
        t = gh._Throttle(per_minute=5, sleep=slept.append, clock=lambda: now[0])
        for _ in range(5):
            t.note_write()
            now[0] += 1.0
        with contextlib.redirect_stderr(io.StringIO()):
            t.note_write()
        self.assertEqual(len(slept), 1)
        self.assertAlmostEqual(slept[0], 60.0 - 5.0 + 0.5, places=6)


class TestReviewRegressions(Base):
    """One test per defect an adversarial review found and reproduced.

    Every one of these passed the first version of the suite, which is why they
    are here: each names the harm, not just the behaviour, so a future edit that
    reintroduces it fails with an explanation rather than a diff.
    """

    # --- the escape-order rule, on the READ side --------------------------
    def test_a_tab_in_a_gist_cannot_reconstitute_a_marker_on_read(self):
        """`gist_of` collapsed whitespace with `str.split()`, which breaks on TAB,
        while the writer flattens with `str.splitlines()`, which does not. So
        `<!--<TAB>decision-map:decisions:end -->` was stored verbatim (scrub found
        no marker -- the prefix needs a space) and came back out as a LIVE marker
        no escape had ever seen. Written into the map's decisions index it left
        the region unpaired, so the next `resolve` died *after* posting its
        comment and closing its ticket, and every later `resolve` on that map
        failed at the same point -- the index unrecoverable by any
        non-destructive operation."""
        self.chart()
        poison = "<!--\tdecision-map:decisions:end -->"
        gh.resolve(self.ops, "billing", "auth-model", poison, None, None)
        got = next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                   if t["key"] == "auth-model")["gist"]
        self.assertNotEqual(got, map_core.DECISIONS_END,
                            "the read side must not reconstitute a live marker")
        self.assertIn("\t", got, "the stored value must come back as stored")
        # and the map is still writable afterwards
        gh.resolve(self.ops, "billing", "rollout-order", "tenant by tenant", None, None)
        body = self.fake.body_of(self.map_number())
        self.assertEqual(body.count(map_core.DECISIONS_END), 1)
        self.assertIn("tenant by tenant", body,
                      "the second decision must reach the index")

    def test_resolve_reports_the_gist_exactly_as_read_will_return_it(self):
        """No tab and no malice needed: collapsing whitespace on read made
        `resolve` return 'shared  keys' as the gist "as stored" while `read`
        reported 'shared keys'. The contract says that value IS as stored."""
        self.chart()
        out = gh.resolve(self.ops, "billing", "auth-model", "shared  keys", None, None)
        back = next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                    if t["key"] == "auth-model")["gist"]
        self.assertEqual(out["gist"], back)

    # --- the map's membership is the marker, not the label ----------------
    def test_a_map_whose_label_was_stripped_is_not_re_created(self):
        """The worst failure this design can produce, and keying the MAP on its
        decorative label reintroduced it after the ticket join had been written
        not to: the slug resolved to nothing, chart concluded the map was absent,
        and it created a SECOND map while labelling every existing ticket
        `create` — a full silent re-creation presented as approvable lines."""
        self.chart()
        n = self.map_number()
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        self.fake.issue(n)["labels"] = []          # bulk label edit
        # NOT self.dry(): that swallows stderr internally, and the warning is
        # half of what is under test here.
        with captured_stderr() as err:
            out = gh.chart(self.ops, copy.deepcopy(INPUT), real=False)
        actions = {e["path"]: e["action"] for e in out["planned"]}
        self.assertNotEqual(actions["<map>"], "create",
                            f"chart would re-create the map: {out['planned']}")
        self.assertEqual(actions["auth-model"], "skip (exists)")
        self.assertIn("lost its", err.getvalue())
        # and the real run restores the label rather than leaving the sweep to
        # be paid for on every future chart
        with captured_stderr():
            self.chart()
        self.assertIn(gh.MAP_LABEL, self.fake.issue(n)["labels"])
        self.assertEqual(
            next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                 if t["key"] == "auth-model")["gist"], "shared keys",
            "the recorded resolution must survive")

    def test_two_maps_claiming_one_slug_makes_chart_fail_not_create_a_third(self):
        """`try_snapshot` caught every MapNotFoundError, so 'two maps claim this
        slug' — a state a human must resolve — was read as 'not there' and chart
        created a third."""
        self.chart()
        self.fake.add_issue(title="impostor",
                            body=map_core.KEY_MARKER % "billing",
                            labels=[gh.MAP_LABEL])
        self.fake.reset_counters()
        with self.assertRaises(gh.MapNotFoundError):
            self.chart()
        self.assertEqual(self.fake.write_count, 0,
                         "it must not create a third map, nor anything else")

    def test_a_non_map_issue_passed_as_map_fails_rather_than_reading_empty(self):
        """The ADR 0061 shape again: an issue that is not a map returned an empty
        map at exit 0, which renders as a finished effort."""
        self.chart()
        plain = self.fake.add_issue(title="just an issue", body="nothing here")
        for fn in (gh.read_map, gh.frontier):
            with self.subTest(fn=fn.__name__):
                with self.assertRaises(gh.MapNotFoundError):
                    fn(self.ops, str(plain))

    def test_one_hand_labelled_issue_does_not_make_every_map_unreadable(self):
        """The loud-error rule is about a labelled CHILD of the map being read.
        Applying it to every `decision-map:map` issue in the repo meant one
        hand-labelled issue broke every map in the repo."""
        self.chart()
        self.fake.add_issue(title="someone labelled this by hand",
                            body="no marker at all", labels=[gh.MAP_LABEL])
        with captured_stderr() as err:
            out = gh.read_map(self.ops, "billing")
        self.assertEqual(out["map"]["key"], "billing")
        self.assertIn("warning", err.getvalue())

    # --- failure contract -------------------------------------------------
    def test_a_malformed_input_exits_2_with_one_line_not_a_traceback(self):
        """chart read inp["target"]["slug"] before validating, so a
        map_input.json missing "target" raised a bare KeyError: exit 1 with a
        traceback where the local backend exits 2 with one stderr line."""
        for bad in ({"map": {}, "tickets": []},
                    {"target": {}, "map": {"title": "t", "destination": "d"},
                     "tickets": []}):
            with self.subTest(bad=sorted(bad)):
                with tempfile.TemporaryDirectory() as d:
                    p = Path(d) / "in.json"
                    p.write_text(json.dumps(bad), encoding="utf-8")
                    out, err = io.StringIO(), io.StringIO()
                    with contextlib.redirect_stdout(out), \
                            contextlib.redirect_stderr(err):
                        rc = gh.main(["chart", "--repo", REPO, "--input", str(p)],
                                     api=self.fake)
                self.assertEqual(rc, gh.EXIT_ERROR)
                self.assertEqual(out.getvalue(), "")
                self.assertEqual(len(err.getvalue().strip().splitlines()), 1,
                                 err.getvalue())

    def test_dry_run_validates_the_map_identifier_too(self):
        """An inert but untruthful dry run: `--map ../../evil --dry-run` exited 0
        where the identical call without --dry-run exits 2, and where the local
        backend exits 2 for both."""
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            rc = gh.main(["claim", "--repo", REPO, "--map", "../../evil",
                          "--ticket", "auth-model", "--dry-run"], api=self.fake)
        self.assertEqual(rc, gh.EXIT_ERROR)
        self.assertIn("map slug", err.getvalue())

    # --- handles ----------------------------------------------------------
    def test_the_id_map_json_reports_can_be_passed_back_to_ticket(self):
        """The contract says `id` exists "for passing back to --ticket". It could
        not be: every subcommand looked the value up in a dict keyed by key, so
        `--ticket 1235` failed for a ticket just reported as id 1235."""
        self.chart()
        auth = next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                    if t["key"] == "auth-model")
        out = gh.claim(self.ops, "billing", auth["id"], "octocat")
        self.assertEqual(out["claimed"], "auth-model",
                         "a numeric --ticket must resolve to its key")
        gh.comment(self.ops, "billing", auth["id"], "by number")
        self.assertIn("by number", self.fake.comments_on(int(auth["id"])))

    def test_frontier_and_map_json_agree_on_a_ticket_id(self):
        """They disagreed — map.json said the issue number, frontier said the
        key — so a skill reading one and calling with the other broke."""
        self.chart()
        m = {t["key"]: t["id"] for t in gh.read_map(self.ops, "billing")["tickets"]}
        f = gh.frontier(self.ops, "billing")
        for e in f["frontier"] + f["blocked"] + f["claimed"]:
            self.assertEqual(e["id"], m[e["key"]], e)

    def test_the_decisions_index_links_to_the_issue_not_a_page_fragment(self):
        """`[title](#2)` inside an issue body is a same-page anchor, so every
        entry in the index a human is meant to click went nowhere."""
        self.chart()
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        inner = map_core.region_body(
            map_core.norm_eol(self.fake.body_of(self.map_number())),
            map_core.DECISIONS_START, map_core.DECISIONS_END)
        self.assertIn("https://github.com/", inner)
        self.assertNotIn("](#", inner)

    # --- edges are identity, not key strings ------------------------------
    def _de_parent_and_recreate(self):
        """Make the one state that separates 'edge exists' from 'key is listed':
        the ticket that held a key is moved out of the map, and a NEW ticket
        takes that key. The stale edge then points at an issue that is no longer
        part of the map while carrying a key that is."""
        self.chart()
        old = int(next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                       if t["key"] == "auth-model")["id"])
        self.fake.issue(old)["parent"] = None
        n = self.map_number()
        fresh = self.fake.add_issue(
            title="Auth model (re-made)",
            body=map_core.KEY_MARKER % "auth-model",
            labels=[gh.TICKET_LABEL, gh.TYPE_LABEL % "grilling"],
            parent=(REPO, n))
        return old, fresh

    def test_block_writes_the_real_edge_when_a_stale_one_shares_the_key(self):
        """`block` tested membership in the KEY list, which deliberately includes
        a re-parented ticket's key so missingBlockers can name it. So a stale edge
        read as 'the edge exists', the genuine edge was never written, and block
        reported success at exit 0."""
        old, fresh = self._de_parent_and_recreate()
        with captured_stderr():
            gh.block(self.ops, "billing", "rollout-order", "auth-model")
            m = gh.read_map(self.ops, "billing")
        rollout = int(next(t for t in m["tickets"]
                           if t["key"] == "rollout-order")["id"])
        edges = self.fake.issue(rollout)["blockedBy"]
        self.assertIn((REPO, fresh), edges,
                      f"the edge to the CURRENT auth-model must exist: {edges}")

    def test_force_removes_an_edge_to_a_de_parented_blocker(self):
        """An OVERWRITE line promises blockedBy is reset. Removal was looked up by
        key in the current children, where a re-parented target is absent, so it
        silently no-op'd and the rewritten ticket stayed blocked by something that
        can never close — permanently quarantined."""
        old, _fresh = self._de_parent_and_recreate()
        m = gh.read_map(self.ops, "billing")
        rollout = int(next(t for t in m["tickets"]
                           if t["key"] == "rollout-order")["id"])
        self.assertIn((REPO, old), self.fake.issue(rollout)["blockedBy"],
                      "sanity: the stale edge is there to begin with")
        with captured_stderr():
            self.chart(force=True)
        self.assertNotIn((REPO, old), self.fake.issue(rollout)["blockedBy"],
                         "--force must clear the stale edge it promised to reset")

    # --- a sub-issue may live in another repo of the same owner -----------
    def test_a_cross_repo_ticket_is_written_to_its_own_repo(self):
        """A sub-issue only has to share the parent's OWNER, and issue numbers
        restart per repository. Keying the join on the bare number and addressing
        every write at the map's repo meant a cross-repo ticket's claim, comment
        and close landed on whatever unrelated issue shared its number — and the
        join reported that unrelated issue's key."""
        self.chart()
        n = self.map_number()
        other = "acme/other"
        # #2 in acme/other, colliding with auth-model's number in the map's repo
        far = self.fake.add_issue(
            title="Far ticket", body=map_core.KEY_MARKER % "far-decision",
            labels=[gh.TICKET_LABEL, gh.TYPE_LABEL % "task"],
            parent=(REPO, n), repo=other, number=2)
        decoy = self.fake.add_issue(title="unrelated same-numbered issue",
                                    body="nothing", repo=other, number=99)
        out = gh.read_map(self.ops, "billing")
        far_json = next(t for t in out["tickets"] if t["key"] == "far-decision")
        self.assertEqual(far_json["repo"], other)
        self.assertEqual(far_json["id"], str(far))
        # the join must not have confused it with the map-repo issue #2
        auth = next(t for t in out["tickets"] if t["key"] == "auth-model")
        self.assertNotEqual(auth["repo"], other)
        # and every write must land in acme/other
        gh.claim(self.ops, "billing", "far-decision", "octocat")
        gh.comment(self.ops, "billing", "far-decision", "a cross-repo note")
        gh.resolve(self.ops, "billing", "far-decision", "done over there", None, None)
        self.assertEqual(self.fake.issue(far, other)["assignees"], ["octocat"])
        self.assertIn("a cross-repo note", self.fake.comments_on(far, other))
        self.assertEqual(self.fake.issue(far, other)["state"], "closed")
        self.assertIn("done over there", self.fake.body_of(far, other))
        # ...and nothing must have touched the same-numbered issue in the map repo
        self.assertEqual(self.fake.issue(2)["assignees"], [])
        self.assertEqual(self.fake.issue(2)["state"], "open")
        self.assertEqual(self.fake.comments_on(decoy, other), [])

    # --- writes the pacing must see --------------------------------------
    def test_a_payload_less_delete_counts_as_a_write(self):
        """Both the throttle and the write log defined a write as 'a call with a
        payload', so the remove-dependency DELETE on the --force path was neither
        paced nor counted — a --force over a well-connected map could trip the
        secondary rate limit mid-rewrite, leaving tickets half-reset."""
        slept = []
        api = GhApiSpy(throttle=gh._Throttle(per_minute=1, sleep=slept.append,
                                             clock=lambda: 0.0))
        api.rest("repos/a/b/issues/1/dependencies/blocked_by/9", "DELETE")
        api.rest("repos/a/b/issues/1/dependencies/blocked_by/8", "DELETE")
        self.assertEqual(len(slept), 1,
                         "a DELETE is a content-changing request and must be paced")

    # --- a create that lands with a link that does not --------------------
    def test_an_unlinkable_created_ticket_fails_loudly_naming_the_orphan(self):
        """create-then-link: if the link fails, an issue carrying that key exists
        but belongs to no map. The join only sees children, so a plain re-run
        created a SECOND issue with the same key — and the run after that failed
        the duplicate-key check on a map nobody could chart again."""
        def boom(*_a, **_k):
            raise gh.GitHubError("simulated link failure")
        self.ops.link_child = boom
        with captured_stderr():
            with self.assertRaises(gh.GitHubError) as cm:
                gh.chart(self.ops, copy.deepcopy(INPUT), real=True)
        msg = str(cm.exception)
        self.assertIn("carries the key marker", msg)
        self.assertIn("BEFORE re-running", msg)
        self.assertRegex(msg, r"#\d+", "the orphan's number must be named")


class TestPositionDiagram(Base):
    def test_a_created_ticket_issue_body_carries_a_graph_region(self):
        body = gh.render_ticket_issue_body("auth-model", "why?")
        self.assertIn(map_core.GRAPH_START, body)
        self.assertIn('ME["auth-model (this ticket)"]', body)
        self.assertLess(body.index(map_core.GRAPH_START),
                        body.index(map_core.GIST_START),
                        "position before the machine-readable gist region")

    def test_adding_a_dependency_patches_both_issue_bodies(self):
        # INPUT already wires auth-model -> rollout-order, so chart() alone
        # must leave both ends rendered
        out = self.chart()
        by_key = {t["key"]: int(t["id"]) for t in out["tickets"]}
        blocked = self.fake.issue(by_key["rollout-order"])["body"]
        blocker = self.fake.issue(by_key["auth-model"])["body"]
        self.assertIn('P0["auth-model"] --> ME', blocked,
                      "the blocked issue shows its blocker as a parent")
        self.assertIn('ME --> C0["rollout-order"]', blocker,
                      "the blocker issue shows what it unblocks as a child")

    def test_an_existing_ticket_gaining_a_new_blocker_role_is_announced_in_the_plan(self):
        """`chart_plan`'s `pending` pass announces the BLOCKED end of a new
        edge, but had no counterpart to local's `blocker_gains` -- the pass
        that announces the BLOCKER end when it is an existing (not created)
        ticket. Without it, a dry-run plan was silent about an issue the real
        run's post-write graph-region pass does patch -- both ends render
        (ADR 0064) -- breaking the ADR-0039 gate's promise that nothing the
        run writes is missing from the plan."""
        inp = copy.deepcopy(INPUT)
        inp["tickets"].append({"key": "billing-flag", "title": "Billing flag",
                               "type": "task", "question": "flagged how?"})
        self.chart(inp)
        inp2 = copy.deepcopy(inp)
        for t in inp2["tickets"]:
            if t["key"] == "auth-model":
                # rollout-order: already blocked by auth-model, unchanged.
                # billing-flag: a genuinely NEW edge from an EXISTING blocker.
                t["blocks"] = ["rollout-order", "billing-flag"]
        out, _err = self.dry(inp2)
        by_path = {e["path"]: e for e in out["planned"]}
        self.assertEqual(by_path["auth-model"]["action"], "merge",
                         "the blocker's own issue must be announced, not skipped")
        self.assertIn("renders as a child in the graph: billing-flag",
                      by_path["auth-model"]["detail"])
        self.assertEqual(by_path["rollout-order"]["action"], "skip (exists)",
                         "an edge that already exists must not be re-announced")

        # the plan told the truth: the real run patches auth-model's issue too
        self.fake.reset_counters()
        self.chart(inp2)
        patched = {int(p.rsplit("/", 1)[-1]) for m, p, _b in self.fake.writes if m == "PATCH"}
        by_key = {t["key"]: int(t["id"])
                 for t in gh.read_map(self.ops, "billing")["tickets"]}
        self.assertIn(by_key["auth-model"], patched,
                     "the plan promised auth-model's issue would be touched")

    def test_block_patches_both_issue_bodies_too(self):
        """chart() wires its edges through the same `_ensure_edge` path as
        `block`, but exercises a different data source for the graph patch
        (a post-write snapshot vs. block()'s pre-write one) -- so block needs
        its own direct check that both ends render."""
        inp = copy.deepcopy(INPUT)
        del inp["tickets"][0]["blocks"]          # chart with NO edge yet
        out = self.chart(inp)
        by_key = {t["key"]: int(t["id"]) for t in out["tickets"]}
        gh.block(self.ops, "billing", "rollout-order", "auth-model")
        blocked = self.fake.issue(by_key["rollout-order"])["body"]
        blocker = self.fake.issue(by_key["auth-model"])["body"]
        self.assertIn('P0["auth-model"] --> ME', blocked)
        self.assertIn('ME --> C0["rollout-order"]', blocker)

    def test_an_over_long_gist_warns_on_the_tracker_too(self):
        self.chart()
        with captured_stderr() as err:
            gh.resolve(self.ops, "billing", "auth-model",
                       "y" * (map_core.GIST_MAX + 1), None, None)
        self.assertIn("warning:", err.getvalue())
        self.assertIn(str(map_core.GIST_MAX), err.getvalue())

    def test_a_gist_of_exactly_gist_max_does_not_warn_here_either(self):
        """The boundary itself, on both backends, because the threshold is
        shared. `len(flat) > GIST_MAX` tested only at GIST_MAX + 1 and at a short
        string cannot tell `>` from `>=`, and a `>=` would warn on every gist of
        exactly the length the contract documents as fine."""
        self.chart()
        with captured_stderr() as err:
            # A link: the subject here is the GIST boundary, and a grilling ticket
            # with no link also warns about its missing artifact.
            gh.resolve(self.ops, "billing", "auth-model",
                       "y" * map_core.GIST_MAX, "docs/adr/0007-shared-keys.md", None)
        self.assertNotIn("warning:", err.getvalue(),
                         f"a gist of exactly {map_core.GIST_MAX} is within the limit")

    def test_a_grilling_ticket_with_no_link_warns_on_this_backend_too(self):
        """The wording is shared (map_core.MISSING_DOC_LINK) for GIST_TOO_LONG's
        reason: a user who moves a map from local to GitHub must not get a
        different explanation of the same problem."""
        self.chart()
        with captured_stderr() as err:
            out = gh.resolve(self.ops, "billing", "auth-model", "shared keys",
                             None, None)
        # The constant itself, on BOTH backends: that is what makes the shared
        # wording a fact rather than a comment.
        expected = map_core.MISSING_DOC_LINK.format(type="grilling")
        self.assertIn(expected, err.getvalue())
        self.assertEqual(out["warning"], expected)

    def test_a_task_ticket_with_no_link_is_not_asked_for_a_doc(self):
        self.chart()
        with captured_stderr() as err:
            out = gh.resolve(self.ops, "billing", "rollout-order", "tenant A first",
                             None, None)
        self.assertNotIn("no --link", err.getvalue())
        self.assertNotIn("warning", out)

    def _force_only(self, keys, real=True):
        """chart --force naming ONLY `keys`. -> (result json, stderr plan)."""
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [t for t in inp["tickets"] if t["key"] in keys]
        with captured_stderr() as err:
            out = gh.chart(self.ops, inp, real=real, force=True)
        return out, err.getvalue()

    def _body(self, key):
        return self.fake.issue(
            {t["key"]: int(t["id"])
             for t in gh.read_map(self.ops, "billing")["tickets"]}[key])["body"]

    def test_force_on_a_blocker_keeps_the_child_it_still_unblocks(self):
        """--force resets the OVERWRITE'd issue's body to an EMPTY diagram. Its
        own blockers are removed for real (documented), but the edges naming IT
        as a blocker live on the other issues and survive -- so the dependency
        stayed live on GitHub while the picture of it was destroyed, and no
        later run repairs it (`chart` skips the ticket, `_ensure_edge` no-ops on
        an edge that exists)."""
        self.chart()
        self.assertIn('ME --> C0["rollout-order"]', self._body("auth-model"))
        self._force_only(["auth-model"])
        self.assertEqual(
            gh.read_map(self.ops, "billing")["tickets"][1]["blockedBy"],
            ["auth-model"], "the dependency survives --force")
        self.assertIn('ME --> C0["rollout-order"]', self._body("auth-model"),
                      "so the picture of it must survive with it")

    def test_force_clears_a_dead_edge_from_a_blocker_the_input_never_names(self):
        """The mirror harm. --force removes rollout-order's dependencies, which
        is documented -- but auth-model, which this input never mentions, was
        left drawing `ME --> C0["rollout-order"]` for an edge `remove_blocked_by`
        had just deleted. A picture of a dead edge is the staleness-read-as-fact
        harm ADR 0064 exists to prevent."""
        self.chart()
        out, plan = self._force_only(["rollout-order"], real=False)
        entry = next(e for e in out["planned"] if e["path"] == "auth-model")
        self.assertEqual(entry["action"], "merge",
                         "the ADR-0039 gate must show the neighbour write")
        self.assertEqual(entry["detail"],
                         "no longer renders as a child in the graph: rollout-order",
                         "the contract forbids an undescribed merge")
        self.assertIn("no longer renders as a child in the graph", plan)

        self._force_only(["rollout-order"])
        self.assertEqual(gh.read_map(self.ops, "billing")["tickets"][1]["blockedBy"], [])
        self.assertNotIn('C0["rollout-order"]', self._body("auth-model"),
                         "the blocker must stop drawing the edge that was removed")
        _out2, plan2 = self._force_only(["rollout-order"], real=False)
        self.assertNotIn("no longer renders as a child", plan2)


class GhApiSpy(gh.GhApi):
    """GhApi with the subprocess removed, so pacing can be tested without gh."""

    def _run(self, cmd, data):
        return ""


class TestScriptRuns(unittest.TestCase):
    def test_the_script_is_importable_and_has_a_help_screen(self):
        """A syntax error or a bad import would otherwise only surface when a
        skill invokes it for real."""
        p = subprocess.run([sys.executable, os.path.join(
            os.path.dirname(os.path.abspath(gh.__file__)), "github_map_ops.py"),
            "--help"], capture_output=True, text=True)
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("--repo", p.stdout)


class TestLint(Base):
    """lint on a tracker answers the same question as on local markdown, from
    the one snapshot the other read-only subcommands already take."""

    def test_a_freshly_charted_map_lints_clean(self):
        self.chart()
        out = gh.lint(self.ops, "billing")
        self.assertTrue(out["clean"], out["findings"])
        self.assertEqual(out["findings"], [])
        self.assertEqual(out["backend"], "github")

    def test_a_normally_resolved_ticket_stays_clean(self):
        self.chart()
        with captured_stderr():
            gh.resolve(self.ops, "billing", "auth-model", "shared keys",
                       None, "```mermaid\ngraph TD\n  A-->B\n```\n")
        self.assertTrue(gh.lint(self.ops, "billing")["clean"])

    def test_a_closed_ticket_with_no_gist_is_an_error(self):
        """The tracker's machine-readable record IS the gist region, so a
        ticket closed in the UI without resolving is the same defect local
        catches as a closed ticket with no resolution block."""
        out = self.chart()
        auth = next(t for t in out["tickets"] if t["key"] == "auth-model")
        self.ops.patch_issue(int(auth["id"]), {"state": "closed"}, repo=REPO)
        res = gh.lint(self.ops, "billing")
        self.assertFalse(res["clean"])
        f = next(f for f in res["findings"]
                 if f["rule"] == "closed-without-resolution")
        self.assertEqual(f["severity"], "error")
        self.assertIn("gist", f["message"])

    def test_the_diagram_rule_is_declared_unchecked_not_silently_dropped(self):
        """A check that was never run reads as a check that passed. The
        resolution body is a native comment the snapshot does not hold, so the
        rule is named in notChecked rather than quietly skipped."""
        self.chart()
        out = gh.lint(self.ops, "billing")
        self.assertIn("resolution-without-diagram", out["notChecked"])
        self.assertEqual(out["notChecked"],
                         list(map_core.RULES_NEEDING_RESOLUTION_BODY))

    def test_lint_reads_the_snapshot_once(self):
        """The whole value of lint is being cheap enough to run after every
        session; one snapshot is what read and frontier each cost."""
        self.chart()
        before = len(self.fake.calls) if hasattr(self.fake, "calls") else None
        gh.lint(self.ops, "billing")
        if before is not None:
            self.assertLessEqual(len(self.fake.calls) - before, 4,
                                 "lint must not walk per-ticket endpoints")

    def test_the_lint_result_shape_matches_the_local_backend(self):
        """One flow reads either backend (ADR 0062), so the keys cannot differ
        -- including notChecked, which is empty rather than absent on local."""
        import tempfile
        from pathlib import Path as _Path
        import local_map_ops as local
        self.chart()
        gh_out = gh.lint(self.ops, "billing")
        with tempfile.TemporaryDirectory() as tmp:
            root = _Path(tmp)
            local.chart(root, {
                "target": {"slug": "billing"},
                "map": {"title": "t", "destination": "d", "notes": "",
                        "notYetSpecified": [], "outOfScope": []},
                "tickets": [{"key": "a", "title": "A?", "type": "grilling",
                             "question": "q?"}],
            }, real=True)
            local_out = local.lint(root, "billing")
        self.assertEqual(set(gh_out), set(local_out))
        self.assertEqual(local_out["notChecked"], [])

    def test_lint_on_a_map_that_does_not_exist_raises(self):
        self.chart()
        with self.assertRaises(Exception):
            gh.lint(self.ops, "no-such-map")


class GitHubMapRegionsTest(unittest.TestCase):
    """The regions are byte-identical across backends (ADR 0062), which is what
    lets one flow skill read either."""

    def test_a_new_map_issue_carries_both_new_regions(self):
        m = {"title": "t", "destination": "d", "notes": ["one", "two"],
             "milestones": [{"slug": "mvp", "label": "demo it",
                             "members": ["auth-model"]}]}
        body = gh.render_map_issue_body(m, "billing")
        self.assertIn(map_core.NOTES_START, body)
        self.assertIn(map_core.MILESTONES_START, body)
        self.assertIn("- `mvp` demo it [auth-model]", body)
        self.assertIn("- one\n- two", body)

    def test_the_shared_body_matches_the_local_backend_below_the_prologue(self):
        m = {"title": "t", "destination": "d", "notes": ["n"],
             "milestones": [{"slug": "mvp", "members": ["k"]}]}
        shared = map_core.render_map_body(m, "")
        self.assertTrue(gh.render_map_issue_body(m, "billing").endswith(shared))


class GitHubMilestoneProjectionTest(Base):
    """The GitHub half of ADR 0099/0102's milestone projection -- mirrors
    LocalMilestoneProjectionTest so the two backends emit the same fields for
    the same logical map."""

    def setUp(self):
        super().setUp()
        inp = copy.deepcopy(INPUT)
        inp["tickets"].append(
            {"key": "api-limits", "title": "API rate limits?",
             "type": "research", "question": "what are the limits?"})
        inp["map"]["milestones"] = [
            {"slug": "mvp", "label": "demo it",
             "members": ["auth-model", "rollout-order"]},
            {"slug": "later", "members": []}]
        self.chart(inp)

    def test_read_carries_ordered_milestones_and_per_ticket_membership(self):
        out = gh.read_map(self.ops, "billing")
        self.assertEqual([m["slug"] for m in out["milestones"]],
                         ["mvp", "later"])
        self.assertEqual(out["milestones"][0]["label"], "demo it")
        self.assertEqual(out["milestones"][0]["members"],
                         ["auth-model", "rollout-order"])
        by_key = {t["key"]: t for t in out["tickets"]}
        self.assertEqual(by_key["auth-model"]["milestone"], "mvp")
        # An unassigned ticket says so explicitly rather than omitting the key.
        self.assertIsNone(by_key["api-limits"]["milestone"])

    def test_frontier_carries_progress_and_per_entry_membership(self):
        out = gh.frontier(self.ops, "billing")
        mvp = next(m for m in out["milestones"] if m["slug"] == "mvp")
        self.assertEqual((mvp["closed"], mvp["total"], mvp["complete"]),
                         (0, 2, False))
        entry = next(e for e in out["frontier"] if e["key"] == "auth-model")
        self.assertEqual(entry["milestone"], "mvp")
        blocked = next(e for e in out["blocked"] if e["key"] == "rollout-order")
        self.assertEqual(blocked["milestone"], "mvp")
        unassigned = next(e for e in out["frontier"] if e["key"] == "api-limits")
        self.assertIsNone(unassigned["milestone"])

    def test_progress_moves_when_a_member_closes(self):
        gh.resolve(self.ops, "billing", "auth-model", "shared keys", None, None)
        out = gh.frontier(self.ops, "billing")
        mvp = next(m for m in out["milestones"] if m["slug"] == "mvp")
        self.assertEqual((mvp["closed"], mvp["total"], mvp["complete"]),
                         (1, 2, False))

    def test_a_claimed_entry_also_carries_its_milestone(self):
        gh.claim(self.ops, "billing", "auth-model", "octocat")
        out = gh.frontier(self.ops, "billing")
        self.assertEqual(out["claimed"][0]["milestone"], "mvp")

    def test_an_unmilestoned_map_reports_an_empty_list(self):
        # The key is always present so a consumer never branches on absence.
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "plain"
        self.chart(inp)
        self.assertEqual(gh.read_map(self.ops, "plain")["milestones"], [])
        self.assertEqual(gh.frontier(self.ops, "plain")["milestones"], [])

    def test_a_new_ticket_issue_leads_with_its_question(self):
        body = gh.render_ticket_issue_body("k", "which one?")
        self.assertLess(body.index("## Question"), body.index(map_core.GRAPH_START))
        self.assertEqual(body.count(map_core.KEY_MARKER % "k"), 1)
        self.assertEqual(body.count(map_core.GIST_START), 1)

    def test_the_two_backends_report_the_same_milestone_fields(self):
        gh_ticket = next(t for t in gh.read_map(self.ops, "billing")["tickets"]
                         if t["key"] == "auth-model")
        with tempfile.TemporaryDirectory() as tmp:
            local_inp = {
                "target": {"slug": "billing"},
                "map": {"title": "t", "destination": "d",
                        "milestones": [{"slug": "mvp",
                                        "members": ["auth-model"]}]},
                "tickets": [{"key": "auth-model", "title": "a",
                             "type": "grilling", "question": "q",
                             "blocks": []}],
            }
            local_map_ops.chart(Path(tmp), local_inp, real=True)
            local_ticket = local_map_ops.read_map(Path(tmp), "billing")["tickets"][0]
        # dbId and repo are GitHub-only handles; every other field name and
        # presence must match, or the two backends disagree about the shape
        # of a ticket for the same logical map.
        self.assertEqual(set(gh_ticket) - {"dbId", "repo"}, set(local_ticket))


if __name__ == "__main__":
    unittest.main(verbosity=2)
