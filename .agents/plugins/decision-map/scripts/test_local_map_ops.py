import contextlib
import copy
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import local_map_ops as ops
import map_core


def _snapshot(base):
    """Return {relative posix path: sha256 hexdigest} for every file under
    `base`. Used to prove a dry run truly wrote nothing (round-2 finding N5:
    the prior assertion compared one file's contents to itself and could
    never fail, regardless of what chart() actually did)."""
    snap = {}
    if not base.exists():
        return snap
    for p in sorted(base.rglob("*")):
        if p.is_file():
            snap[p.relative_to(base).as_posix()] = hashlib.sha256(p.read_bytes()).hexdigest()
    return snap

# Derived by scanning, not hand-listed (round 5, N7): every character
# str.splitlines() treats as a line break. _fm_parse splits the frontmatter
# block with splitlines(), so the writer's normaliser must cover exactly this
# set. Scanning to 0x2200 covers all of them with margin; a separate one-off
# scan of the full 0x110000 range confirmed there are no others.
_SPLITLINES_SEPARATORS = [chr(c) for c in range(0x2200)
                          if len(("a" + chr(c) + "b").splitlines()) > 1]

INPUT = {
    "target": {"slug": "example-effort"},
    "map": {"title": "Decision map — example", "destination": "a spec",
            "notes": "use grill-with-docs",
            "notYetSpecified": ["how to deploy"], "outOfScope": ["mobile app"]},
    "tickets": [
        {"key": "auth-model", "title": "Auth model?", "type": "grilling",
         "question": "per-tenant or shared?", "blocks": ["rollout-order"]},
        {"key": "rollout-order", "title": "Rollout order?", "type": "grilling",
         "question": "which env first?", "blocks": []},
        {"key": "api-limits", "title": "API rate limits?", "type": "research",
         "question": "what are the limits?", "blocks": []},
    ],
}

class LocalMapOpsTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def _chart(self):
        return ops.chart(self.root, INPUT, real=True)

    def test_chart_dry_run_writes_nothing(self):
        ops.chart(self.root, INPUT, real=False)
        self.assertFalse((self.root / "example-effort").exists())

    def test_chart_creates_map_and_tickets(self):
        out = self._chart()
        self.assertTrue((self.root / "example-effort" / "map.md").exists())
        self.assertTrue((self.root / "example-effort" / "tickets" / "auth-model.md").exists())
        self.assertEqual(out["backend"], "local")
        self.assertEqual(len(out["tickets"]), 3)
        modes = {t["key"]: t["mode"] for t in out["tickets"]}
        self.assertEqual(modes["api-limits"], "AFK")
        self.assertEqual(modes["auth-model"], "HITL")

    def test_frontier_excludes_blocked_and_claimed(self):
        self._chart()
        ops.claim(self.root, "example-effort", "api-limits", "pon")
        f = ops.frontier(self.root, "example-effort")
        names = [t["id"] for t in f["frontier"]]
        self.assertIn("auth-model", names)          # open, unblocked, unclaimed
        self.assertNotIn("rollout-order", names)     # blocked by auth-model
        self.assertNotIn("api-limits", names)        # claimed
        self.assertEqual(f["blocked"][0]["blockedBy"], ["auth-model"])
        # P1 regression guard: read_map()'s ticket dicts must expose the
        # upstream-blocker relation under the key "blockedBy" (output shape),
        # never "blocked_by" (that key name is the on-disk frontmatter format).
        m = ops.read_map(self.root, "example-effort")
        rollout = next(t for t in m["tickets"] if t["key"] == "rollout-order")
        self.assertEqual(rollout["blockedBy"], ["auth-model"])
        self.assertNotIn("blocked_by", rollout)

    def test_frontier_on_a_missing_map_fails_instead_of_reporting_done(self):
        # Three empty buckets and exit 0 is indistinguishable from "every
        # decision is resolved", and work-map renders exactly that to the
        # user. A map that does not exist must fail like read_map does.
        with self.assertRaises(OSError):
            ops.frontier(self.root, "no-such-map")

    def test_a_deleted_blocker_does_not_silently_unblock_its_dependents(self):
        self._chart()
        # rollout-order is blocked by auth-model. Delete the blocker the way a
        # human would on a tracker -- the item is simply gone.
        (self.root / "example-effort" / "tickets" / "auth-model.md").unlink()
        f = ops.frontier(self.root, "example-effort")
        self.assertNotIn("rollout-order", [t["id"] for t in f["frontier"]],
                         "a blocker that no longer exists must not count as satisfied")
        blocked = next(t for t in f["blocked"] if t["id"] == "rollout-order")
        self.assertEqual(blocked["blockedBy"], ["auth-model"])
        # and the missing blocker must be announced, not swallowed
        self.assertEqual(blocked.get("missingBlockers"), ["auth-model"])

    def test_resolve_closes_and_indexes(self):
        self._chart()
        ops.resolve(self.root, "example-effort", "auth-model",
                    "per-tenant keys", link="docs/adr/0007-x.md", body=None)
        m = ops.read_map(self.root, "example-effort")
        t = next(t for t in m["tickets"] if t["key"] == "auth-model")
        self.assertEqual(t["status"], "closed")
        self.assertEqual(t["gist"], "per-tenant keys")
        map_md = (self.root / "example-effort" / "map.md").read_text(encoding="utf-8")
        self.assertIn("[Auth model?](tickets/auth-model.md) — per-tenant keys", map_md)
        # S2 (negative): the index is a projection of CLOSED tickets only.
        # Including open ones survived the whole suite, and makes the map
        # advertise an undecided question as a recorded decision with an empty
        # gist -- worse than omitting it, because a reader trusts the index.
        self.assertNotIn("tickets/api-limits.md", map_md,
                         "an OPEN ticket must not appear in Decisions so far")
        # unblocking: rollout-order now on the frontier
        f = ops.frontier(self.root, "example-effort")
        self.assertIn("rollout-order", [t["id"] for t in f["frontier"]])
        # S1 (negative): a closed ticket belongs in NO frontier bucket.
        # Deleting frontier()'s closed filter survived the whole suite, and
        # the mutant reports closed decisions as claimed and a closed research
        # ticket as takeable -- that predicate is what makes the
        # one-decision-per-session loop terminate at all.
        self.assertNotIn("auth-model",
                         [t["id"] for t in f["frontier"]]
                         + [t["id"] for t in f["blocked"]]
                         + [t["id"] for t in f["claimed"]],
                         "a closed ticket must not appear in any frontier bucket")

    def test_block_adds_edge(self):
        self._chart()
        result = ops.block(self.root, "example-effort", "api-limits", "auth-model")
        # Finding 6: block()'s return value was never asserted, so the P1
        # rename (blocked_by -> blockedBy) was untested — reverting rename #3
        # alone left all tests green. Lock the actual return shape down here.
        self.assertEqual(result, {"ticket": "api-limits", "blockedBy": ["auth-model"]})
        self.assertNotIn("blocked_by", result)
        f = ops.frontier(self.root, "example-effort")
        self.assertNotIn("api-limits", [t["id"] for t in f["frontier"]])

    def test_block_renders_the_edge_at_both_ends(self):
        self._chart()
        ops.block(self.root, "example-effort", "api-limits", "auth-model")
        base = self.root / "example-effort" / "tickets"
        blocked = (base / "api-limits.md").read_text(encoding="utf-8")
        blocker = (base / "auth-model.md").read_text(encoding="utf-8")
        self.assertIn('P0["auth-model"] --> ME', blocked,
                      "the blocked ticket shows its blocker as a parent")
        self.assertIn('ME --> C0["api-limits"]', blocker,
                      "the blocker shows what it unblocks as a child")

    def test_re_blocking_an_existing_edge_writes_nothing(self):
        self._chart()
        ops.block(self.root, "example-effort", "api-limits", "auth-model")
        frozen = _snapshot(self.root / "example-effort")
        ops.block(self.root, "example-effort", "api-limits", "auth-model")
        self.assertEqual(_snapshot(self.root / "example-effort"), frozen)

    # ------------------------------------------------------------------
    # Fix round 1 — regression tests for review findings
    # ------------------------------------------------------------------

    # Finding 1 (Critical): re-charting an existing map folder must never
    # silently destroy recorded state (claims, resolutions, blocking edges).
    # REWRITTEN for Task 3b / ADR 0057: the guard is unchanged in substance --
    # recorded state must survive an un-forced re-chart -- but the mechanism
    # is now "skip every existing file" rather than "refuse the whole run".
    # Byte-identity is a strictly stronger assertion than the old one.
    def test_rechart_skips_existing_files_by_default(self):
        self._chart()
        ops.resolve(self.root, "example-effort", "auth-model",
                    "per-tenant keys", link=None, body=None)
        ops.claim(self.root, "example-effort", "rollout-order", "pon")
        before = _snapshot(self.root / "example-effort")
        ops.chart(self.root, INPUT, real=True)          # no longer raises
        self.assertEqual(_snapshot(self.root / "example-effort"), before)
        # nothing was destroyed
        m = ops.read_map(self.root, "example-effort")
        auth = next(t for t in m["tickets"] if t["key"] == "auth-model")
        rollout = next(t for t in m["tickets"] if t["key"] == "rollout-order")
        self.assertEqual(auth["status"], "closed")
        self.assertEqual(auth["gist"], "per-tenant keys")
        self.assertEqual(rollout["assignee"], "pon")

    # REWRITTEN for Task 3b: the action vocabulary lost "refuse" and gained
    # "skip (exists)". The dry-run-writes-nothing guard (round-2 N5) is
    # unchanged and still verified by content hash.
    def test_rechart_dry_run_reports_create_skip_overwrite_accurately(self):
        self._chart()
        base = self.root / "example-effort"
        before = _snapshot(base)
        self.assertTrue(before, "sanity: the chart fixture must have written files to snapshot")
        # default (force=False): every existing file must be reported "skip (exists)"
        out_default = ops.chart(self.root, INPUT, real=False)
        actions_default = {p["path"]: p["action"] for p in out_default["planned"]}
        self.assertTrue(actions_default)
        self.assertTrue(all(a == "skip (exists)" for a in actions_default.values()),
                        actions_default)
        self.assertNotIn("refuse", set(actions_default.values()))
        # dry run never writes, regardless of force -- verified via a real
        # content hash snapshot (round-2 finding N5: the prior version of
        # this assertion compared one file's contents to itself, which is
        # vacuously true no matter what chart() does; a mutant that made a
        # --force dry run clobber every file still passed the whole suite).
        self.assertEqual(_snapshot(base), before)
        # force=True: same files must be reported "OVERWRITE", not silently "create"
        out_force = ops.chart(self.root, INPUT, real=False, force=True)
        actions_force = {p["path"]: p["action"] for p in out_force["planned"]}
        self.assertTrue(all(a == "OVERWRITE" for a in actions_force.values()))
        self.assertEqual(_snapshot(base), before)

    def test_rechart_with_force_overwrites_explicitly(self):
        self._chart()
        ops.resolve(self.root, "example-effort", "auth-model",
                    "per-tenant keys", link=None, body=None)
        ops.chart(self.root, INPUT, real=True, force=True)
        m = ops.read_map(self.root, "example-effort")
        auth = next(t for t in m["tickets"] if t["key"] == "auth-model")
        # explicit, informed opt-in: fresh content wins
        self.assertEqual(auth["status"], "open")

    # Bundled "validate the input" fixes, alongside finding 4
    def test_chart_rejects_unsafe_ticket_key(self):
        bad = copy.deepcopy(INPUT)
        bad["tickets"][0]["key"] = "../../pwned"
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        self.assertFalse((self.root / "example-effort").exists())

    def test_chart_rejects_invalid_ticket_type(self):
        bad = copy.deepcopy(INPUT)
        bad["tickets"][0]["type"] = "reserch"
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        self.assertFalse((self.root / "example-effort").exists())

    # Finding 4: an unknown `blocks` target must fail validation before any
    # file is written — not crash pass 2 with a half-written map folder.
    def test_chart_rejects_unknown_blocks_target(self):
        bad = copy.deepcopy(INPUT)
        bad["tickets"][0]["blocks"] = ["ghost-ticket"]
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        self.assertFalse((self.root / "example-effort").exists())

    # Finding 2: a frontmatter value that merely looks bracketed must not be
    # coerced into a list — only the known `blocked_by` field is a list.
    def test_bracket_and_comma_values_are_not_coerced_to_lists(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "brackets-effort"
        inp["tickets"] = [
            {"key": "spike", "title": "[spike] rollout order [v2]", "type": "task",
             "question": "q?", "blocks": []},
        ]
        ops.chart(self.root, inp, real=True)
        m = ops.read_map(self.root, "brackets-effort")
        t = next(t for t in m["tickets"] if t["key"] == "spike")
        self.assertEqual(t["name"], "[spike] rollout order [v2]")
        ops.resolve(self.root, "brackets-effort", "spike",
                    "[deferred to ADR 0007, see notes]", link=None, body=None)
        m2 = ops.read_map(self.root, "brackets-effort")
        t2 = next(t for t in m2["tickets"] if t["key"] == "spike")
        self.assertEqual(t2["gist"], "[deferred to ADR 0007, see notes]")

    def test_newline_in_title_does_not_corrupt_frontmatter(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "newline-effort"
        inp["tickets"] = [
            {"key": "nl-ticket", "title": "Multi\nline title", "type": "task",
             "question": "q?", "blocks": []},
        ]
        ops.chart(self.root, inp, real=True)
        m = ops.read_map(self.root, "newline-effort")
        t = next(t for t in m["tickets"] if t["key"] == "nl-ticket")
        self.assertNotIn("\n", t["name"])
        # the continuation after the embedded newline must survive (collapsed
        # to a space), not be silently dropped -- a bare title-truncation
        # check here would pass even on the buggy parser, because the
        # dropped continuation line ("line title") has no ":" in it and so
        # doesn't yet corrupt the *next* key's parse.
        self.assertEqual(t["name"], "Multi line title")
        # subsequent frontmatter keys must parse cleanly (not corrupted/dropped)
        self.assertEqual(t["type"], "task")
        self.assertEqual(t["mode"], "HITL")
        self.assertEqual(t["status"], "open")

    # Finding 3: resolve() must be re-entrant.
    def test_resolve_is_idempotent(self):
        self._chart()
        ops.resolve(self.root, "example-effort", "auth-model", "first gist", link=None, body=None)
        ops.resolve(self.root, "example-effort", "auth-model", "second gist", link=None, body=None)
        ticket_md = (self.root / "example-effort" / "tickets" / "auth-model.md").read_text(encoding="utf-8")
        self.assertEqual(ticket_md.count("## Resolution"), 1)
        self.assertIn("second gist", ticket_md)
        self.assertNotIn("first gist", ticket_md)
        map_md = (self.root / "example-effort" / "map.md").read_text(encoding="utf-8")
        self.assertEqual(map_md.count("tickets/auth-model.md"), 1)
        self.assertIn("second gist", map_md)
        self.assertNotIn("first gist", map_md)

    # Finding 5: urls must use forward slashes so `[name](url)` markdown links
    # aren't broken by backslash (Markdown's escape character) on Windows.
    def test_urls_use_forward_slashes(self):
        out = self._chart()
        for t in out["tickets"]:
            self.assertNotIn("\\", t["url"])
        m = ops.read_map(self.root, "example-effort")
        self.assertNotIn("\\", m["map"]["url"])
        for t in m["tickets"]:
            self.assertNotIn("\\", t["url"])
        f = ops.frontier(self.root, "example-effort")
        for t in f["frontier"]:
            self.assertNotIn("\\", t["url"])

    # Finding 6: comment() had no coverage at all.
    def test_comment_appends_section(self):
        self._chart()
        result = ops.comment(self.root, "example-effort", "auth-model",
                              "checking with security team")
        self.assertEqual(result, {"commented": "auth-model"})
        ticket_md = (self.root / "example-effort" / "tickets" / "auth-model.md").read_text(encoding="utf-8")
        self.assertIn("## Comment", ticket_md)
        self.assertIn("checking with security team", ticket_md)

    # ------------------------------------------------------------------
    # Fix round 2 — regression tests for review findings N1-N3, N5
    # ------------------------------------------------------------------

    # N1 (Important, regression from the round-1 fix): the round-1 fix made
    # resolve() strip "## Resolution\n.*\Z" (anchored to end-of-string) before
    # re-appending -- but comment() can append AFTER a resolve (the CLI has no
    # closed-ticket guard on comment), so a second resolve()'s \Z-anchored
    # strip deleted the user-authored comment along with the stale Resolution.
    def test_resolve_after_comment_preserves_the_comment(self):
        self._chart()
        ops.resolve(self.root, "example-effort", "auth-model", "first gist", link=None, body=None)
        ops.comment(self.root, "example-effort", "auth-model", "user comment after resolve")
        ops.resolve(self.root, "example-effort", "auth-model", "second gist", link=None, body=None)
        ticket_md = (self.root / "example-effort" / "tickets" / "auth-model.md").read_text(encoding="utf-8")
        self.assertIn("user comment after resolve", ticket_md)
        self.assertEqual(ticket_md.count("## Resolution"), 1)
        self.assertIn("second gist", ticket_md)
        self.assertNotIn("first gist", ticket_md)

    # N2 (Important): target.slug was never validated, so it could carry a
    # ticket key straight past the map folder -- direct-probe-confirmed
    # against HEAD: "../../pwned-slug" landed two directories above the temp
    # root, and "C:/Windows/Temp/pwned-slug" wrote directly under
    # C:\Windows\Temp. Both payloads plus one more (absolute unix-style path)
    # must now be rejected before any file is written.
    def test_chart_rejects_unsafe_map_slug_dotdot(self):
        bad = copy.deepcopy(INPUT)
        bad["target"]["slug"] = "../../pwned-slug-n2"
        escape_target = (self.root / "../../pwned-slug-n2").resolve()
        try:
            with self.assertRaises(ops.ChartValidationError):
                ops.chart(self.root, bad, real=True)
            self.assertFalse(escape_target.exists())
        finally:
            if escape_target.exists():
                shutil.rmtree(escape_target, ignore_errors=True)

    def test_chart_rejects_unsafe_map_slug_drive_path(self):
        bad = copy.deepcopy(INPUT)
        bad["target"]["slug"] = "C:/Windows/Temp/pwned-slug-n2b"
        escape_target = Path("C:/Windows/Temp/pwned-slug-n2b")
        try:
            with self.assertRaises(ops.ChartValidationError):
                ops.chart(self.root, bad, real=True)
            self.assertFalse(escape_target.exists())
        finally:
            if escape_target.exists():
                shutil.rmtree(escape_target, ignore_errors=True)

    def test_chart_rejects_unsafe_map_slug_absolute_unix_path(self):
        bad = copy.deepcopy(INPUT)
        bad["target"]["slug"] = "/etc/pwned-slug-n2c"
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)

    # N3 (Minor, but directly protects round-1's Critical): _SAFE_SLUG_RE
    # used "$" (which, in Python, also matches just before a trailing
    # newline) instead of "\Z". A key like "okname\n" therefore passed
    # validation, then died with OSError while writing the ticket file --
    # AFTER map.md was already on disk (the exact half-written state the
    # module docstring claims can't happen). Must now be rejected up front.
    def test_chart_rejects_ticket_key_with_trailing_newline(self):
        bad = copy.deepcopy(INPUT)
        bad["tickets"][0]["key"] = "okname\n"
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        self.assertFalse((self.root / "example-effort").exists())

    # ------------------------------------------------------------------
    # Fix round 3 — regression tests for review findings R1-R3
    # ------------------------------------------------------------------

    # R1 (Important, introduced by round 2's fix): a --body-file containing
    # its own "## " sub-heading (e.g. "## Rationale") made the round-2
    # lookahead-based strip stop INSIDE the old Resolution block, orphaning
    # its tail. Each re-resolve leaves one more stale sub-heading behind --
    # unbounded accumulation, reintroducing round-1 finding 3's harm through
    # a documented, ordinary --resolve --body-file argument.
    def test_resolve_with_heading_in_body_does_not_accumulate(self):
        self._chart()
        ops.resolve(self.root, "example-effort", "auth-model", "gist one",
                    link=None, body="## Rationale\n\nfirst reasoning")
        ops.resolve(self.root, "example-effort", "auth-model", "gist two",
                    link=None, body="## Rationale\n\nsecond reasoning")
        ops.resolve(self.root, "example-effort", "auth-model", "gist three",
                    link=None, body="## Rationale\n\nthird reasoning")
        ticket_md = (self.root / "example-effort" / "tickets" / "auth-model.md").read_text(encoding="utf-8")
        self.assertEqual(ticket_md.count("## Resolution"), 1)
        self.assertEqual(ticket_md.count("## Rationale"), 1)
        self.assertIn("gist three", ticket_md)
        self.assertIn("third reasoning", ticket_md)
        self.assertNotIn("gist one", ticket_md)
        self.assertNotIn("gist two", ticket_md)
        self.assertNotIn("first reasoning", ticket_md)
        self.assertNotIn("second reasoning", ticket_md)

    # R2 (Important, residual of round-2's N1 fix -- narrowed, not closed):
    # the strip regex has no "^"/MULTILINE anchor and its leading "\n*" can
    # match zero characters, so the literal substring "## Resolution\n" is
    # matched WHEREVER it appears -- including inside a ticket's own Question
    # text, at a line start or truly mid-line -- deleting real user content on
    # the very FIRST resolve(), before any resolution has ever been recorded.
    def test_resolve_first_call_does_not_corrupt_question_with_heading_text_at_line_start(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "r2-line-effort"
        question = "Should the process include\n## Resolution\nas a required governance step?"
        inp["tickets"] = [
            {"key": "line-start", "title": "Process question", "type": "task",
             "question": question, "blocks": []},
        ]
        ops.chart(self.root, inp, real=True)
        ops.resolve(self.root, "r2-line-effort", "line-start", "resolved", link=None, body=None)
        ticket_md = (self.root / "r2-line-effort" / "tickets" / "line-start.md").read_text(encoding="utf-8")
        self.assertIn("as a required governance step?", ticket_md)

    def test_resolve_first_call_does_not_corrupt_question_with_mid_line_heading_text(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "r2-mid-effort"
        question = "Consider this idea: ## Resolution\nand what it implies for our timeline?"
        inp["tickets"] = [
            {"key": "mid-line", "title": "Inline question", "type": "task",
             "question": question, "blocks": []},
        ]
        ops.chart(self.root, inp, real=True)
        ops.resolve(self.root, "r2-mid-effort", "mid-line", "resolved", link=None, body=None)
        ticket_md = (self.root / "r2-mid-effort" / "tickets" / "mid-line.md").read_text(encoding="utf-8")
        self.assertIn("and what it implies for our timeline?", ticket_md)

    # R3 (Minor): the module docstring claims malformed input "fails cleanly
    # ... instead of writing a half-finished map folder" -- false for a
    # ticket missing "title" or "question" (or the map missing "title"/
    # "destination"): chart() raised a bare KeyError mid-pass-1, after map.md
    # and earlier tickets were already on disk, and the resulting partial
    # folder then trips refuse-by-default on retry, nudging the user toward
    # the destructive --force path.
    def test_chart_rejects_ticket_missing_required_field(self):
        bad = copy.deepcopy(INPUT)
        del bad["tickets"][1]["question"]  # rollout-order, second in the list
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        # must fail before ANY file is written -- not even auth-model.md
        # (the first, valid ticket), which chart's pass-1 loop would already
        # have written to disk by the time it reached the invalid one
        self.assertFalse((self.root / "example-effort").exists())

    def test_chart_rejects_map_missing_required_field(self):
        bad = copy.deepcopy(INPUT)
        del bad["map"]["destination"]
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        self.assertFalse((self.root / "example-effort").exists())

    # ------------------------------------------------------------------
    # Fix round 4 — regression tests for review findings N1, N2
    #
    # N1 (Important): round 3 replaced the needle ("## Resolution" -> a
    # namespaced HTML comment) but not the MECHANISM -- resolve() still
    # located the region it owns by pattern-searching a body whose every byte
    # is user-controlled (chart() writes `question` verbatim, comment() writes
    # `body_text`, resolve() writes `gist`/`link`/`body`). A user string
    # carrying a marker therefore reproduced BOTH earlier harms: unbounded
    # accumulation (round-1 finding 3) via an END marker, and silent deletion
    # of user text (round-2 finding N1) via a START marker. The fix escapes
    # every marker occurrence in every user-supplied string on the way in, so
    # "only resolve() ever writes these markers" is enforced, not asserted.
    # ------------------------------------------------------------------

    def _ticket_text(self, slug, ticket):
        return (self.root / slug / "tickets" / f"{ticket}.md").read_text(encoding="utf-8")

    # N1 harm (a): an END marker inside --body-file made the non-greedy match
    # stop early -- 5 resolves left 5 orphaned tails and 5 stray markers, the
    # file growing 274 -> 478 chars monotonically.
    def test_resolve_body_containing_end_marker_does_not_accumulate(self):
        self._chart()
        lengths = []
        for i in range(1, 6):
            ops.resolve(self.root, "example-effort", "auth-model", f"gist-{i}",
                        link=None,
                        body=f"Reasoning {i}.\n{ops._RESOLUTION_END}\nTAIL-{i}")
            text = self._ticket_text("example-effort", "auth-model")
            lengths.append(len(text))
            self.assertEqual(text.count(ops._RESOLUTION_START), 1,
                             f"resolve #{i}: exactly one region start expected")
            self.assertEqual(text.count(ops._RESOLUTION_END), 1,
                             f"resolve #{i}: exactly one region end expected")
            self.assertIn(f"TAIL-{i}", text, f"resolve #{i}: own body tail lost")
            self.assertIn("per-tenant or shared?", text)
        final = self._ticket_text("example-effort", "auth-model")
        for i in range(1, 5):
            self.assertNotIn(f"TAIL-{i}\n", final)   # no orphaned tails
            self.assertNotIn(f"gist-{i}", final)     # no stale gists
        # every payload is the same length, so an unchanging file length is a
        # direct assertion that nothing accumulated across the 5 resolves
        self.assertEqual(lengths, [lengths[0]] * 5,
                         f"file grew across resolves: {lengths}")

    # N1 harm (b): a Question carrying a well-formed marker PAIR lost its
    # inner text on the very FIRST resolve.
    def test_resolve_does_not_eat_a_marker_pair_inside_the_question(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "pair-effort"
        question = (f"Should we do X?\n{ops._RESOLUTION_START}\n## Resolution\n\n"
                    f"USER-AUTHORED-EXAMPLE\n{ops._RESOLUTION_END}\nAnd what about Y?")
        inp["tickets"] = [{"key": "t1", "title": "T one", "type": "task",
                           "question": question, "blocks": []}]
        ops.chart(self.root, inp, real=True)
        ops.resolve(self.root, "pair-effort", "t1", "answered", link=None, body=None)
        text = self._ticket_text("pair-effort", "t1")
        self.assertIn("USER-AUTHORED-EXAMPLE", text)
        self.assertIn("Should we do X?", text)
        self.assertIn("And what about Y?", text)
        # exactly one live region -- resolve()'s own; the Question's copies are
        # escaped, so they are text, not markers
        self.assertEqual(text.count(ops._RESOLUTION_START), 1)
        self.assertEqual(text.count(ops._RESOLUTION_END), 1)

    # N1 harm (b), worse variant: a Question carrying only the START marker
    # lost its prose and its trailing sentence on the SECOND resolve.
    def test_resolve_does_not_eat_prose_after_a_lone_start_marker_in_the_question(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "lone-effort"
        question = (f"Should we do X?\n{ops._RESOLUTION_START}\n"
                    f"IMPORTANT-USER-PROSE-AFTER-FAKE-START\nAnd Y?")
        inp["tickets"] = [{"key": "t1", "title": "T one", "type": "task",
                           "question": question, "blocks": []}]
        ops.chart(self.root, inp, real=True)
        ops.resolve(self.root, "lone-effort", "t1", "first", link=None, body=None)
        ops.comment(self.root, "lone-effort", "t1", "a human note")
        ops.resolve(self.root, "lone-effort", "t1", "second", link=None, body=None)
        text = self._ticket_text("lone-effort", "t1")
        self.assertIn("IMPORTANT-USER-PROSE-AFTER-FAKE-START", text)
        self.assertIn("And Y?", text)
        self.assertIn("a human note", text)
        self.assertIn("second", text)
        self.assertNotIn("first", text)
        self.assertEqual(text.count(ops._RESOLUTION_START), 1)
        self.assertEqual(text.count(ops._RESOLUTION_END), 1)

    # N1 harm (b), the exact harm round 2 was supposed to close: a comment()
    # written BEFORE the first resolve, carrying a START marker, was deleted
    # by the second resolve.
    def test_resolve_does_not_delete_an_early_comment_containing_a_marker(self):
        self._chart()
        ops.comment(self.root, "example-effort", "auth-model",
                    f"heads up {ops._RESOLUTION_START} IMPORTANT-COMMENT-BODY")
        ops.resolve(self.root, "example-effort", "auth-model", "first", link=None, body=None)
        ops.resolve(self.root, "example-effort", "auth-model", "second", link=None, body=None)
        text = self._ticket_text("example-effort", "auth-model")
        self.assertIn("IMPORTANT-COMMENT-BODY", text)
        self.assertIn("second", text)
        self.assertNotIn("first", text)
        self.assertEqual(text.count(ops._RESOLUTION_START), 1)
        self.assertEqual(text.count(ops._RESOLUTION_END), 1)

    # N1, leak half: a marker in --gist / --link leaked verbatim into map.md's
    # Decisions-so-far index line and into the `gist` field of read/frontier.
    def test_markers_in_gist_and_link_do_not_leak_into_map_or_json(self):
        self._chart()
        ops.resolve(self.root, "example-effort", "auth-model",
                    f"answer {ops._RESOLUTION_END} tail",
                    link=f"docs/{ops._RESOLUTION_START}.md", body=None)
        map_md = (self.root / "example-effort" / "map.md").read_text(encoding="utf-8")
        self.assertNotIn(ops._RESOLUTION_START, map_md)
        self.assertNotIn(ops._RESOLUTION_END, map_md)
        self.assertIn("answer", map_md)
        m = ops.read_map(self.root, "example-effort")
        auth = next(t for t in m["tickets"] if t["key"] == "auth-model")
        self.assertNotIn(ops._RESOLUTION_START, auth["gist"])
        self.assertNotIn(ops._RESOLUTION_END, auth["gist"])
        text = self._ticket_text("example-effort", "auth-model")
        self.assertEqual(text.count(ops._RESOLUTION_START), 1)
        self.assertEqual(text.count(ops._RESOLUTION_END), 1)

    # N1, extension: map.md's Decisions-so-far index was found by the same
    # class of pattern search (a "^- [...](tickets/<key>.md)" line regex over
    # the whole file), so a map `notes` line shaped like an index entry was
    # substituted away by resolve(). The index is now a generated region and
    # is rebuilt from the ticket files, so nothing outside it is ever touched.
    def test_resolve_does_not_clobber_an_index_shaped_line_in_notes(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "notes-effort"
        inp["map"] = dict(inp["map"])
        inp["map"]["notes"] = ("prior art:\n"
                               "- [Auth model?](tickets/auth-model.md) — USER-AUTHORED-NOTE\n"
                               "keep reading")
        ops.chart(self.root, inp, real=True)
        ops.resolve(self.root, "notes-effort", "auth-model", "REAL-GIST",
                    link=None, body=None)
        map_md = (self.root / "notes-effort" / "map.md").read_text(encoding="utf-8")
        self.assertIn("USER-AUTHORED-NOTE", map_md)
        self.assertIn("keep reading", map_md)
        self.assertIn("REAL-GIST", map_md)
        self.assertEqual(map_md.count("tickets/auth-model.md"), 2)  # the note + the index entry
        # re-resolving still updates exactly the generated entry
        ops.resolve(self.root, "notes-effort", "auth-model", "SECOND-GIST",
                    link=None, body=None)
        map_md = (self.root / "notes-effort" / "map.md").read_text(encoding="utf-8")
        self.assertIn("USER-AUTHORED-NOTE", map_md)
        self.assertIn("SECOND-GIST", map_md)
        self.assertNotIn("REAL-GIST", map_md)
        self.assertEqual(map_md.count("tickets/auth-model.md"), 2)

    def test_an_over_long_gist_warns_but_is_still_recorded(self):
        self._chart()
        long_gist = "x" * (map_core.GIST_MAX + 1)
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            out = ops.resolve(self.root, "example-effort", "auth-model",
                              long_gist, None, None)
        self.assertIn("warning:", err.getvalue())
        self.assertIn(str(map_core.GIST_MAX), err.getvalue())
        self.assertEqual(out["resolved"], "auth-model")
        text = (self.root / "example-effort" / "tickets" / "auth-model.md").read_text(
            encoding="utf-8")
        self.assertIn(long_gist, text, "the answer is recorded regardless (ADR 0066)")

    def test_a_short_gist_warns_about_nothing(self):
        self._chart()
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            ops.resolve(self.root, "example-effort", "auth-model", "short answer",
                        None, None)
        self.assertNotIn("warning:", err.getvalue())

    def test_a_gist_of_exactly_gist_max_does_not_warn(self):
        """The boundary itself. The condition is `len(flat) > GIST_MAX`, and the
        two tests around it pin GIST_MAX + 1 and a short string -- neither of
        which can tell `>` from `>=`. A `>=` typo would warn on every gist of
        exactly the documented limit, i.e. tell the user the map index is
        unreadable at the length the contract says is fine."""
        self._chart()
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            ops.resolve(self.root, "example-effort", "auth-model",
                        "x" * map_core.GIST_MAX, None, None)
        self.assertNotIn("warning:", err.getvalue(),
                         f"a gist of exactly {map_core.GIST_MAX} is within the limit")

    # N1, backstop: the escape is the prevention, but every write also asserts
    # the file holds at most one well-formed region. A hand-edited file with a
    # stray marker must make the module REFUSE to write, not silently corrupt.
    def test_write_refuses_when_a_hand_edited_file_has_stray_markers(self):
        self._chart()
        p = self.root / "example-effort" / "tickets" / "auth-model.md"
        p.write_text(p.read_text(encoding="utf-8") + f"\n{ops._RESOLUTION_END}\n",
                     encoding="utf-8")
        with self.assertRaises(ops.MarkerIntegrityError):
            ops.claim(self.root, "example-effort", "auth-model", "pon")

    # ------------------------------------------------------------------
    # N2 (Minor, re-opens R3): validation was presence-only, so a wrong-TYPED
    # field reproduced R3's exact partial-folder harm -- `title: [1, 2]` raised
    # TypeError from _fm_dump AFTER map.md and the first ticket were on disk --
    # while `title: null`, a dict, an int and `question: null` were silently
    # accepted and written. Validate types in the same pre-write pass.
    # ------------------------------------------------------------------

    def _assert_rejected_without_writing(self, bad):
        """chart() must raise ChartValidationError and leave the filesystem
        byte-identical -- not even the <slug>/ directory, which would trip
        refuse-by-default on retry and push the user toward --force."""
        before = _snapshot(self.root)
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        self.assertEqual(_snapshot(self.root), before,
                         "validation must run before ANY write")
        self.assertFalse(any(self.root.iterdir()))

    def test_chart_rejects_wrong_typed_ticket_fields(self):
        for field, value in [("title", [1, 2]), ("title", None), ("title", {"a": 1}),
                             ("title", 42), ("question", None), ("question", 7),
                             ("type", None), ("key", 5)]:
            with self.subTest(field=field, value=value):
                bad = copy.deepcopy(INPUT)
                bad["tickets"][1][field] = value   # second ticket: pass 1 would
                                                   # already have written map.md
                                                   # and the first ticket
                self._assert_rejected_without_writing(bad)

    def test_chart_rejects_wrong_typed_map_fields(self):
        for field, value in [("title", None), ("title", 42), ("destination", None),
                             ("destination", ["a"]), ("notes", 3),
                             ("notYetSpecified", 5), ("outOfScope", "mobile"),
                             ("notYetSpecified", [1, 2])]:
            with self.subTest(field=field, value=value):
                bad = copy.deepcopy(INPUT)
                bad["map"][field] = value
                self._assert_rejected_without_writing(bad)

    # ------------------------------------------------------------------
    # Fix round 5 -- review findings N4, N5, N7
    # ------------------------------------------------------------------

    # N4: _fm_value scrubbed BEFORE collapsing line breaks, so a marker split
    # by a newline carried no marker for _scrub to find and was then
    # RECONSTITUTED into a live one by the very next collapse. Unpaired ->
    # MarkerIntegrityError mid-chart with map.md already on disk (R3's
    # partial-folder harm through a new door). Paired -> passed
    # _assert_regions (then named _assert_one_region) and wrote live markers
    # into a generated file, which
    # falsifies the round-4 invariant outright.
    def test_split_marker_is_not_reconstituted_when_frontmatter_is_collapsed(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "n4a-effort"
        inp["tickets"] = [{"key": "t1", "type": "task", "question": "q?",
                           "title": "<!--\ndecision-map:resolution:start --> TAIL-A",
                           "blocks": []}]
        ops.chart(self.root, inp, real=True)          # must not raise
        text = self._ticket_text("n4a-effort", "t1")
        self.assertEqual(text.count(ops._RESOLUTION_START), 0)
        self.assertEqual(text.count(ops._RESOLUTION_END), 0)
        self.assertIn(ops._MARKER_ESCAPED_PREFIX, text)
        self.assertIn("TAIL-A", text)
        # the whole map folder landed, not a half-written one
        self.assertTrue((self.root / "n4a-effort" / "map.md").exists())

    def test_split_marker_pair_is_not_reconstituted_and_never_goes_live(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "n4b-effort"
        inp["tickets"] = [{"key": "t1", "type": "task", "question": "q?",
                           "title": ("<!--\ndecision-map:resolution:start --> MID "
                                     "<!--\ndecision-map:resolution:end --> TAIL-B"),
                           "blocks": []}]
        ops.chart(self.root, inp, real=True)
        text = self._ticket_text("n4b-effort", "t1")
        self.assertEqual(text.count(ops._RESOLUTION_START), 0)
        self.assertEqual(text.count(ops._RESOLUTION_END), 0)
        self.assertIn("MID", text)
        self.assertIn("TAIL-B", text)
        # and the same payload through --gist must not reach map.md live
        ops.resolve(self.root, "n4b-effort", "t1",
                    "<!--\ndecision-map:resolution:end --> GIST-TAIL",
                    link=None, body=None)
        text = self._ticket_text("n4b-effort", "t1")
        self.assertEqual(text.count(ops._RESOLUTION_START), 1)   # resolve's own
        self.assertEqual(text.count(ops._RESOLUTION_END), 1)
        map_md = (self.root / "n4b-effort" / "map.md").read_text(encoding="utf-8")
        self.assertNotIn(ops._RESOLUTION_START, map_md)
        self.assertNotIn(ops._RESOLUTION_END, map_md)
        self.assertEqual(map_md.count(ops._DECISIONS_START), 1)
        self.assertIn("GIST-TAIL", map_md)

    # N5 (Important): _ticket_path is byte-identical across all five commits --
    # the runtime --ticket / --map identifiers were never validated, so
    # claim/comment/resolve/block rewrote arbitrary .md files OUTSIDE --root,
    # lossily round-tripping the victim's own frontmatter on the way through.
    def _traversal_fixture(self):
        """--root is a SUBdirectory, so a traversal payload can be proven to
        land outside --root rather than merely outside the map folder."""
        mroot = self.root / "maps"
        mroot.mkdir()
        victim = self.root / "VICTIM.md"
        victim.write_text(
            "---\ntitle: not a ticket\nstatus: precious\n---\n\nUNRELATED FILE\n",
            encoding="utf-8")
        inp = copy.deepcopy(INPUT)
        ops.chart(mroot, inp, real=True)
        return mroot, victim

    TRAVERSALS = ["../../../VICTIM", "..\\..\\..\\VICTIM", "../VICTIM",
                  "C:/Windows/Temp/dm-r5-victim", "C:\\Windows\\Temp\\dm-r5-victim",
                  "/etc/passwd", "foo/bar", "", "..", None]

    def test_runtime_ticket_id_cannot_escape_the_map_folder(self):
        mroot, victim = self._traversal_fixture()
        before = _snapshot(self.root)
        body = self.root / "b.md"
        body.write_text("x", encoding="utf-8")
        before = _snapshot(self.root)
        for payload in self.TRAVERSALS:
            for op, args in [
                    ("claim", lambda t: ops.claim(mroot, "example-effort", t, "attacker")),
                    ("comment", lambda t: ops.comment(mroot, "example-effort", t, "hi")),
                    ("resolve", lambda t: ops.resolve(mroot, "example-effort", t,
                                                      "g", link=None, body=None)),
                    ("block", lambda t: ops.block(mroot, "example-effort", t, "auth-model")),
            ]:
                with self.subTest(op=op, payload=payload):
                    with self.assertRaises(ops.UnsafeIdentifierError):
                        args(payload)
                    self.assertEqual(_snapshot(self.root), before,
                                     "a rejected traversal must leave the filesystem "
                                     "byte-identical, inside AND outside --root")
        self.assertIn("UNRELATED FILE", victim.read_text(encoding="utf-8"))
        self.assertIn("status: precious", victim.read_text(encoding="utf-8"))

    def test_runtime_map_slug_cannot_escape_the_root(self):
        mroot, _ = self._traversal_fixture()
        before = _snapshot(self.root)
        for payload in self.TRAVERSALS:
            for op, fn in [("read", ops.read_map), ("frontier", ops.frontier)]:
                with self.subTest(op=op, payload=payload):
                    with self.assertRaises(ops.UnsafeIdentifierError):
                        fn(mroot, payload)
                    self.assertEqual(_snapshot(self.root), before)
            with self.subTest(op="claim-slug", payload=payload):
                with self.assertRaises(ops.UnsafeIdentifierError):
                    ops.claim(mroot, payload, "auth-model", "attacker")
                self.assertEqual(_snapshot(self.root), before)

    def test_block_rejects_an_unsafe_blocked_by_identifier(self):
        self._chart()
        before = _snapshot(self.root)
        for payload in ["../../evil", "a, b", "x\ny", None]:
            with self.subTest(payload=payload):
                with self.assertRaises(ops.UnsafeIdentifierError):
                    ops.block(self.root, "example-effort", "auth-model", payload)
                self.assertEqual(_snapshot(self.root), before)

    def test_a_stray_unsafely_named_file_does_not_break_read_or_frontier(self):
        self._chart()
        (self.root / "example-effort" / "tickets" / "not a ticket.md").write_text(
            "hand added\n", encoding="utf-8")
        m = ops.read_map(self.root, "example-effort")          # must not raise
        self.assertEqual(len(m["tickets"]), 3)
        ops.frontier(self.root, "example-effort")              # must not raise

    # N7: _fm_value normalised only CRLF/LF/CR, but _fm_parse splits the
    # frontmatter block with splitlines(), which also breaks on U+000B,
    # U+000C, U+001C, U+001D, U+001E, U+0085, U+2028 and U+2029 -- so
    # `claim --user "me<sep>status: closed"` forged a real frontmatter key and
    # the ticket silently reported itself closed.
    def test_writer_normalises_every_line_break_the_reader_splits_on(self):
        self._chart()
        for sep in _SPLITLINES_SEPARATORS:
            with self.subTest(sep="U+%04X" % ord(sep)):
                ops.claim(self.root, "example-effort", "rollout-order",
                          f"me{sep}status: closed{sep}gist: forged")
                t = next(x for x in ops.read_map(self.root, "example-effort")["tickets"]
                         if x["key"] == "rollout-order")
                self.assertEqual(t["status"], "open", "forged a status key")
                self.assertIsNone(t["gist"], "forged a gist key")
                self.assertIn("status: closed", t["assignee"],
                              "the text must survive as part of the assignee value")

    # ------------------------------------------------------------------
    # Task 3b -- `chart` is additive by default (ADR 0057)
    # ------------------------------------------------------------------

    def _plus_ticket(self, key="fog-graduate", blocks=None):
        inp = copy.deepcopy(INPUT)
        inp["tickets"].append({"key": key, "title": "Graduated from fog",
                               "type": "research", "question": "newly specified?",
                               "blocks": blocks or []})
        return inp

    def test_additive_chart_adds_a_ticket_and_leaves_existing_bytes_identical(self):
        self._chart()
        base = self.root / "example-effort"
        before = _snapshot(base)
        ops.chart(self.root, self._plus_ticket(), real=True)      # no --force
        after = _snapshot(base)
        self.assertIn("tickets/fog-graduate.md", after)
        for path, digest in before.items():
            self.assertEqual(after[path], digest,
                             f"{path} must be byte-identical after an additive chart")

    def test_rechart_identical_input_is_a_byte_identical_no_op(self):
        self._chart()
        base = self.root / "example-effort"
        before = _snapshot(base)
        ops.chart(self.root, INPUT, real=True)
        self.assertEqual(_snapshot(base), before, "re-charting identical input must be a no-op")
        ops.chart(self.root, INPUT, real=True)                    # and again
        self.assertEqual(_snapshot(base), before)

    def test_additive_chart_preserves_a_resolution_and_its_index_entry(self):
        self._chart()
        ops.claim(self.root, "example-effort", "api-limits", "pon")
        ops.resolve(self.root, "example-effort", "auth-model", "per-tenant keys",
                    link="docs/adr/0007-x.md", body="## Rationale\n\nblast radius")
        base = self.root / "example-effort"
        before = _snapshot(base)
        ops.chart(self.root, self._plus_ticket(), real=True)
        auth = self._ticket_text("example-effort", "auth-model")
        self.assertEqual(after_start := auth.count(ops._RESOLUTION_START), 1, after_start)
        self.assertIn("per-tenant keys", auth)
        self.assertIn("blast radius", auth)
        self.assertEqual(before["tickets/auth-model.md"],
                         _snapshot(base)["tickets/auth-model.md"])
        m = ops.read_map(self.root, "example-effort")
        auth_json = next(t for t in m["tickets"] if t["key"] == "auth-model")
        self.assertEqual(auth_json["status"], "closed")
        self.assertEqual(next(t for t in m["tickets"]
                              if t["key"] == "api-limits")["assignee"], "pon")
        map_md = (base / "map.md").read_text(encoding="utf-8")
        self.assertIn("[Auth model?](tickets/auth-model.md) — per-tenant keys", map_md)

    def test_additive_chart_merges_fog_and_scope_without_duplication(self):
        self._chart()
        inp = self._plus_ticket()
        inp["map"] = dict(inp["map"])
        inp["map"]["notYetSpecified"] = ["how to deploy", "NEW-FOG-LINE"]
        inp["map"]["outOfScope"] = ["mobile app", "NEW-SCOPE-LINE"]
        ops.chart(self.root, inp, real=True)
        map_md = (self.root / "example-effort" / "map.md").read_text(encoding="utf-8")
        self.assertEqual(map_md.count("- how to deploy"), 1, "existing line duplicated")
        self.assertEqual(map_md.count("- mobile app"), 1, "existing line duplicated")
        self.assertEqual(map_md.count("- NEW-FOG-LINE"), 1)
        self.assertEqual(map_md.count("- NEW-SCOPE-LINE"), 1)
        # order preserved: the pre-existing line stays first
        self.assertLess(map_md.index("- how to deploy"), map_md.index("- NEW-FOG-LINE"))
        # the merge must not disturb the decisions region
        self.assertEqual(map_md.count(ops._DECISIONS_START), 1)
        # and merging the same input again changes nothing
        before = _snapshot(self.root / "example-effort")
        ops.chart(self.root, inp, real=True)
        self.assertEqual(_snapshot(self.root / "example-effort"), before)
        # ADR 0057: "never remove existing ones". An input that OMITS a line
        # already on disk must not delete it -- the merge is a union, not a
        # replacement. (Without this the suite could not tell a real merge
        # from "wipe the region and write the input", because every other
        # case here happens to re-list the lines already present.)
        inp2 = self._plus_ticket()
        inp2["map"] = dict(inp2["map"])
        inp2["map"]["notYetSpecified"] = ["ONLY-THIS-ONE"]
        inp2["map"]["outOfScope"] = []
        ops.chart(self.root, inp2, real=True)
        map_md = (self.root / "example-effort" / "map.md").read_text(encoding="utf-8")
        for kept in ("- how to deploy", "- NEW-FOG-LINE", "- mobile app",
                     "- NEW-SCOPE-LINE", "- ONLY-THIS-ONE"):
            self.assertIn(kept, map_md, f"{kept} must survive a merge that omits it")

    def test_additive_chart_leaves_divergent_destination_and_notes_alone(self):
        self._chart()
        inp = self._plus_ticket()
        inp["map"] = dict(inp["map"])
        inp["map"]["destination"] = "A COMPLETELY DIFFERENT DESTINATION"
        inp["map"]["notes"] = "DIFFERENT NOTES"
        out = ops.chart(self.root, inp, real=True)
        map_md = (self.root / "example-effort" / "map.md").read_text(encoding="utf-8")
        self.assertIn("a spec", map_md, "on-disk destination must survive")
        self.assertIn("use grill-with-docs", map_md, "on-disk notes must survive")
        self.assertNotIn("A COMPLETELY DIFFERENT DESTINATION", map_md)
        self.assertNotIn("DIFFERENT NOTES", map_md)
        fields = " ".join(out["divergence"])
        self.assertIn("destination", fields)
        self.assertIn("notes", fields)
        # --force is how you actually change them
        ops.chart(self.root, inp, real=True, force=True)
        map_md = (self.root / "example-effort" / "map.md").read_text(encoding="utf-8")
        self.assertIn("A COMPLETELY DIFFERENT DESTINATION", map_md)

    # REWRITTEN for ADR 0058: the edge is now UNIONED into the existing ticket
    # instead of being dropped and reported. The guarantee is restated as
    # "never removes, never reorders, never overwrites" -- so this test pins
    # the scoped identity: exactly one blockedBy entry gained, every other
    # byte of the file unchanged.
    def test_additive_chart_unions_a_new_edge_into_an_existing_ticket(self):
        self._chart()
        base = self.root / "example-effort"
        target = base / "tickets" / "api-limits.md"
        # The target carries RECORDED STATE. Without this the test cannot
        # distinguish a union from "rewrite the ticket fresh, then append the
        # edge" -- a default-valued ticket looks identical either way, and
        # that is exactly the bug an adversarial probe caught here: a ticket
        # whose action became "merge" fell through pass 1's skip check and
        # had its claim silently cleared.
        ops.claim(self.root, "example-effort", "api-limits", "pon")
        ops.comment(self.root, "example-effort", "api-limits", "a human note")
        before_text = target.read_text(encoding="utf-8")
        self.assertIn("assignee: pon", before_text)
        before_tree = _snapshot(base)
        ops.chart(self.root, self._plus_ticket(blocks=["api-limits"]), real=True)
        after_text = target.read_text(encoding="utf-8")
        # the edge is live in the model, and api-limits is not offered as
        # actionable (it reports under `claimed` because it is claimed --
        # frontier() checks assignee before blockers)
        m = ops.read_map(self.root, "example-effort")
        api = next(t for t in m["tickets"] if t["key"] == "api-limits")
        self.assertIn("fog-graduate", api["blockedBy"],
                      "the unioned edge must be visible to readers")
        f = ops.frontier(self.root, "example-effort")
        self.assertNotIn("api-limits", [t["id"] for t in f["frontier"]])
        self.assertIn("api-limits", [t["id"] for t in f["claimed"]])
        # scoped identity: the blocked_by line changed, and so did the graph
        # region -- an edge is drawn at both of its ends (ADR 0064), so this
        # test no longer pins "exactly one line". What it still pins is the
        # thing that matters: recorded state survives a union.
        b_lines, a_lines = before_text.splitlines(), after_text.splitlines()
        differing = [i for i, (x, y) in enumerate(zip(b_lines, a_lines)) if x != y]
        changed = [a_lines[i] for i in differing]
        self.assertTrue(any(ln.startswith("blocked_by:") for ln in changed),
                        f"blocked_by must be among the changed lines, got {changed}")
        self.assertNotIn("blocked_by: []", after_text)
        self.assertIn("blocked_by: [fog-graduate]", after_text)
        for ln in changed:
            self.assertFalse(ln.startswith(("title:", "type:", "mode:", "status:",
                                            "assignee:", "gist:")),
                             f"a union must not touch {ln!r}")
        # the recorded state is explicitly still there
        self.assertIn("assignee: pon", after_text)
        self.assertIn("a human note", after_text)
        # every other ticket is byte-identical EXCEPT the blocker end, whose
        # graph region gains this ticket as a child
        for path, digest in before_tree.items():
            if path in ("tickets/api-limits.md", "tickets/fog-graduate.md"):
                continue
            self.assertEqual(_snapshot(base)[path], digest, f"{path} changed")
        blocker_text = (base / "tickets" / "fog-graduate.md").read_text(encoding="utf-8")
        self.assertIn('ME --> C0["api-limits"]', blocker_text)
        # unioning the same edge again is a no-op, byte for byte
        frozen = _snapshot(base)
        ops.chart(self.root, self._plus_ticket(blocks=["api-limits"]), real=True)
        self.assertEqual(_snapshot(base), frozen,
                         "re-unioning an edge already present must change nothing")

    def test_additive_chart_unions_an_edge_without_relisting_the_target(self):
        """ADR 0058 / F3: an edge may name a ticket that exists on disk but is
        not re-listed in this input's tickets[]."""
        self._chart()
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [{"key": "late-arrival", "title": "Late", "type": "task",
                           "question": "q?",
                           "blocks": ["rollout-order", "api-limits"]}]
        # api-limits is open, unclaimed and unblocked, so it is on the
        # frontier right now -- ADR 0058's stated harm is that it would STAY
        # there while a just-created ticket is meant to block it.
        self.assertIn("api-limits",
                      [t["id"] for t in ops.frontier(self.root, "example-effort")["frontier"]])
        ops.chart(self.root, inp, real=True)
        m = ops.read_map(self.root, "example-effort")
        rollout = next(t for t in m["tickets"] if t["key"] == "rollout-order")
        self.assertIn("late-arrival", rollout["blockedBy"])
        self.assertIn("auth-model", rollout["blockedBy"], "existing edge must survive")
        self.assertEqual(len(m["tickets"]), 4)
        f = ops.frontier(self.root, "example-effort")
        self.assertNotIn("api-limits", [t["id"] for t in f["frontier"]],
                         "a blocked ticket must not still be reported actionable")
        self.assertIn("late-arrival",
                      {b["id"]: b["blockedBy"] for b in f["blocked"]}.get("api-limits", []))

    def test_chart_still_rejects_an_edge_to_a_target_that_exists_nowhere(self):
        self._chart()
        before = _snapshot(self.root)
        bad = self._plus_ticket(blocks=["ghost-ticket"])
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        self.assertEqual(_snapshot(self.root), before)

    # F1: the edge was computed only AFTER the dry-run returned, so the
    # ADR-0039 approval gate was silent about a write it was about to make.
    def test_dry_run_reports_the_edge_it_will_union(self):
        self._chart()
        base = self.root / "example-effort"
        before = _snapshot(base)
        out = ops.chart(self.root, self._plus_ticket(blocks=["api-limits"]), real=False)
        entries = {Path(p["path"]).name: p for p in out["planned"]}
        self.assertEqual(entries["api-limits.md"]["action"], "merge",
                         "a ticket that will gain an edge is not a skip")
        self.assertIn("fog-graduate", entries["api-limits.md"].get("detail") or "",
                      f"the plan must name the edge: {entries['api-limits.md']}")
        self.assertEqual(entries["auth-model.md"]["action"], "skip (exists)")
        self.assertEqual(_snapshot(base), before, "a dry run must never write")
        # the plan told the truth: the real run changes exactly that file
        ops.chart(self.root, self._plus_ticket(blocks=["api-limits"]), real=True)
        after = _snapshot(base)
        self.assertNotEqual(after["tickets/api-limits.md"], before["tickets/api-limits.md"])
        self.assertEqual(after["tickets/auth-model.md"], before["tickets/auth-model.md"])

    def test_dry_run_stderr_renders_the_edge_detail(self):
        self._chart()
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            ops.chart(self.root, self._plus_ticket(blocks=["api-limits"]), real=False)
        text = err.getvalue()
        self.assertIn("merge", text)
        # assert the DETAIL specifically -- "fog-graduate" alone is no proof,
        # the created ticket's own path line already contains that word
        self.assertIn("unions blockedBy", text,
                      f"the human rendering must name the edge too: {text!r}")

    def test_dry_run_json_carries_divergence_and_edge_only_targets(self):
        self._chart()
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [{"key": "late-arrival", "title": "Late", "type": "task",
                           "question": "q?", "blocks": ["api-limits"]}]
        inp["map"] = dict(inp["map"], destination="DIFFERENT")
        out = ops.chart(self.root, inp, real=False)
        # the plan must include a target that is NOT re-listed in tickets[]
        names = {Path(p["path"]).name: p for p in out["planned"]}
        self.assertIn("api-limits.md", names,
                      f"an edge-only target must appear in the plan: {list(names)}")
        self.assertEqual(names["api-limits.md"]["action"], "merge")
        # the dry run must carry divergence too, not only the real run
        self.assertIn("divergence", out)
        self.assertTrue(any("destination" in d for d in out["divergence"]),
                        f"dry-run divergence lost: {out}")

    def test_dry_run_stderr_renders_divergence(self):
        self._chart()
        inp = self._plus_ticket()
        inp["map"] = dict(inp["map"], destination="DIFFERENT")
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            ops.chart(self.root, inp, real=False)
        self.assertIn("divergence", err.getvalue())
        self.assertIn("destination", err.getvalue())

    def test_real_run_stderr_renders_divergence(self):
        self._chart()
        inp = self._plus_ticket()
        inp["map"] = dict(inp["map"], destination="DIFFERENT")
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            ops.chart(self.root, inp, real=True)
        self.assertIn("divergence", err.getvalue())
        self.assertIn("destination", err.getvalue())

    # U5: ADR 0058's own stated consequence -- "every write to an existing
    # ticket must still route through the module's write path so the
    # marker-integrity assertion keeps covering it" -- was unguarded. A mutant
    # writing the union with write_text() instead of _save_ticket() passed all
    # 68 tests. Corruption must make the union REFUSE, not silently write.
    def test_union_write_is_covered_by_the_marker_assertion(self):
        self._chart()
        p = self.root / "example-effort" / "tickets" / "api-limits.md"
        corrupt = p.read_text(encoding="utf-8") + "\n<!-- decision-map:bogus -->\n"
        p.write_text(corrupt, encoding="utf-8")
        # via block() directly ...
        with self.assertRaises(ops.MarkerIntegrityError):
            ops.block(self.root, "example-effort", "api-limits", "auth-model")
        self.assertEqual(p.read_text(encoding="utf-8"), corrupt,
                         "a refused write must leave the file untouched")
        # ... and via the additive chart path that unions the same edge
        with self.assertRaises(ops.MarkerIntegrityError):
            ops.chart(self.root, self._plus_ticket(blocks=["api-limits"]), real=True)
        self.assertEqual(p.read_text(encoding="utf-8"), corrupt)

    # P1: under --force, a ticket that is BOTH listed in tickets[] and newly
    # blocked must still be OVERWRITE, not relabelled merge. Narrowing
    # _chart_plan's ("create", "OVERWRITE") skip to ("create",) made such a
    # ticket a merge, which pass 1 skips -- so --force silently preserved the
    # resolution and claim it promises to discard. That combination was
    # untested.
    def test_force_overwrites_a_listed_ticket_that_also_gains_an_edge(self):
        self._chart()
        ops.claim(self.root, "example-effort", "rollout-order", "pon")
        ops.resolve(self.root, "example-effort", "rollout-order", "decided",
                    link=None, body=None)
        inp = self._plus_ticket(blocks=["rollout-order"])
        # the plan must announce a full rewrite, not a merge
        plan = {Path(p["path"]).name: p["action"]
                for p in ops.chart(self.root, inp, real=False, force=True)["planned"]}
        self.assertEqual(plan["rollout-order.md"], "OVERWRITE",
                         f"--force must announce a full rewrite: {plan}")
        ops.chart(self.root, inp, real=True, force=True)
        m = ops.read_map(self.root, "example-effort")
        ro = next(t for t in m["tickets"] if t["key"] == "rollout-order")
        self.assertEqual(ro["status"], "open", "--force must discard the resolution")
        self.assertIsNone(ro["assignee"], "--force must discard the claim")
        self.assertIsNone(ro["gist"], "--force must discard the gist")
        ticket = self._ticket_text("example-effort", "rollout-order")
        self.assertNotIn("decided", ticket)
        self.assertEqual(ticket.count(ops._RESOLUTION_START), 0)
        # and the edge is still wired afterwards
        self.assertIn("fog-graduate", ro["blockedBy"])
        self.assertIn("auth-model", ro["blockedBy"])

    # Round-3 hazard: _SAFE_SLUG_RE accepts "foo--bar", which would make the
    # cross-backend key marker "<!-- decision-map:key:foo--bar -->" a MALFORMED
    # HTML comment -- "--" is not allowed inside comment text, so sanitizers
    # and rich-text editors rewrite or truncate it, silently breaking the
    # key->item join and re-creating every ticket. Rejected at the one place
    # keys are minted.
    # G4: map.json and frontier.json filter blockedBy DIFFERENTLY and must not
    # be made to agree -- map.json is the durable graph (every recorded
    # blocker, open or closed), frontier.json answers "why can I not pick this
    # up right now" (open blockers only). Two backends are about to implement
    # this from the contract, so pin the distinction.
    def test_map_json_keeps_closed_blockers_that_frontier_drops(self):
        self._chart()
        ops.resolve(self.root, "example-effort", "auth-model", "answered",
                    link=None, body=None)
        m = ops.read_map(self.root, "example-effort")
        rollout = next(t for t in m["tickets"] if t["key"] == "rollout-order")
        self.assertEqual(rollout["blockedBy"], ["auth-model"],
                         "map.json must keep a blocker after it closes")
        f = ops.frontier(self.root, "example-effort")
        self.assertIn("rollout-order", [t["id"] for t in f["frontier"]],
                      "a ticket whose only blocker closed is actionable")
        self.assertNotIn("rollout-order", [b["id"] for b in f["blocked"]])

    # E (ledger F2 / L16): a self-blocking ticket permanently quarantines
    # itself -- frontier() reports it blocked by a ticket that can never
    # close, so it is never takeable and never resolvable. And block()
    # accepted a --blocked-by naming a ticket that exists nowhere, returning
    # rc=0 while writing a phantom key that blocks the ticket forever, even
    # though chart() rejects the identical edge. One validation surface.
    def test_block_rejects_a_self_blocking_edge(self):
        self._chart()
        before = _snapshot(self.root)
        with self.assertRaises(ops.InvalidEdgeError):
            ops.block(self.root, "example-effort", "auth-model", "auth-model")
        self.assertEqual(_snapshot(self.root), before)
        f = ops.frontier(self.root, "example-effort")
        self.assertIn("auth-model", [t["id"] for t in f["frontier"]],
                      "the ticket must stay actionable")

    def test_chart_rejects_a_ticket_that_blocks_itself(self):
        before = _snapshot(self.root)
        bad = copy.deepcopy(INPUT)
        bad["tickets"][0]["blocks"] = ["auth-model"]      # its own key
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        self.assertEqual(_snapshot(self.root), before,
                         "rejection must happen before any write")

    def test_block_rejects_a_blocked_by_that_exists_nowhere(self):
        self._chart()
        before = _snapshot(self.root)
        with self.assertRaises(FileNotFoundError):
            ops.block(self.root, "example-effort", "auth-model", "ghost")
        self.assertEqual(_snapshot(self.root), before,
                         "no phantom key may be written")
        m = ops.read_map(self.root, "example-effort")
        auth = next(t for t in m["tickets"] if t["key"] == "auth-model")
        self.assertEqual(auth["blockedBy"], [])

    # F1 (scrutinize, Major): the map's own title/destination/notes were the
    # only user strings in the module passed to _scrub() WITHOUT being
    # flattened first -- every other one goes through _fm_value/_one_line.
    # Two silent harms: read_map()'s splitlines()[0] truncated a two-line
    # title with no error and no divergence entry, and a newline-bearing
    # destination injected a spurious "## Notes" heading AHEAD of the real
    # one, into the document a human is meant to read and hand-edit. Both
    # skills invite the shape by describing the destination as "one or two
    # lines". This is the map-level analogue of
    # test_newline_in_title_does_not_corrupt_frontmatter.
    def test_newlines_in_map_scalars_do_not_corrupt_map_md(self):
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "map-scalars"
        inp["map"] = {
            "title": "Migrate billing\nSECOND LINE SHOULD NOT APPEAR AS HEADING",
            "destination": "ship it\n\n## Notes\nFAKE INJECTED NOTES SECTION",
            "notes": "first note\nsecond note line",
            "notYetSpecified": [], "outOfScope": [],
        }
        ops.chart(self.root, inp, real=True)
        map_md = (self.root / "map-scalars" / "map.md").read_text(encoding="utf-8")
        # exactly the five headings the renderer writes -- nothing injected
        self.assertEqual([l for l in map_md.splitlines() if l.startswith("## ")],
                         ["## Destination", "## Notes", "## Decisions so far",
                          "## Not yet specified", "## Out of scope"])
        # the H1 is one line and carries the WHOLE title, not a truncation
        self.assertEqual(map_md.splitlines()[0],
                         "# Migrate billing SECOND LINE SHOULD NOT APPEAR AS HEADING")
        m = ops.read_map(self.root, "map-scalars")
        self.assertEqual(m["map"]["name"],
                         "Migrate billing SECOND LINE SHOULD NOT APPEAR AS HEADING")
        # read_map's destination regex still extracts the full flattened value
        self.assertEqual(m["map"]["destination"],
                         "ship it  ## Notes FAKE INJECTED NOTES SECTION")
        self.assertIn("first note second note line", map_md)
        # and map.md still opens with the overview diagram right after the H1
        self.assertIn("```mermaid", map_md.split("## Destination")[0])

    def test_changed_destination_reports_divergence_exactly_once(self):
        """Knock-on guard: _plan_map_md compares _one_line(value) against a
        flattened file, so flattening at render must keep the two consistent
        rather than skew them."""
        inp = copy.deepcopy(INPUT)
        inp["target"]["slug"] = "div-once"
        inp["map"] = dict(inp["map"], destination="line one\nline two")
        ops.chart(self.root, inp, real=True)
        same = ops.chart(self.root, inp, real=True)
        self.assertEqual([d for d in same["divergence"] if "destination" in d], [],
                         "an unchanged multi-line destination must not diverge")
        changed = copy.deepcopy(inp)
        changed["map"] = dict(changed["map"], destination="something else entirely")
        out = ops.chart(self.root, changed, real=True)
        self.assertEqual(len([d for d in out["divergence"] if "destination" in d]), 1,
                         f"expected exactly one destination divergence: {out['divergence']}")

    # F2 (scrutinize, Minor): the skip itself is right -- raising would let one
    # stray file break read/frontier for the whole map -- but it was totally
    # silent, so a hand-added `tickets/my ticket.md` was invisible and a
    # session would believe a decision was unrepresented while it sat on disk.
    def test_a_skipped_ticket_file_is_announced_on_stderr(self):
        self._chart()
        (self.root / "example-effort" / "tickets" / "my ticket.md").write_text(
            "hand added\n", encoding="utf-8")
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            m = ops.read_map(self.root, "example-effort")
        self.assertEqual(len(m["tickets"]), 3, "the JSON shape must not change")
        self.assertIn("warning:", err.getvalue())
        self.assertIn("my ticket.md", err.getvalue())
        err2 = io.StringIO()
        with contextlib.redirect_stderr(err2):
            ops.frontier(self.root, "example-effort")
        self.assertIn("my ticket.md", err2.getvalue(),
                      "frontier must warn too -- it is the view a session trusts")

    def test_chart_rejects_a_key_containing_a_double_hyphen(self):
        before = _snapshot(self.root)
        for key in ("foo--bar", "a--", "--lead", "x---y"):
            with self.subTest(key=key):
                bad = copy.deepcopy(INPUT)
                bad["tickets"][0]["key"] = key
                bad["tickets"][0]["blocks"] = []
                with self.assertRaises(ops.ChartValidationError) as cm:
                    ops.chart(self.root, bad, real=True)
                self.assertIn("--", str(cm.exception))
                self.assertEqual(_snapshot(self.root), before,
                                 "rejection must happen before any write")
        # a single hyphen is still fine -- the fixture keys all use one
        ops.chart(self.root, INPUT, real=True)
        self.assertTrue((self.root / "example-effort" / "tickets"
                         / "auth-model.md").exists())

    def test_plan_detail_is_deduped_and_absent_on_non_merge_entries(self):
        self._chart()
        inp = self._plus_ticket(blocks=["api-limits", "api-limits"])
        out = ops.chart(self.root, inp, real=False)
        entries = {Path(p["path"]).name: p for p in out["planned"]}
        self.assertEqual(entries["api-limits.md"]["detail"],
                         "unions blockedBy: fog-graduate",
                         "a repeated blocker must appear once")
        for name, e in entries.items():
            if e["action"] != "merge":
                self.assertIsNone(e["detail"],
                                  f"{name}: detail is only meaningful on merge")

    # Task 4: block() now writes BOTH ends of a blocking edge (ADR 0064), so
    # chart's dry-run plan must announce the blocker's file too, not only the
    # blocked ticket's -- nothing the real run writes may be missing from the
    # plan (ADR 0039).
    def test_the_plan_announces_the_blocker_end_of_a_new_edge(self):
        self._chart()
        plan = ops.chart(self.root, self._plus_ticket(blocks=["api-limits"]), real=False)
        by_path = {p["path"]: p for p in plan["planned"]}
        blocked = next(v for k, v in by_path.items() if k.endswith("api-limits.md"))
        self.assertEqual(blocked["action"], "merge")
        self.assertIn("unions blockedBy", blocked["detail"])
        # the plan must name EVERY file the real run writes -- that is the
        # whole value of the ADR-0039 gate
        self.assertTrue(any(k.endswith("fog-graduate.md") for k in by_path),
                        f"the blocker end is missing from the plan: {list(by_path)}")

    def test_no_merge_entry_may_carry_a_null_detail(self):
        self._chart()
        plan = ops.chart(self.root, self._plus_ticket(blocks=["api-limits"]), real=False)
        for e in plan["planned"]:
            if e["action"] == "merge":
                self.assertIsNotNone(e["detail"], f"undescribed write: {e['path']}")

    # Review finding (Critical): the blocker_gains loop read the BLOCKED
    # ticket's frontmatter unconditionally, with no existence check -- but a
    # `blocks` target may be a ticket created in this SAME input (additive
    # chart explicitly supports mixing existing and new tickets). A
    # pre-existing blocker naming a brand-new ticket as something it blocks
    # crashed the dry run with an uncaught FileNotFoundError before the
    # ADR-0039 gate ever rendered anything.
    def test_a_pre_existing_blocker_naming_a_brand_new_ticket_does_not_crash(self):
        self._chart()
        inp = copy.deepcopy(INPUT)
        # auth-model (pre-existing, "skip (exists)") now also blocks a ticket
        # that does not exist yet anywhere -- neither on disk nor charted
        # before this call -- only newly appended to this same tickets[].
        inp["tickets"][0] = dict(inp["tickets"][0],
                                 blocks=["rollout-order", "fresh-ticket"])
        inp["tickets"].append({"key": "fresh-ticket", "title": "Fresh",
                               "type": "task", "question": "q?", "blocks": []})
        plan = ops.chart(self.root, inp, real=False)   # must not raise
        by_path = {Path(p["path"]).name: p for p in plan["planned"]}
        self.assertEqual(by_path["auth-model.md"]["action"], "merge")
        self.assertIn("fresh-ticket", by_path["auth-model.md"]["detail"],
                      f"the new child must be named: {by_path['auth-model.md']}")
        # and the real run must actually touch exactly what was announced
        ops.chart(self.root, inp, real=True)
        auth = self._ticket_text("example-effort", "auth-model")
        self.assertIn('ME --> C0["fresh-ticket"]', auth)

    # Review finding (Important): the two tests above pass byte-for-byte
    # identically with the entire blocker_gains block deleted -- the first is
    # satisfied by the pre-existing `pending` loop (the BLOCKED end), the
    # second by the unconditional per-ticket loop that seeds `by_path`. This
    # is the scenario that actually requires blocker_gains: an EXISTING,
    # otherwise-untouched ticket (action "skip (exists)") gains a brand-new
    # `blocks` edge onto another EXISTING ticket in a re-chart. Neither ticket
    # is created or overwritten by this call, so nothing else in _chart_plan
    # would ever flip the blocker's entry to "merge".
    def test_an_existing_ticket_gaining_a_new_blocker_role_is_announced_in_the_plan(self):
        self._chart()
        inp = copy.deepcopy(INPUT)
        for t in inp["tickets"]:
            if t["key"] == "api-limits":
                t["blocks"] = ["rollout-order"]
        plan = ops.chart(self.root, inp, real=False)
        by_path = {Path(p["path"]).name: p for p in plan["planned"]}
        self.assertEqual(by_path["auth-model.md"]["action"], "skip (exists)")
        self.assertEqual(by_path["api-limits.md"]["action"], "merge",
                         "the blocker's own file must be announced, not skipped")
        self.assertIn("renders as a child in the graph: rollout-order",
                      by_path["api-limits.md"]["detail"])
        # the plan told the truth: the real run changes exactly those two
        # files (map.md and auth-model.md, both "skip (exists)", must not)
        before = _snapshot(self.root / "example-effort")
        ops.chart(self.root, inp, real=True)
        after = _snapshot(self.root / "example-effort")
        changed = {k for k in after if before.get(k) != after.get(k)}
        self.assertEqual(changed, {"tickets/api-limits.md", "tickets/rollout-order.md"},
                         f"the plan must name exactly what the real run touches: {changed}")

    # C2: a map-body merge came back with detail: null, so the plan rendered a
    # bare "merge .../map.md". That contradicts the contract's own promise --
    # "a merge entry carries a detail string naming what it will add" -- and
    # its worked example, "adds 1 fog line". It matters most at the ADR-0039
    # gate: work-map asks the user to approve exactly that line, and a blank
    # one asks them to approve a write nobody described.
    def test_map_body_merge_names_the_lines_it_adds(self):
        self._chart()
        base = self.root / "example-effort"
        before = _snapshot(base)

        def plan(fog, scope):
            inp = copy.deepcopy(INPUT)
            inp["map"] = dict(inp["map"], notYetSpecified=fog, outOfScope=scope)
            out = ops.chart(self.root, inp, real=False)
            entry = {Path(p["path"]).name: p for p in out["planned"]}["map.md"]
            return inp, entry

        inp, both = plan(["how to deploy", "FOG-A", "FOG-B"],
                         ["mobile app", "SCOPE-A"])
        self.assertEqual(both["action"], "merge")
        self.assertEqual(both["detail"], "adds 2 fog lines, 1 out-of-scope line")
        self.assertEqual(plan(["how to deploy", "FOG-A"], ["mobile app"])[1]["detail"],
                         "adds 1 fog line")
        self.assertEqual(plan(["how to deploy"],
                              ["mobile app", "SCOPE-A", "SCOPE-B"])[1]["detail"],
                         "adds 2 out-of-scope lines")
        # a map body that gains nothing is still a skip, and a skip still
        # carries no detail
        unchanged = plan(["how to deploy"], ["mobile app"])[1]
        self.assertEqual(unchanged["action"], "skip (exists)")
        self.assertIsNone(unchanged["detail"])
        # the human rendering carries the same sentence the JSON does
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            ops.chart(self.root, inp, real=False)
        self.assertIn("adds 2 fog lines, 1 out-of-scope line", err.getvalue())
        self.assertEqual(_snapshot(base), before, "a dry run must never write")
        # and the plan told the truth: the real run adds exactly those lines
        ops.chart(self.root, inp, real=True)
        map_md = (base / "map.md").read_text(encoding="utf-8")
        for added in ("- FOG-A", "- FOG-B", "- SCOPE-A"):
            self.assertEqual(map_md.count(added), 1, f"{added} not added once")

    # F3: the "adds nothing" branch of the map-body detail was unguarded --
    # mutating it to "adds 0 lines", the exact lie its own docstring warns
    # against, left all 74 tests green. It is reachable from the hand edit
    # work-map INSTRUCTS: delete the graduated fog line, and the next chart
    # restores the tool-owned "- (none)" placeholder, changing bytes while
    # adding nothing.
    def test_map_body_merge_that_adds_nothing_does_not_claim_it_added_lines(self):
        self._chart()
        base = self.root / "example-effort"
        p = base / "map.md"
        text = p.read_text(encoding="utf-8")
        self.assertIn("- how to deploy",
                      ops._region_body(text, ops._FOG_START, ops._FOG_END))
        # the hand edit: empty the fog region, markers left alone
        p.write_text(ops._replace_region(text, ops._FOG_START, ops._FOG_END, "\n"),
                     encoding="utf-8")
        inp = copy.deepcopy(INPUT)
        inp["map"] = dict(inp["map"], notYetSpecified=[])   # nothing new to add
        entry = {Path(e["path"]).name: e
                 for e in ops.chart(self.root, inp, real=False)["planned"]}["map.md"]
        self.assertEqual(entry["action"], "merge",
                         "restoring the placeholder is a write, so not a skip")
        self.assertEqual(entry["detail"],
                         "normalises the map body's list regions (no new lines)")
        self.assertNotIn("adds", entry["detail"],
                         "a merge that adds nothing must not say it adds anything")
        # and the real run does exactly that: the placeholder, no phantom lines
        ops.chart(self.root, inp, real=True)
        fog = ops._region_body(p.read_text(encoding="utf-8"),
                               ops._FOG_START, ops._FOG_END)
        self.assertEqual(fog.strip(), ops._EMPTY_LIST_LINE)

    def test_block_does_not_rewrite_when_the_edge_is_already_present(self):
        """The byte-identical no-op depends on block() not writing at all --
        a rewrite happens to produce the same bytes for files this module
        wrote, so only a file that does NOT round-trip exactly can prove it."""
        self._chart()
        p = self.root / "example-effort" / "tickets" / "rollout-order.md"
        odd = p.read_text(encoding="utf-8").replace(
            "blocked_by: [auth-model]", "blocked_by:   [auth-model]")
        self.assertIn("blocked_by:   [auth-model]", odd, "sanity: fixture rewritten")
        p.write_text(odd, encoding="utf-8")
        ops.block(self.root, "example-effort", "rollout-order", "auth-model")
        self.assertEqual(p.read_text(encoding="utf-8"), odd,
                         "block() must not write when the edge is already present")
        ops.chart(self.root, INPUT, real=True)      # additive re-chart, same edge
        self.assertEqual(p.read_text(encoding="utf-8"), odd,
                         "an additive re-chart must not rewrite it either")

    # F2: every message that steers the user to --force must say what --force
    # destroys. The reviewer followed this advice and lost a resolution.
    def test_force_advice_always_warns_what_force_destroys(self):
        """Every message that steers the user to re-charting must state the
        cost -- checked across BOTH divergence sources, not just the scalar
        one, since each builds its own sentence."""
        def assert_warned(messages, label):
            self.assertTrue(messages, f"sanity: expected {label} divergence")
            warned = 0
            for message in messages:
                if "--force" in message or "re-chart" in message.lower():
                    lowered = message.lower()
                    self.assertTrue(
                        any(w in lowered for w in ("discard", "destroy")),
                        f"{label}: advice must state the cost: {message!r}")
                    # name ALL THREE kinds of recorded state, not just one --
                    # pinning only "resolution" let a mutant drop the rest
                    for lost in ("resolution", "claim", "blocking edge"):
                        self.assertIn(lost, lowered,
                                      f"{label}: must name {lost!r}: {message!r}")
                    warned += 1
            self.assertTrue(warned, f"{label}: no message mentioned re-charting")

        # source 1: a divergent scalar
        self._chart()
        inp = self._plus_ticket()
        inp["map"] = dict(inp["map"], destination="DIFFERENT", notes="DIFFERENT")
        assert_warned(ops.chart(self.root, inp, real=True)["divergence"], "scalar")

        # source 2: a map.md predating the list regions
        inp2 = copy.deepcopy(INPUT)
        inp2["target"] = dict(inp2["target"], slug="legacy-effort")
        ops.chart(self.root, inp2, real=True)
        mp = self.root / "legacy-effort" / "map.md"
        legacy = mp.read_text(encoding="utf-8")
        for marker in (ops._FOG_START, ops._FOG_END, ops._SCOPE_START, ops._SCOPE_END):
            legacy = legacy.replace(marker, "")
        mp.write_text(legacy, encoding="utf-8")
        inp2["map"] = dict(inp2["map"], notYetSpecified=["how to deploy", "NEW"])
        assert_warned(ops.chart(self.root, inp2, real=True)["divergence"], "legacy region")

    # N5 (parked Minor): the legacy map.md path -- an existing map with no fog
    # or scope regions -- was never exercised.
    def test_legacy_map_without_list_regions_is_reported_not_mangled(self):
        self._chart()
        mp = self.root / "example-effort" / "map.md"
        legacy = mp.read_text(encoding="utf-8")
        legacy = legacy.replace(ops._FOG_START, "").replace(ops._FOG_END, "")
        legacy = legacy.replace(ops._SCOPE_START, "").replace(ops._SCOPE_END, "")
        mp.write_text(legacy, encoding="utf-8")
        inp = self._plus_ticket()
        inp["map"] = dict(inp["map"], notYetSpecified=["how to deploy", "BRAND-NEW-FOG"])
        out = ops.chart(self.root, inp, real=True)
        after = mp.read_text(encoding="utf-8")
        self.assertIn("- how to deploy", after, "legacy content must survive untouched")
        self.assertNotIn("BRAND-NEW-FOG", after, "must not guess where to insert")
        self.assertTrue(any("notYetSpecified" in d for d in out["divergence"]),
                        f"the un-merged lines must be reported: {out['divergence']}")
        self.assertTrue((self.root / "example-effort" / "tickets"
                         / "fog-graduate.md").exists(), "the ticket still lands")

    def test_additive_chart_dry_run_labels_create_and_skip_and_writes_nothing(self):
        self._chart()
        base = self.root / "example-effort"
        before = _snapshot(base)
        out = ops.chart(self.root, self._plus_ticket(), real=False)
        actions = {Path(p["path"]).name: p["action"] for p in out["planned"]}
        self.assertEqual(actions["fog-graduate.md"], "create")
        self.assertEqual(actions["auth-model.md"], "skip (exists)")
        self.assertEqual(actions["rollout-order.md"], "skip (exists)")
        self.assertNotIn("refuse", set(actions.values()))
        self.assertEqual(_snapshot(base), before, "a dry run must never write")
        # a `skip` must never write: the real run agrees with the plan
        ops.chart(self.root, self._plus_ticket(), real=True)
        after = _snapshot(base)
        for name, action in actions.items():
            if action == "skip (exists)":
                key = f"tickets/{name}" if name != "map.md" else "map.md"
                self.assertEqual(after[key], before[key], f"{name} was labelled skip but changed")

    def test_markers_in_fog_and_scope_survive_the_additive_path_escaped(self):
        self._chart()
        inp = self._plus_ticket()
        inp["map"] = dict(inp["map"])
        inp["map"]["notYetSpecified"] = [f"how to deploy",
                                         f"FOG {ops._RESOLUTION_START} TAIL",
                                         f"SPLIT <!--\ndecision-map:decisions:start --> TAIL2"]
        inp["map"]["outOfScope"] = [f"SCOPE {ops._DECISIONS_END} TAIL3"]
        ops.chart(self.root, inp, real=True)
        map_md = (self.root / "example-effort" / "map.md").read_text(encoding="utf-8")
        self.assertEqual(map_md.count(ops._RESOLUTION_START), 0)
        self.assertEqual(map_md.count(ops._DECISIONS_START), 1)   # the real region only
        self.assertEqual(map_md.count(ops._DECISIONS_END), 1)
        for canary in ("TAIL", "TAIL2", "TAIL3"):
            self.assertIn(canary, map_md)
        ops.resolve(self.root, "example-effort", "auth-model", "g", link=None, body=None)
        map_md = (self.root / "example-effort" / "map.md").read_text(encoding="utf-8")
        self.assertEqual(map_md.count(ops._DECISIONS_START), 1)
        self.assertIn("TAIL2", map_md)

    def test_additive_chart_refuses_traversal_the_same_way(self):
        self._chart()
        before = _snapshot(self.root)
        bad = self._plus_ticket()
        bad["target"] = dict(bad["target"], slug="../../../pwned")
        with self.assertRaises(ops.ChartValidationError):
            ops.chart(self.root, bad, real=True)
        self.assertEqual(_snapshot(self.root), before)

    # F6 (parked Minor, closed here because this change touches the assertion):
    # _assert_regions' "every decision-map marker belongs to a declared region"
    # check was load-bearing but unguarded -- a mutant dropping it survived the
    # whole suite. This test passes against 3f0f61e by design; it is validated
    # against a mutant instead (see the report).
    def test_write_refuses_when_a_stray_marker_prefix_is_present(self):
        self._chart()
        p = self.root / "example-effort" / "tickets" / "auth-model.md"
        p.write_text(p.read_text(encoding="utf-8") + "\n<!-- decision-map:bogus -->\n",
                     encoding="utf-8")
        with self.assertRaises(ops.MarkerIntegrityError):
            ops.claim(self.root, "example-effort", "auth-model", "pon")

    def test_chart_rejects_wrong_typed_containers(self):
        cases = []
        bad = copy.deepcopy(INPUT); bad["tickets"] = {"a": 1}; cases.append(bad)
        bad = copy.deepcopy(INPUT); bad["tickets"][0] = "not a dict"; cases.append(bad)
        bad = copy.deepcopy(INPUT); bad["map"] = "not a dict"; cases.append(bad)
        bad = copy.deepcopy(INPUT); del bad["target"]; cases.append(bad)
        bad = copy.deepcopy(INPUT); bad["target"]["slug"] = 7; cases.append(bad)
        bad = copy.deepcopy(INPUT); bad["tickets"][0]["blocks"] = "rollout-order"
        cases.append(bad)
        bad = copy.deepcopy(INPUT); bad["tickets"][0]["blocks"] = [1]; cases.append(bad)
        del bad
        for i, case in enumerate(cases):
            with self.subTest(case=i):
                self._assert_rejected_without_writing(case)


class LocalMapOpsCliTest(unittest.TestCase):
    """Finding 6: main()/argparse had zero coverage. Drive the real CLI
    entry point (sys.argv + main()) rather than calling the functions
    directly, to prove the argument wiring itself works."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.input_path = self.root / "map_input.json"
        self.input_path.write_text(json.dumps(INPUT), encoding="utf-8")

    def tearDown(self):
        self.tmp.cleanup()

    def _run_full(self, argv):
        """Drive main() and return (exit_code, stdout, stderr).

        Round 5: main() no longer raises for a known failure -- it prints one
        line to stderr and returns a distinct exit code, and stdout carries
        JSON or nothing. Streams must therefore be captured separately.
        """
        old_argv = sys.argv
        sys.argv = ["local_map_ops.py"] + argv
        out, err = io.StringIO(), io.StringIO()
        try:
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                rc = ops.main()
        finally:
            sys.argv = old_argv
        return rc, out.getvalue(), err.getvalue()

    def _run(self, argv):
        rc, out, err = self._run_full(argv)
        self.assertEqual(rc, 0, f"expected success, got rc={rc}: {err}")
        return out

    def test_cli_chart_dry_run_then_real_then_frontier(self):
        rc, out, err = self._run_full(
            ["chart", "--root", str(self.root), "--input", str(self.input_path)])
        self.assertEqual(rc, 0)
        # the human-readable plan goes to stderr; stdout is pure JSON
        self.assertIn("DRY RUN", err)
        self.assertEqual(json.loads(out)["dryRun"], True)
        self.assertFalse((self.root / "example-effort").exists())

        out = self._run(["chart", "--root", str(self.root), "--input", str(self.input_path), "--real"])
        data = json.loads(out)
        self.assertEqual(data["backend"], "local")
        self.assertTrue((self.root / "example-effort" / "map.md").exists())

        out = self._run(["frontier", "--root", str(self.root), "--map", "example-effort"])
        data = json.loads(out)
        self.assertIn("auth-model", [t["id"] for t in data["frontier"]])

    # F3 (parked Minor, closed here because Task 3b is about dry-run
    # truthfulness): --dry-run short-circuited BEFORE the identifier guard, so
    # `claim --dry-run --ticket ../../../VICTIM` reported rc=0 "wouldRun" for a
    # call the real run rejects with rc=2. Inert, but an untruthful dry run.
    def test_cli_block_rejects_a_phantom_target_with_a_clean_error(self):
        self._run(["chart", "--root", str(self.root), "--input", str(self.input_path),
                   "--real"])
        for bad in ("ghost", "auth-model"):     # nonexistent, then self-block
            with self.subTest(blocked_by=bad):
                rc, out, err = self._run_full(
                    ["block", "--root", str(self.root), "--map", "example-effort",
                     "--ticket", "auth-model", "--blocked-by", bad])
                self.assertEqual(rc, ops.EXIT_ERROR, "must not report success")
                self.assertEqual(out, "", "stdout must carry JSON or nothing")
                self.assertTrue(err.startswith("error: block: "), err)

    def test_cli_dry_run_validates_identifiers_before_reporting_success(self):
        self._run(["chart", "--root", str(self.root), "--input", str(self.input_path),
                   "--real"])
        for argv in (["claim", "--ticket", "../../../VICTIM", "--user", "x"],
                     ["resolve", "--ticket", "../../../VICTIM", "--gist", "g"],
                     ["block", "--ticket", "auth-model", "--blocked-by", "../../x"],
                     ["claim", "--ticket", "auth-model", "--user", "x"]):
                     # last one is valid and must still succeed
            full = argv[:1] + ["--root", str(self.root), "--map", "example-effort",
                               "--dry-run"] + argv[1:]
            rc, out, err = self._run_full(full)
            unsafe = any(".." in a for a in argv)
            with self.subTest(argv=argv):
                if unsafe:
                    self.assertEqual(rc, ops.EXIT_ERROR,
                                     "a dry run must reject what the real run rejects")
                    self.assertEqual(out, "")
                else:
                    self.assertEqual(rc, 0)
                    self.assertEqual(json.loads(out)["dryRun"], True)

    # REWRITTEN for Task 3b / ADR 0057: the CLI-level re-chart assertion flips
    # from "refuses with EXIT_ERROR" to "succeeds additively". The clean-error
    # contract it also guarded (round 5) is still covered, by
    # test_cli_known_failures_are_clean_one_line_errors.
    def test_cli_claim_resolve_and_additive_rechart(self):
        self._run(["chart", "--root", str(self.root), "--input", str(self.input_path), "--real"])

        out = self._run(["claim", "--root", str(self.root), "--map", "example-effort",
                          "--ticket", "auth-model", "--user", "pon"])
        self.assertEqual(json.loads(out), {"claimed": "auth-model", "assignee": "pon"})

        out = self._run(["resolve", "--root", str(self.root), "--map", "example-effort",
                          "--ticket", "auth-model", "--gist", "per-tenant keys"])
        self.assertEqual(json.loads(out)["resolved"], "auth-model")

        out = self._run(["comment", "--root", str(self.root), "--map", "example-effort",
                          "--ticket", "rollout-order",
                          "--body-file", str(self._write_body("noted"))])
        self.assertEqual(json.loads(out), {"commented": "rollout-order"})

        # CLI-level re-chart is now ADDITIVE (ADR 0057): it succeeds, writes
        # nothing that already exists, and preserves the resolution above.
        base = self.root / "example-effort"
        before = _snapshot(base)
        rc, out, err = self._run_full(
            ["chart", "--root", str(self.root), "--input", str(self.input_path), "--real"])
        self.assertEqual(rc, 0)
        self.assertEqual(json.loads(out)["backend"], "local")
        self.assertEqual(_snapshot(base), before,
                         "an additive re-chart of identical input must write nothing")
        # explicit --force opt-in still works from the CLI
        out = self._run(["chart", "--root", str(self.root), "--input", str(self.input_path),
                          "--real", "--force"])
        data = json.loads(out)
        self.assertEqual(data["backend"], "local")

    # Deferred across rounds 1-4, landed in round 5: known failures must not
    # surface as raw tracebacks with empty stdout.
    def test_cli_known_failures_are_clean_one_line_errors(self):
        self._run(["chart", "--root", str(self.root), "--input", str(self.input_path),
                   "--real"])
        cases = {
            "unsafe ticket id": ["claim", "--root", str(self.root), "--map",
                                 "example-effort", "--ticket", "../../../VICTIM"],
            "unsafe map slug": ["frontier", "--root", str(self.root), "--map", "../.."],
            "missing ticket file": ["claim", "--root", str(self.root), "--map",
                                    "example-effort", "--ticket", "nope"],
            "missing --input": ["chart", "--root", str(self.root), "--real"],
            "missing --map": ["claim", "--root", str(self.root), "--ticket", "auth-model"],
            "missing --gist": ["resolve", "--root", str(self.root), "--map",
                               "example-effort", "--ticket", "auth-model"],
            "unreadable --input": ["chart", "--root", str(self.root), "--input",
                                   str(self.root / "nope.json"), "--real"],
        }
        for label, argv in cases.items():
            with self.subTest(case=label):
                rc, out, err = self._run_full(argv)
                self.assertEqual(rc, ops.EXIT_ERROR,
                                 "known failure needs its own exit code")
                self.assertEqual(out, "", "stdout must carry JSON or nothing")
                self.assertEqual(len(err.strip().splitlines()), 1,
                                 f"expected exactly one line of diagnostics, got: {err!r}")
                self.assertTrue(err.startswith("error: "), err)

    def test_cli_process_exit_code_is_wired(self):
        """The in-process tests above check main()'s return value; this one
        proves __main__ actually turns it into a process exit code."""
        script = Path(ops.__file__)
        env = dict(os.environ, PYTHONIOENCODING="utf-8")
        ok = subprocess.run(
            [sys.executable, str(script), "chart", "--root", str(self.root),
             "--input", str(self.input_path)], capture_output=True, text=True, env=env)
        self.assertEqual(ok.returncode, 0)
        json.loads(ok.stdout)                     # stdout is pure JSON
        bad = subprocess.run(
            [sys.executable, str(script), "claim", "--root", str(self.root),
             "--map", "example-effort", "--ticket", "../../../VICTIM"],
            capture_output=True, text=True, env=env)
        self.assertEqual(bad.returncode, ops.EXIT_ERROR)
        self.assertEqual(bad.stdout, "")
        self.assertNotIn("Traceback", bad.stderr)
        self.assertTrue(bad.stderr.startswith("error: "), bad.stderr)

    def _write_body(self, text):
        p = self.root / "body.md"
        p.write_text(text, encoding="utf-8")
        return p


class GraphRegionOnTicketTests(unittest.TestCase):
    # Own setUp/tearDown/_chart rather than subclassing LocalMapOpsTest: that
    # class's test_* methods would otherwise run a second time under this
    # class's name too (unittest discovery is per-subclass), inflating the
    # suite far past this class's own 4 tests.
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def _chart(self):
        return ops.chart(self.root, INPUT, real=True)

    def test_a_created_ticket_carries_a_graph_region_above_the_question(self):
        self._chart()
        text = (self.root / "example-effort" / "tickets" / "api-limits.md").read_text(
            encoding="utf-8")
        self.assertIn(map_core.GRAPH_START, text)
        self.assertIn(map_core.GRAPH_END, text)
        self.assertLess(text.index(map_core.GRAPH_START), text.index("## Question"),
                        "the position must be visible before the prose")
        self.assertIn('ME["api-limits (this ticket)"]', text)

    def test_set_graph_region_inserts_above_question_on_a_legacy_ticket(self):
        legacy = "\n## Question\n\nold body\n"
        out = ops._set_graph_region(legacy, "<!-- decision-map:graph:start -->\nX\n"
                                            "<!-- decision-map:graph:end -->\n")
        self.assertLess(out.index("decision-map:graph:start"), out.index("## Question"))
        self.assertIn("old body", out, "legacy content must survive untouched")

    def test_set_graph_region_replaces_an_existing_region_and_touches_nothing_else(self):
        body = ("<!-- decision-map:graph:start -->\nOLD\n<!-- decision-map:graph:end -->\n"
                "\n## Question\n\nkeep me\n")
        out = ops._set_graph_region(body, "<!-- decision-map:graph:start -->\nNEW\n"
                                          "<!-- decision-map:graph:end -->\n")
        self.assertIn("NEW", out)
        self.assertNotIn("OLD", out)
        self.assertIn("keep me", out)

    def _ticket(self, key):
        return (self.root / "example-effort" / "tickets" / f"{key}.md").read_text(
            encoding="utf-8")

    def _force(self, keys, real=True):
        """chart --force naming ONLY `keys`. -> (result json, stderr plan)."""
        inp = copy.deepcopy(INPUT)
        inp["tickets"] = [t for t in inp["tickets"] if t["key"] in keys]
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            out = ops.chart(self.root, inp, real=real, force=True)
        return out, err.getvalue()

    def test_force_on_a_blocker_keeps_the_child_it_still_unblocks(self):
        """--force resets the OVERWRITE'd ticket's body to an EMPTY diagram. Its
        PARENTS are discarded for real (documented), but its CHILDREN are edges
        held in the other tickets' frontmatter and survive untouched -- so the
        edge stayed live in the model while its picture was destroyed.

        Permanent, not transient: a later additive chart skips the ticket as
        "already exists, no change" and `block` correctly refuses to rewrite an
        edge that already exists, so nothing ever repairs it."""
        self._chart()
        self.assertIn('ME --> C0["rollout-order"]', self._ticket("auth-model"))
        self._force(["auth-model"])
        self.assertIn("blocked_by: [auth-model]", self._ticket("rollout-order"),
                      "the edge survives --force: it is recorded on the other end")
        self.assertIn('ME --> C0["rollout-order"]', self._ticket("auth-model"),
                      "so the picture of that edge must survive with it")

    def test_force_clears_a_dead_edge_from_a_blocker_the_input_never_names(self):
        """The mirror harm. --force DOES discard rollout-order's own blockers,
        which is documented and correct -- but auth-model, which this input never
        mentions, was left drawing `ME --> C0["rollout-order"]` for an edge that
        no longer exists anywhere. A picture of a dead edge is exactly the
        staleness-read-as-a-fact harm ADR 0064 exists to prevent, and the user
        approved an OVERWRITE line that never mentioned auth-model."""
        self._chart()
        out, plan = self._force(["rollout-order"], real=False)
        entry = next(e for e in out["planned"] if e["path"].endswith("auth-model.md"))
        self.assertEqual(entry["action"], "merge",
                         "the ADR-0039 gate must show the neighbour write")
        self.assertEqual(entry["detail"],
                         "no longer renders as a child in the graph: rollout-order",
                         "the contract forbids an undescribed merge")
        self.assertIn("no longer renders as a child in the graph", plan)

        self._force(["rollout-order"])
        self.assertIn("blocked_by: []", self._ticket("rollout-order"))
        self.assertNotIn('C0["rollout-order"]', self._ticket("auth-model"),
                         "the blocker must stop drawing the edge that was removed")
        # and the correction is not re-announced: a second identical --force
        # finds nothing left to lose, so the plan goes quiet again.
        _out2, plan2 = self._force(["rollout-order"], real=False)
        self.assertNotIn("no longer renders as a child", plan2)

    def test_children_of_finds_every_ticket_naming_this_one_as_a_blocker(self):
        self._chart()
        ops.block(self.root, "example-effort", "api-limits", "auth-model")
        # INPUT already wires auth-model -> rollout-order via "blocks" at chart
        # time, so a correct scan finds BOTH that pre-existing edge and the
        # one block() just added -- sorted, since _children_of promises order.
        self.assertEqual(ops._children_of(self.root, "example-effort", "auth-model"),
                         ["api-limits", "rollout-order"])


class PositionDiagramTests(unittest.TestCase):
    def test_renders_parents_self_and_children_sorted(self):
        r = map_core.position_diagram_region(
            "carve-core-api", ["zeta-blocker", "alpha-blocker"], ["downstream-one"])
        self.assertEqual(r, (
            "<!-- decision-map:graph:start -->\n"
            "```mermaid\n"
            "graph TD\n"
            '    ME["carve-core-api (this ticket)"]\n'
            '    P0["alpha-blocker"] --> ME\n'
            '    P1["zeta-blocker"] --> ME\n'
            '    ME --> C0["downstream-one"]\n'
            "```\n"
            "<!-- decision-map:graph:end -->\n"))

    def test_a_ticket_with_no_edges_renders_a_single_node(self):
        r = map_core.position_diagram_region("lonely", [], [])
        self.assertIn('    ME["lonely (this ticket)"]\n```', r)
        self.assertNotIn("P0[", r)
        self.assertNotIn("C0[", r)

    def test_render_is_deterministic_regardless_of_input_order(self):
        a = map_core.position_diagram_region("k", ["b", "a"], ["d", "c"])
        b = map_core.position_diagram_region("k", ["a", "b"], ["c", "d"])
        self.assertEqual(a, b, "input order must not change the bytes written")

    def test_duplicate_edges_collapse(self):
        r = map_core.position_diagram_region("k", ["a", "a"], [])
        self.assertEqual(r.count('"a"'), 1)

    def test_the_graph_region_is_declared_on_both_ticket_region_tuples(self):
        pair = (map_core.GRAPH_START, map_core.GRAPH_END)
        self.assertIn(pair, map_core.TICKET_REGIONS)
        self.assertIn(pair, map_core.TRACKER_TICKET_REGIONS)


LINT_INPUT = {
    "target": {"slug": "lint-effort"},
    "map": {"title": "Decision map - lint", "destination": "a written spec",
            "notes": "",
            "notYetSpecified": ["how we shard the write path"],
            "outOfScope": []},
    "tickets": [
        {"key": "alpha", "title": "Alpha question?", "type": "grilling",
         "question": "a?", "blocks": ["beta"]},
        {"key": "beta", "title": "Beta question?", "type": "grilling",
         "question": "b?"},
    ],
}

_DIAGRAM_BODY = "```mermaid\ngraph TD\n  A[\"chosen\"] --> B[\"effect\"]\n```\n"


class LintTest(unittest.TestCase):
    """lint is the check an agent can run: it answers pass/fail about the MAP,
    where every other subcommand only validates the one call being made. Each
    rule below exists because a flow skill states the invariant in prose and
    nothing enforces it (see references/data-contracts.md, `lint`)."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        ops.chart(self.root, LINT_INPUT, real=True)

    def tearDown(self):
        self.tmp.cleanup()

    def _lint(self):
        return ops.lint(self.root, "lint-effort")

    def _rules(self):
        return {f["rule"] for f in self._lint()["findings"]}

    def _patch_fm(self, ticket, **fields):
        """Hand-edit a ticket's frontmatter -- which is exactly what work-map
        Step 2 instructs a user to do to release a claim, so every corruption
        below is reachable by following the skill, not only by accident."""
        fm, body = ops._load_ticket(self.root, "lint-effort", ticket)
        fm.update(fields)
        ops._save_ticket(self.root, "lint-effort", ticket, fm, body)

    # --- the clean baseline -------------------------------------------------

    def test_a_freshly_charted_map_is_clean(self):
        out = self._lint()
        self.assertTrue(out["clean"], out["findings"])
        self.assertEqual(out["findings"], [])

    def test_a_normally_worked_ticket_stays_clean(self):
        """claim -> resolve is the whole happy path of work-map. resolve does
        NOT clear `assignee`, so a rule keyed on closed+claimed would fire on
        every correctly-worked ticket; this test is what pins that out."""
        ops.claim(self.root, "lint-effort", "alpha", "decide-1200")
        ops.resolve(self.root, "lint-effort", "alpha", "we chose X",
                    None, _DIAGRAM_BODY)
        out = self._lint()
        self.assertTrue(out["clean"], out["findings"])

    # --- edge integrity -----------------------------------------------------

    def test_a_blocker_that_is_not_a_ticket_is_an_error(self):
        self._patch_fm("beta", blocked_by=["ghost"])
        out = self._lint()
        self.assertIn("dangling-blocker", {f["rule"] for f in out["findings"]})
        self.assertFalse(out["clean"])
        f = next(f for f in out["findings"] if f["rule"] == "dangling-blocker")
        self.assertEqual(f["severity"], "error")
        self.assertEqual(f["ticket"], "beta")
        self.assertIn("ghost", f["message"])

    def test_a_ticket_that_blocks_itself_is_an_error(self):
        self._patch_fm("beta", blocked_by=["beta"])
        self.assertIn("self-blocked", self._rules())

    def test_a_blocking_cycle_is_an_error(self):
        """beta is already blocked by alpha; closing the loop makes both
        permanently unreachable -- frontier reports them as `blocked` forever
        and the map reads as stalled rather than broken."""
        self._patch_fm("alpha", blocked_by=["beta"])
        out = self._lint()
        self.assertIn("blocker-cycle", {f["rule"] for f in out["findings"]})
        cyc = next(f for f in out["findings"] if f["rule"] == "blocker-cycle")
        self.assertEqual(cyc["severity"], "error")
        self.assertIn("alpha", cyc["message"])
        self.assertIn("beta", cyc["message"])

    def test_a_cycle_is_reported_once_not_once_per_member(self):
        self._patch_fm("alpha", blocked_by=["beta"])
        found = [f for f in self._lint()["findings"] if f["rule"] == "blocker-cycle"]
        self.assertEqual(len(found), 1, found)

    # --- resolution integrity ----------------------------------------------

    def test_a_closed_ticket_with_no_resolution_block_is_an_error(self):
        self._patch_fm("beta", status="closed")
        self.assertIn("closed-without-resolution", self._rules())

    def test_a_resolution_without_a_mermaid_diagram_is_a_warning(self):
        """ADR 0065: the resolution body opens with one diagram of the ANSWER."""
        ops.resolve(self.root, "lint-effort", "alpha", "we chose X", None,
                    "just prose, no diagram\n")
        out = self._lint()
        f = next(f for f in out["findings"]
                 if f["rule"] == "resolution-without-diagram")
        self.assertEqual(f["severity"], "warning")
        self.assertEqual(f["ticket"], "alpha")

    def test_a_gist_over_the_limit_is_a_warning(self):
        """resolve warns past GIST_MAX on stderr and records it anyway, so the
        over-long gist is on disk and only lint can find it afterwards."""
        ops.resolve(self.root, "lint-effort", "alpha", "x" * (map_core.GIST_MAX + 1),
                    None, _DIAGRAM_BODY)
        self.assertIn("gist-too-long", self._rules())

    # --- claim hygiene ------------------------------------------------------

    def test_an_open_ticket_held_by_the_anonymous_default_is_a_warning(self):
        """work-map: an anonymous claim names nobody, so the files cannot tell
        you who holds it. "me" is argparse's default for --user."""
        ops.claim(self.root, "lint-effort", "alpha", "me")
        f = next(f for f in self._lint()["findings"]
                 if f["rule"] == "anonymous-claim")
        self.assertEqual(f["severity"], "warning")
        self.assertEqual(f["ticket"], "alpha")

    def test_a_named_claim_is_clean(self):
        ops.claim(self.root, "lint-effort", "alpha", "decide-1200")
        self.assertNotIn("anonymous-claim", self._rules())

    # --- fog hygiene --------------------------------------------------------

    def test_a_fog_line_that_became_a_ticket_is_a_warning(self):
        """Union never deletes, so graduating fog leaves the old line behind;
        both flow skills tell you to delete it by hand and nothing checks."""
        ops.chart(self.root, {
            "target": {"slug": "lint-effort"},
            "map": {"title": "Decision map - lint", "destination": "a written spec",
                    "notes": "", "notYetSpecified": [], "outOfScope": []},
            "tickets": [{"key": "write-path-sharding",
                         "title": "How we shard the write path?",
                         "type": "grilling", "question": "which key?"}],
        }, real=True)
        f = next(f for f in self._lint()["findings"]
                 if f["rule"] == "fog-line-graduated")
        self.assertEqual(f["severity"], "warning")
        self.assertIn("write-path-sharding", f["message"])

    def test_unrelated_fog_is_not_flagged(self):
        """A check that cries wolf is worse than no check: the fog rule is the
        only heuristic one, so it must stay silent on an ordinary map."""
        self.assertNotIn("fog-line-graduated", self._rules())

    # --- CLI contract -------------------------------------------------------

    def test_lint_on_a_map_that_does_not_exist_fails_like_read(self):
        with self.assertRaises(OSError):
            ops.lint(self.root, "no-such-map")

    def test_findings_exit_3_not_1(self):
        """1 is reserved: local_map_ops' exit-code comment gives it to an
        unexpected crash, so a caller can tell "your map has problems" from
        "this tool is broken"."""
        self._patch_fm("beta", blocked_by=["ghost"])
        r = subprocess.run(
            [sys.executable, str(Path(ops.__file__)), "lint",
             "--root", str(self.root), "--map", "lint-effort"],
            capture_output=True, text=True)
        self.assertEqual(r.returncode, 3, r.stderr)
        self.assertFalse(json.loads(r.stdout)["clean"])

    def test_a_clean_map_exits_0(self):
        r = subprocess.run(
            [sys.executable, str(Path(ops.__file__)), "lint",
             "--root", str(self.root), "--map", "lint-effort"],
            capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertTrue(json.loads(r.stdout)["clean"])

    def test_every_finding_names_its_rule_severity_and_message(self):
        self._patch_fm("beta", blocked_by=["ghost", "beta"])
        for f in self._lint()["findings"]:
            self.assertIn(f["severity"], ("error", "warning"), f)
            self.assertTrue(f["rule"] and f["message"], f)
            self.assertIn("ticket", f)


if __name__ == "__main__":
    unittest.main()
