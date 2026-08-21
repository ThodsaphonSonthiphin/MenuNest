#!/usr/bin/env python3
r"""github_map_ops.py — decision-map GitHub Issues backend (ADR 0062).

The map is an issue; each ticket is a native **sub-issue** of it; blocking is a
native **issue dependency**; the resolution is an issue comment. The contract is
`plugins/decision-map/references/data-contracts.md` and the subcommand surface is
identical to the local backend's — same flags, same JSON, same exit codes — so
the flow skills change only which script they call.

Stdlib only. Every API call goes through `gh api`, never a pinned `gh` feature:
first-class `--parent` / `--blocked-by` flags only arrived in gh 2.94.0, and this
must work on older installs (developed against 2.93.0).

WHY THIS IS SAFE TO WRITE NOW. ADR 0059 gated both tracker backends behind a
six-step probe of one bet: that a `<!-- decision-map:key:<key> -->` marker
written into an item's body survives round trips. The GitHub half of that gate
PASSED on 2026-08-01 (ADR 0060) — the marker came back byte-identical through
create -> GET, through a close/reopen, and through a human editing the body in
the web UI, which is the deciding test. The ADO half is still open, which is why
this file exists and `ado_map_ops.py` does not.

THE JOIN — one GraphQL round trip, and it must never truncate.

    key -> issue is built by enumerating the map's own sub-issues, never by a
    search. `_SNAPSHOT_QUERY` fetches the map, its children, their bodies, their
    labels, their assignees AND their blockedBy edges (including each edge
    target's body) in a single request. REST needs 1 + n calls for the same
    thing, because the sub-issue listing carries bodies but not dependencies.

    A truncated join is the worst failure this design can produce: a child the
    join does not see is labelled `create`, so a map is silently re-created in
    full and presented to the user as a page of ordinary, approvable `create`
    lines. GitHub caps sub-issues at 100 so `first: 100` covers every legal map,
    but `_snapshot` still FAILS LOUDLY on `hasNextPage` rather than trusting
    that cap to hold for ever.

WHAT COSTS WHAT (the contract asked for this to be written down before
implementing, because `resolve` turns out to be the expensive one, not the
cheapest):

    chart  (dry run) 1 GraphQL + 1 label list                      = 2 reads
    chart  (real)    the above + 1 write per missing label
                     + 1 map create-or-patch
                     + 2 writes per new ticket (create, then link)
                     + 1 write per new edge + 1 closing GraphQL
                     + 1 PATCH per ticket an edge touched this run, both ends
    read             1 GraphQL
    frontier         1 GraphQL
    claim            1 GraphQL + 1 PATCH               (+1 `gh api user`)
    block            1 GraphQL + 1 POST                (0 writes if it exists)
                     + 1 PATCH per end when the edge is new (2 PATCHes)
    comment          1 GraphQL + 1 POST
    resolve          1 GraphQL + 1 comment + 1 ticket PATCH (body AND state in
                     the same call) + 1 map PATCH      = 4 calls

    The decisions index is a projection over every ticket, which is why
    `resolve` touches the map at all — but the snapshot it already holds
    supplies every ticket's status and gist, so re-projecting costs no extra
    reads.

    The position-diagram patch on `chart` costs no extra READ: it reuses the
    "1 closing GraphQL" snapshot above, which is fetched only after every
    create, link and edge write, so it already carries the current state of
    every ticket an edge touched this run. `block` writes exactly one edge, so
    it folds that edge into each end's parent/child list BY HAND from the one
    snapshot it already holds, rather than fetch a second one mid-call.

RATE LIMITS. The binding limit is the **secondary** one — 80 content-creating
requests per minute — not the 5,000/hour primary. `_Throttle` below counts
writes in a sliding minute and sleeps only when it would otherwise cross the
line, so a small map runs at full speed and a 40-ticket chart paces itself
instead of failing halfway through with a map half-created.
"""
import argparse, json, subprocess, sys, time

from map_core import (
    VALID_TICKET_TYPES,
    KEY_MARKER, GIST_START, GIST_END, GRAPH_START, GRAPH_END,
    GIST_MAX, GIST_TOO_LONG,
    TYPES_EXPECTING_A_DOC, MISSING_DOC_LINK,
    MAP_REGIONS, TRACKER_TICKET_REGIONS,
    DECISIONS_START, DECISIONS_END,
    FORCE_COST,
    ChartValidationError, UnsafeIdentifierError, InvalidEdgeError,
    CliUsageError, MarkerIntegrityError, JoinIntegrityError,
    scrub, one_line, mode as derive_mode,
    assert_regions, safe_segment, norm_eol,
    region_body, replace_region, region_re,
    map_merge_detail, render_map_body, scalar_divergences, scalar_fields_for,
    merge_map_lists,
    decisions_region, validate_chart_input, key_of_body, milestone_index,
    milestone_progress,
    position_diagram_region, set_graph_region,
    lint_findings, RULES_NEEDING_RESOLUTION_BODY,
    force_orphaned_blockers, force_orphan_detail, rewired_edges,
)

MAP_LABEL = "decision-map:map"
TICKET_LABEL = "decision-map:ticket"
TYPE_LABEL = "decision-map:type:%s"
LABEL_COLOUR = "ededed"

# GitHub's own hard ceiling on sub-issues per parent, so also the hard ceiling
# on tickets per map. Checked BEFORE a real run: creating up to the cap and then
# failing halfway leaves a half-charted map, and the failure arrives after the
# user has already approved the plan.
MAX_TICKETS = 100
# Documented cap on dependencies per relationship type -- tighter than the
# sub-issue cap and a separate budget: a ticket cannot be blocked by more than
# 50 others.
MAX_BLOCKERS = 50
# The secondary limit is 80 content-creating requests per minute. Stay under it
# with headroom, because a retry after a 403 costs a minute.
WRITES_PER_MINUTE = 70

_GIST_BLOCK_RE = region_re(GIST_START, GIST_END)
_DECISIONS_BLOCK_RE = region_re(DECISIONS_START, DECISIONS_END)

# The overview diagram every generated decision-map document opens with. The
# local backend's names files; this one names the native mechanisms, because
# that is what a reader of the issue can actually click on.
_MAP_DIAGRAM = (
    "```mermaid\ngraph TD\n"
    "    MAP[\"map (this issue)\"] --> T[\"sub-issues — one decision each\"]\n"
    "    T --> B[\"blocked-by dependencies\"]\n"
    "    T --> D[\"Decisions so far (index below)\"]\n```\n\n")


class GitHubError(Exception):
    """A `gh api` call failed, or returned something unusable. Reported as one
    line on stderr like every other known failure -- never as a traceback, and
    never alongside a partial JSON document on stdout."""


class MapNotFoundError(Exception):
    """The map named by --map could not be resolved to an issue.

    Kept distinct from GitHubError so the stderr line can DISTINGUISH the four
    read failures a tracker has and the local backend does not: absent, not a
    decision-map map, no permission, and deleted. GitHub answers 404 for both
    "does not exist" and "you cannot see it" on a private repo, so those two
    share one message that says so rather than guessing."""


class MapAbsentError(MapNotFoundError):
    """The map genuinely does not exist -- nothing in the repo carries that key.

    This is the ONLY failure `chart` may read as "safe to create", and it exists
    as its own class because catching the parent swallowed three failures that
    are not absence at all: two issues claiming one slug, an issue that exists
    but is not a decision-map map, and a 404 that might be a permission problem.
    Treating any of those as "not there" made `chart` create a SECOND map and
    label every existing ticket `create` -- the silent full re-creation this
    design names as the worst thing it can do."""


# ---------------------------------------------------------------------------
# transport
# ---------------------------------------------------------------------------
class _Throttle:
    """Sliding-window write pacing for GitHub's secondary rate limit.

    Not a fixed sleep between writes: that would make a 3-ticket chart wait for
    no reason. Records the timestamp of each write and sleeps only when the
    window is genuinely full, so small maps run at full speed and large ones
    slow down exactly enough.
    """

    def __init__(self, per_minute=WRITES_PER_MINUTE, sleep=time.sleep,
                 clock=time.monotonic):
        self.per_minute = per_minute
        self._sleep = sleep
        self._clock = clock
        self._stamps = []

    def note_write(self):
        now = self._clock()
        self._stamps = [t for t in self._stamps if now - t < 60.0]
        if self.per_minute and len(self._stamps) >= self.per_minute:
            wait = 60.0 - (now - self._stamps[0]) + 0.5
            if wait > 0:
                print(f"pacing: {len(self._stamps)} writes in the last minute; "
                      f"sleeping {wait:.0f}s to stay under GitHub's secondary "
                      "rate limit", file=sys.stderr)
                self._sleep(wait)
            now = self._clock()
            self._stamps = [t for t in self._stamps if now - t < 60.0]
        self._stamps.append(now)


class GhApi:
    """`gh api` as a callable object, so tests can substitute a fake.

    Payloads travel on **stdin** (`--input -`), never as a command-line
    argument: PowerShell 5.1 mangles native-exe arguments containing quotes, and
    a ticket title or question routinely contains them.
    """

    API_VERSION = "2022-11-28"

    def __init__(self, throttle=None, timeout=120, retries=3):
        self.throttle = throttle or _Throttle()
        self.timeout = timeout
        self.retries = retries
        self.calls = 0

    def _run(self, cmd, data):
        for attempt in range(self.retries):
            self.calls += 1
            p = subprocess.run(cmd, input=data, capture_output=True, text=True,
                               encoding="utf-8", errors="replace",
                               timeout=self.timeout)
            if p.returncode == 0:
                return p.stdout
            err = ((p.stderr or "") + (p.stdout or "")).strip()
            # Only a rate-limit refusal is worth retrying. Everything else is a
            # deterministic 4xx and a retry just burns another call.
            if "rate limit" in err.lower() and attempt < self.retries - 1:
                wait = 60 * (attempt + 1)
                print(f"rate limited; sleeping {wait}s then retrying",
                      file=sys.stderr)
                time.sleep(wait)
                continue
            raise GitHubError(f"gh api {' '.join(cmd[2:])} failed: {err[:900]}")
        raise GitHubError("gh api exhausted its retries against a rate limit")

    def rest(self, path, method=None, payload=None):
        cmd = ["gh", "api", "-H", f"X-GitHub-Api-Version: {self.API_VERSION}"]
        if method:
            cmd += ["--method", method]
        cmd.append(path)
        if payload is not None:
            cmd += ["--input", "-"]
        # Pace on the METHOD, not on whether there is a payload. GitHub's
        # secondary limit counts content-creating requests, and the
        # remove-dependency call on the --force path is a DELETE with no body:
        # counting payloads meant a --force over a well-connected map issued
        # dozens of unpaced writes and could trip the limit mid-rewrite, leaving
        # tickets half-reset.
        if method and method.upper() != "GET":
            self.throttle.note_write()
        out = self._run(cmd, json.dumps(payload) if payload is not None else None)
        return json.loads(out) if out.strip() else {}

    def graphql(self, query, variables):
        out = self._run(["gh", "api", "graphql", "--input", "-"],
                        json.dumps({"query": query, "variables": variables}))
        doc = json.loads(out) if out.strip() else {}
        if doc.get("errors"):
            raise GitHubError("GraphQL: " + "; ".join(
                e.get("message", str(e)) for e in doc["errors"]))
        return doc.get("data") or {}

    def whoami(self):
        return (self.rest("user") or {}).get("login")


# ---------------------------------------------------------------------------
# the snapshot: one round trip, everything the join needs
# ---------------------------------------------------------------------------
# `body` is selected on the blockedBy targets too, and that is not redundant.
# An edge whose target is not a child of this map is one of two very different
# things, and the target's body is the only way to tell them apart in the same
# request:
#   - it carries a decision-map key marker -> it WAS a ticket and no longer is
#     (deleted from the map, re-parented). It must keep blocking (ADR 0061).
#   - it carries none -> a cross-map or hand-made dependency, which this design
#     does not model, so it is ignored (with a warning, never silently).
_SNAPSHOT_QUERY = """
query($owner:String!,$repo:String!,$number:Int!){
  repository(owner:$owner,name:$repo){
    issue(number:$number){
      number databaseId title body state url
      labels(first:50){nodes{name}}
      subIssues(first:100){
        totalCount
        pageInfo{hasNextPage}
        nodes{
          number databaseId title body state url
          repository{nameWithOwner}
          assignees(first:10){nodes{login}}
          labels(first:50){nodes{name}}
          blockedBy(first:50){
            totalCount
            nodes{ number databaseId body repository{nameWithOwner} }
          }
        }
      }
    }
  }
}
"""


def _state(gql_state):
    """GraphQL returns OPEN/CLOSED; REST returns open/closed; the contract wants
    open/closed. Read `state` ALONE -- never `state_reason`, which is nullable on
    a closed issue and whose enum has grown past completed/not_planned to include
    duplicate and reopened, so any check that enumerates reasons mis-reads issues
    that are plainly closed."""
    return "closed" if str(gql_state).lower() == "closed" else "open"


def _label_names(node):
    return [n["name"] for n in ((node.get("labels") or {}).get("nodes") or [])]


def _repo_of(node, default):
    """`owner/name` for an issue node, falling back to the map's repo.

    The fallback covers a node the query did not select `repository` on; every
    node this backend reads does select it, so in practice the default is only
    reached by a fixture that predates the field.
    """
    return ((node.get("repository") or {}).get("nameWithOwner")) or default


def _ref_of(node, default_repo):
    """The only identity an issue actually has: (repo, number).

    An issue number is unique **per repository**, not globally, and a sub-issue
    may live in another repo of the same owner. A join keyed on the number alone
    resolves #7-over-there to the key of #7-over-here."""
    return (_repo_of(node, default_repo), node["number"])


def _type_of(labels, what):
    """The ticket type, from its `decision-map:type:<type>` label.

    Native GitHub issue types are organisation-scoped and simply absent on a
    user-owned repo, which is why the type rides on a label instead.

    A ticket whose type label a human removed falls back to `grilling`, which
    derives mode HITL. That is the safe direction: HITL means the session stops
    and asks a human, where AFK would send an agent off unattended on a decision
    nobody chose to delegate. The fallback is announced, never silent.
    """
    found = [x[len("decision-map:type:"):] for x in labels
             if x.startswith("decision-map:type:")]
    found = [f for f in found if f in VALID_TICKET_TYPES]
    if len(found) == 1:
        return found[0]
    if not found:
        print(f"warning: {what} carries no decision-map:type:<type> label; "
              "treating it as 'grilling' (mode HITL, so a human is asked). "
              "Re-add the label to restore its real type", file=sys.stderr)
    else:
        print(f"warning: {what} carries {len(found)} decision-map:type labels "
              f"({', '.join(sorted(found))}); treating it as 'grilling' "
              "(mode HITL). Remove the extras", file=sys.stderr)
    return "grilling"


class Snapshot:
    """One map issue and its children, as a single consistent read.

    Everything downstream — the plan, `read`, `frontier`, every edge decision —
    is computed from one of these, so no subcommand can see two different
    versions of the map partway through.
    """

    def __init__(self, repo, node):
        self.repo = repo
        self.map = node
        self.is_map = MAP_LABEL in _label_names(node)
        self.map_key = key_of_body(
            node.get("body"), f"the map issue #{node['number']}",
            labelled=self.is_map)
        # Parsed once here, not per ticket: the map body is already in hand and
        # both map_json and frontier read the same answer from it.
        # The unparsable lines are discarded here, matching the local backend's
        # convention at the same three call sites: `lint` re-parses the body and
        # is the one command that reports them, and a field nothing reads is a
        # promise of reporting that was never wired.
        self.milestones, self.milestone_of, _ = \
            milestone_index(norm_eol(node.get("body")))
        sub = node.get("subIssues") or {}
        if (sub.get("pageInfo") or {}).get("hasNextPage"):
            # Never truncate the join. A child the join cannot see is labelled
            # `create`, which re-creates a ticket that already exists and shows
            # that to the user as an ordinary line to approve.
            raise JoinIntegrityError(
                f"map issue #{node['number']} reports more than 100 sub-issues, "
                "so this snapshot would be incomplete. Refusing rather than "
                "risk re-creating tickets the join could not see")
        self.tickets = {}        # key -> child node
        self.by_ref = {}         # (repo, number) -> key
        self.ignored = []        # children that are not decision-map tickets
        for child in (sub.get("nodes") or []):
            labels = _label_names(child)
            what = f"issue #{child['number']} ({child.get('title')!r})"
            key = key_of_body(child.get("body"), what,
                              labelled=TICKET_LABEL in labels)
            if key is None:
                # Neither the label nor a marker: not a decision-map ticket at
                # all, so an unrelated hand-created sub-issue cannot collide
                # with the join.
                self.ignored.append(child["number"])
                continue
            if key in self.tickets:
                raise JoinIntegrityError(
                    f"two sub-issues of #{node['number']} resolve to the key "
                    f"{key!r} (#{self.tickets[key]['number']} and "
                    f"#{child['number']}); keys are unique within a map. Fail "
                    "rather than pick one")
            self.tickets[key] = child
            self.by_ref[_ref_of(child, repo)] = key

    def repo_of(self, key):
        """The repository the ticket's issue actually lives in.

        **Not assumed to be the map's repo.** A sub-issue only has to share the
        parent's repository *owner*, so it may legitimately live in a different
        repo of that owner — and issue numbers restart per repository, so #7 in
        one repo and #7 in another are different issues. Keying the join on the
        bare number and addressing every write to the map's repo meant a
        cross-repo ticket's `resolve`, `claim` or `comment` landed on whatever
        unrelated issue happened to share its number, and the join reported the
        wrong key for it.
        """
        return _repo_of(self.tickets[key], self.repo)

    # --- derived views -----------------------------------------------------
    @property
    def keys(self):
        """Every ticket key, **key-ascending**.

        Ordering is fixed here rather than left to the tracker's natural
        creation order, so `map.json` and `frontier.json` are a deterministic
        function of logical state and the two backends emit the same document
        for the same map (the local backend globs a directory, which is
        key-ascending too).
        """
        return sorted(self.tickets)

    def key_for(self, ident):
        """The key for a `--ticket` value, which may be a key OR an issue number.

        `map.json` reports each ticket's `id` as its issue number, and the
        contract says `id` exists "for passing back to `--ticket`". It could not
        be: every ticket subcommand looked the value up in `self.tickets`, which
        is keyed by key only, so `--ticket 1235` failed with "no ticket with key
        '1235'" for a ticket the tool had just reported as id 1235.
        """
        if ident in self.tickets:
            return ident
        if isinstance(ident, str) and ident.isdigit():
            n = int(ident)
            for key, t in self.tickets.items():
                if t["number"] == n:
                    return key
        raise MapNotFoundError(
            f"no ticket with key or issue number {ident!r} on map "
            f"#{self.map['number']} in {self.repo}; `read --map` lists what it "
            "does have")

    def number_of(self, key):
        t = self.tickets.get(key)
        if t is None:
            raise MapNotFoundError(
                f"no ticket with key {key!r} on map #{self.map['number']} "
                f"in {self.repo}; `read --map` lists the keys it does have")
        return t["number"]

    def blocker_refs_of(self, key):
        """The RAW edge targets as (repo, number) — every one, resolvable or not.

        This, not `blockers_of`, is what a write decision must consult.
        `blockers_of` returns *keys*, and it deliberately includes the key of a
        re-parented ticket so `missingBlockers` can name it (ADR 0061) — which
        made a stale edge indistinguishable from a live one:

        - `block` and additive `chart` saw the orphan's key in the held list and
          concluded the edge existed, so a genuine new edge to the ticket that
          now holds that key was **never written**, and both reported success;
        - `--force` promises to reset `blockedBy`, but removal was looked up by
          key in `self.tickets`, where an orphan is absent — so the removal
          silently no-op'd and the rewritten ticket stayed blocked by an item
          that can never close, permanently quarantining it.

        Both failures come from asking "is this key blocking?" when the question
        is "does an edge to *this issue* exist?".
        """
        node = self.tickets[key]
        edges = (node.get("blockedBy") or {})
        if (edges.get("totalCount") or 0) > len(edges.get("nodes") or []):
            raise JoinIntegrityError(
                f"issue #{node['number']} reports {edges['totalCount']} blockers "
                f"but only {len(edges.get('nodes') or [])} were returned; "
                "refusing to report a partial blocker list as complete")
        return [(_ref_of(t, self.repo), t["databaseId"])
                for t in (edges.get("nodes") or [])]

    def holds_edge(self, blocked_key, blocker_key):
        """Does `blocked_key` already have an edge to the ISSUE that is
        `blocker_key` right now? Keyed on identity, never on the key string."""
        want = _ref_of(self.tickets[blocker_key], self.repo)
        return any(ref == want for ref, _db in self.blocker_refs_of(blocked_key))

    def blockers_of(self, key):
        """-> (blocker_keys, missing_keys). Both are keys, never native ids.

        Three cases, and the difference between the last two is the whole
        reason the snapshot query fetches each edge target's body:

        1. the target is a current child -> its key blocks, normally;
        2. the target is not a child but carries a key marker -> it WAS a ticket
           of this map and is not any more. It keeps blocking and is named in
           `missingBlockers`. An absence is not a resolution: wrongly holding a
           ticket back costs one question to a human, while wrongly releasing it
           starts a session on work the map says is not ready (ADR 0061);
        3. the target carries no marker -> a cross-map or hand-added dependency.
           decision-map models dependencies within one map only, so it cannot be
           expressed as a key and is ignored -- but a `warning:` line says so,
           because a silently dropped blocker reads to the user as an unblocked
           ticket.
        """
        node = self.tickets[key]
        edges = (node.get("blockedBy") or {})
        if (edges.get("totalCount") or 0) > len(edges.get("nodes") or []):
            raise JoinIntegrityError(
                f"issue #{node['number']} reports {edges['totalCount']} blockers "
                f"but only {len(edges.get('nodes') or [])} were returned; "
                "refusing to report a partial blocker list as complete")
        blockers, missing = [], []
        for target in (edges.get("nodes") or []):
            hit = self.by_ref.get(_ref_of(target, self.repo))
            if hit is not None:
                if hit not in blockers:
                    blockers.append(hit)
                continue
            orphan = key_of_body(
                target.get("body"),
                f"issue #{target['number']}, a blocker of #{node['number']}",
                labelled=False)
            if orphan is not None:
                if orphan not in blockers:
                    blockers.append(orphan)
                if orphan not in missing:
                    missing.append(orphan)
            else:
                print(f"warning: issue #{node['number']} ({key}) is blocked by "
                      f"#{target['number']}, which is not a ticket of this map; "
                      "decision-map models dependencies within one map only, so "
                      "that edge is not reported in blockedBy. Express it as a "
                      "fog line or a note instead", file=sys.stderr)
        return blockers, missing

    def gist_of(self, key):
        """The stored gist, read with the WRITER's own flattener.

        `one_line`, never a bare `str.split()`. This is the escape-order rule
        (flatten first, escape last, then touch nothing) applied to the READ
        side, and getting it wrong here was the same defect the writer took five
        review rounds to get right, reintroduced from the other end:

        `one_line` flattens with `str.splitlines()`, which does NOT break on
        TAB. `" ".join(inner.split())` DOES. So a gist of
        `"<!--\\tdecision-map:decisions:end -->"` stored verbatim (scrub found no
        marker -- the prefix needs a space) came back out of this method as a
        **live** `<!-- decision-map:decisions:end -->` that no escape had ever
        seen. Written into the map's decisions index it made the region unpaired,
        so the next `resolve` raised MarkerIntegrityError *after* posting its
        comment and closing its ticket -- and every later `resolve` on that map
        failed at the same point, so the index could never be rebuilt by any
        non-destructive operation. Worse, a crafted pair balanced the counts and
        was accepted, planting a live `fog` region inside the decisions region so
        the next additive `chart` merged fog lines into the wrong list.

        It also had a consequence needing no tab and no malice: `" ".join(split())`
        collapses an ordinary double space, so `resolve` returned `"shared  keys"`
        as the gist "as stored" while `read` reported `"shared keys"`. The
        contract says that value IS as stored, and byte-identity broke on text
        nobody touched.

        The region is written as GIST_START + "\\n" + one line + "\\n" + GIST_END,
        so the reader only ever has to shed the two framing newlines. A value
        with an interior newline can only be a hand edit, and putting it through
        `one_line` flattens *and* escapes it, in that order -- which is exactly
        what the invariant requires of anything entering the tool's own field.
        """
        body = norm_eol(self.tickets[key].get("body"))
        inner = region_body(body, GIST_START, GIST_END)
        if inner is None:
            return None
        return one_line(inner.strip("\n")) or None

    def assignee_of(self, key):
        logins = [a["login"] for a in
                  ((self.tickets[key].get("assignees") or {}).get("nodes") or [])]
        return logins[0] if logins else None

    def ticket_json(self, key):
        t = self.tickets[key]
        ttype = _type_of(_label_names(t), f"issue #{t['number']} ({key})")
        blockers, _missing = self.blockers_of(key)
        return {
            "key": key,
            # BOTH handles, because they answer different questions: `id` is the
            # issue number, which is what a human reads and what `--ticket`
            # accepts (`key_for` takes either), while every sub-issue and
            # dependency mutation is keyed on the unrelated database id.
            # Carrying only one costs an extra resolve call per write.
            "id": str(t["number"]), "dbId": t["databaseId"],
            "repo": self.repo_of(key),
            "name": t.get("title") or key,
            "url": t.get("url"),
            "type": ttype, "mode": derive_mode(ttype),
            "status": _state(t.get("state")),
            "assignee": self.assignee_of(key),
            "blockedBy": blockers,
            "gist": self.gist_of(key),
            "milestone": self.milestone_of.get(key),
        }

    def map_json(self):
        body = norm_eol(self.map.get("body"))
        dest = ""
        head = body.split("## Destination\n", 1)
        if len(head) == 2:
            dest = head[1].split("\n\n", 1)[0].split("\n##", 1)[0].strip()
        return {
            "backend": "github",
            "map": {"id": str(self.map["number"]), "dbId": self.map["databaseId"],
                    "key": self.map_key,
                    "name": self.map.get("title"),
                    "url": self.map.get("url"),
                    "destination": dest},
            "milestones": self.milestones,
            "tickets": [self.ticket_json(k) for k in self.keys],
        }


# ---------------------------------------------------------------------------
# document rendering
# ---------------------------------------------------------------------------
def render_map_issue_body(m, slug):
    """The map issue body: key marker, overview diagram, then the shared regions.

    The name is NOT repeated as an H1 — the issue's own title carries it, and a
    duplicate heading is the kind of thing a human deletes, which would then
    read as a divergence for ever.
    """
    prologue = (KEY_MARKER % slug) + "\n\n" + _MAP_DIAGRAM
    return render_map_body(m, prologue)


def render_ticket_issue_body(key, question):
    """The ticket issue body: key marker, the question, the position diagram,
    an empty gist region.

    The QUESTION comes first (ADR 0102) -- the card's identity is what it asks,
    and the diagram is context read second. Every region is still written at
    creation rather than inserted later, for the reason the local backend does
    the same: a writer that has to decide *where* a region goes is guessing at
    the boundary of content it did not write, which is the pattern that cost
    three review rounds. A fresh ticket has no blockers and unblocks nothing
    yet, so the diagram renders with empty parent/child lists; `chart`'s
    edge-wiring pass re-renders it once real edges exist.
    """
    return (f"{KEY_MARKER % key}\n\n## Question\n\n{scrub(question)}\n\n"
            f"{position_diagram_region(key, [], [])}\n"
            f"{GIST_START}\n{GIST_END}\n")


def _assert_map_body(text, what):
    assert_regions(text, MAP_REGIONS, what, single_markers=(_key_marker_in(text),))


def _assert_ticket_body(text, what):
    assert_regions(text, TRACKER_TICKET_REGIONS, what,
                   single_markers=(_key_marker_in(text),))


def _key_marker_in(text):
    """The exact key marker string a document holds, for assert_regions to
    account for. Returns a marker that cannot occur when there is none, so a
    body carrying no key marker is not silently granted one free stray marker.
    """
    found = key_of_body(text, "this document", labelled=False)
    return KEY_MARKER % found if found else "\x00"


# ---------------------------------------------------------------------------
# the backend
# ---------------------------------------------------------------------------
class GitHubOps:
    def __init__(self, api, repo):
        if not repo or "/" not in repo:
            raise CliUsageError(
                "--repo OWNER/REPO is required (and is never inferred from the "
                "current git remote -- this writes issues to it)")
        self.api = api
        self.repo = repo
        self.owner, self.name = repo.split("/", 1)

    # --- reads -------------------------------------------------------------
    def _scan_for_slug(self, slug, labelled_only):
        """Issue numbers whose body carries the key marker for `slug`.

        `labelled_only=True` filters the listing on the map label -- the fast
        path, bounded by the number of maps in the repo. `False` walks every
        issue in the repo, which is the slow path used only to confirm an
        absence before creating (see `_issue_number`).

        Either way this is a **listing**, not the search API: strongly
        consistent, where code search is eventually consistent and a stale index
        would mean duplicate maps. Tickets are never resolved this way -- a
        ticket is found only through its own map's children.

        A labelled issue carrying NO key marker is skipped with a warning here,
        not treated as fatal. The contract's loud-error rule is about a labelled
        **child of the map being read**, where ignoring it hides a ticket from
        the join; applying it to every labelled issue in the repo instead meant
        one hand-labelled issue made *every* map in the repo unreadable.
        """
        hits, page = [], 1
        query = f"&labels={MAP_LABEL}" if labelled_only else ""
        while page <= 100:
            batch = self.api.rest(f"repos/{self.repo}/issues?state=all"
                                  f"{query}&per_page=100&page={page}")
            if not isinstance(batch, list) or not batch:
                break
            for issue in batch:
                try:
                    found = key_of_body(issue.get("body"),
                                        f"issue #{issue['number']}",
                                        labelled=False)
                except JoinIntegrityError as e:
                    print(f"warning: skipping issue #{issue['number']} while "
                          f"looking for map {slug!r}: {e}", file=sys.stderr)
                    continue
                if found is None and MAP_LABEL in [
                        x.get("name") for x in (issue.get("labels") or [])]:
                    # Carrying the tool's own label with no marker is either a
                    # map whose marker was stripped or a mislabel. Neither is
                    # fatal here -- the marker is what identifies a map, and
                    # failing would let one mislabelled issue break every map in
                    # the repo -- but it is never passed over in silence.
                    print(f"warning: issue #{issue['number']} carries the "
                          f"{MAP_LABEL} label but no decision-map key marker, so "
                          "it is not a map. Remove the label, or restore the "
                          "marker if this was a map", file=sys.stderr)
                if found == slug:
                    hits.append(issue["number"])
            if len(batch) < 100:
                break
            page += 1
        return hits

    def _issue_number(self, ref, confirm_absence=False):
        """Resolve --map to an issue number.

        A number is taken as a number; anything else is a slug.

        `confirm_absence=True` is used by `chart`, and it is not an
        optimisation -- it closes the worst hole this backend had. Map discovery
        went through the `decision-map:map` **label**, and the label is
        decorative: the key marker is what identifies an item. So a map whose
        label a human removed resolved to nothing, `chart` concluded the map did
        not exist, and it created a SECOND map while labelling every existing
        ticket `create` -- presenting a full silent re-creation to the user as a
        page of ordinary, approvable lines. That is the exact failure this
        contract calls the worst it can produce, and keying the *map* on its
        label reintroduced it after the ticket join had been carefully written
        not to.

        So before concluding a map is absent, `chart` walks every issue in the
        repo looking for the marker. That costs one call per 100 issues, once,
        only on the path that would otherwise create a duplicate map.
        """
        if isinstance(ref, str) and ref.isdigit():
            return int(ref)
        slug = safe_segment(ref, "map slug")
        hits = self._scan_for_slug(slug, labelled_only=True)
        if not hits and confirm_absence:
            hits = self._scan_for_slug(slug, labelled_only=False)
            if hits:
                print(f"warning: map {slug!r} is issue #{hits[0]} but has lost "
                      f"its {MAP_LABEL} label. Treating the key marker as "
                      "authoritative (it is) and re-adding the label rather "
                      "than creating a second map", file=sys.stderr)
        if not hits:
            raise MapAbsentError(
                f"no issue in {self.repo} carries the key marker for slug "
                f"{slug!r}. If the map does not exist yet, chart it; if it "
                "does, its key marker was removed by hand -- pass its issue "
                "number as --map instead")
        if len(hits) > 1:
            # NOT MapAbsentError: two maps is a state a human must resolve, and
            # letting chart read it as absence made it create a third.
            raise MapNotFoundError(
                f"{len(hits)} issues in {self.repo} claim the map slug {slug!r} "
                f"(#{', #'.join(str(h) for h in hits)}); a slug identifies one "
                "map. Fix by hand rather than have this pick one")
        return hits[0]

    def snapshot(self, ref, confirm_absence=False):
        number = self._issue_number(ref, confirm_absence=confirm_absence)
        data = self.api.graphql(_SNAPSHOT_QUERY, {
            "owner": self.owner, "repo": self.name, "number": number})
        node = ((data.get("repository") or {}).get("issue"))
        if not node:
            # GitHub answers 404 for "no such issue" and for "you cannot see
            # it" alike on a private repo, and GraphQL returns a null node for
            # a deleted one. Say so instead of asserting which it is. NOT
            # MapAbsentError: "I cannot see it" must never be read as "it is not
            # there", or chart creates a duplicate of a map it lacked access to.
            raise MapNotFoundError(
                f"issue #{number} was not returned by {self.repo}: it does not "
                "exist, was deleted, or the token cannot see it (GitHub does "
                "not distinguish absent from forbidden). Check the number and "
                "that the token has `repo` access")
        snap = Snapshot(self.repo, node)
        if snap.map_key is None:
            # Also not absence: this issue exists, it is simply the wrong one.
            raise MapNotFoundError(
                f"issue #{number} in {self.repo} is not a decision-map map: it "
                "carries no <!-- decision-map:key:... --> marker. Pass the map "
                "issue, not a ticket")
        if not snap.is_map:
            print(f"warning: issue #{number} carries a decision-map key marker "
                  f"but not the {MAP_LABEL} label. The marker is authoritative, "
                  "so it is being read as a map; re-add the label so humans can "
                  "filter for it", file=sys.stderr)
        return snap

    def try_snapshot(self, ref):
        """The map, or None when nothing in the repo carries that key.

        **Only genuine absence becomes None**, and that distinction is the whole
        point of MapAbsentError. Catching every MapNotFoundError here meant three
        non-absences -- two maps claiming one slug, an issue that is not a map,
        and a 404 that may be a permission failure -- all read as "not there",
        so `chart` created a duplicate map and labelled every existing ticket
        `create`.
        """
        try:
            return self.snapshot(ref, confirm_absence=True)
        except MapAbsentError:
            return None

    def existing_labels(self):
        names, page = set(), 1
        while page <= 10:
            batch = self.api.rest(f"repos/{self.repo}/labels?per_page=100&page={page}")
            if not isinstance(batch, list) or not batch:
                break
            names.update(x["name"] for x in batch)
            if len(batch) < 100:
                break
            page += 1
        return names

    # --- writes ------------------------------------------------------------
    def ensure_label(self, name):
        try:
            self.api.rest(f"repos/{self.repo}/labels", "POST",
                          {"name": name, "color": LABEL_COLOUR,
                           "description": "decision-map (tool-managed)"})
        except GitHubError as e:
            # A label created concurrently answers 422 already_exists. That is
            # the desired end state, so it is not an error -- but anything else
            # is, and must not be swallowed.
            if "already_exists" not in str(e):
                raise

    # Every per-issue write takes an explicit `repo`, defaulting to the map's.
    # A sub-issue only has to share the parent's *owner*, so it may live in
    # another repo of that owner -- and issue numbers restart per repository.
    # Addressing these at the map's repo unconditionally meant a cross-repo
    # ticket's claim, comment, close and gist all landed on whatever unrelated
    # issue happened to share its number.
    def create_issue(self, title, body, labels):
        return self.api.rest(f"repos/{self.repo}/issues", "POST",
                             {"title": one_line(title), "body": body,
                              "labels": sorted(labels)})

    def patch_issue(self, number, payload, repo=None):
        return self.api.rest(f"repos/{repo or self.repo}/issues/{number}",
                             "PATCH", payload)

    def link_child(self, parent_number, child_db_id):
        # Keyed on the DATABASE id, not the issue number -- they are unrelated
        # values and passing the number here silently targets a different issue
        # or 404s. The endpoint lives on the PARENT's repo, always.
        self.api.rest(f"repos/{self.repo}/issues/{parent_number}/sub_issues",
                      "POST", {"sub_issue_id": child_db_id})

    def add_blocked_by(self, number, blocker_db_id, repo=None):
        self.api.rest(
            f"repos/{repo or self.repo}/issues/{number}/dependencies/blocked_by",
            "POST", {"issue_id": blocker_db_id})

    def remove_blocked_by(self, number, blocker_db_id, repo=None):
        self.api.rest(
            f"repos/{repo or self.repo}/issues/{number}/dependencies/"
            f"blocked_by/{blocker_db_id}", "DELETE")

    def add_comment(self, number, body, repo=None):
        return self.api.rest(f"repos/{repo or self.repo}/issues/{number}/comments",
                             "POST", {"body": body})


# ---------------------------------------------------------------------------
# chart
# ---------------------------------------------------------------------------
def _plan_map(ops, snap, inp, slug, force):
    """-> (action, body_or_None, detail_or_None, divergences) for the map issue."""
    m = inp["map"]
    if snap is None:
        return "create", render_map_issue_body(m, slug), None, []
    if force:
        return "OVERWRITE", render_map_issue_body(m, slug), None, []
    existing = norm_eol(snap.map.get("body"))
    # The title lives in the issue's own title FIELD, not in the body, so it is
    # compared against the title. Comparing it against the body reported a
    # `title` divergence on every single re-chart.
    div = scalar_divergences(m, snap.map.get("title") or "", fields=("title",))
    div += scalar_divergences(
        m, existing, fields=scalar_fields_for(existing, ("destination", "notes")))
    text, added, list_div = merge_map_lists(existing, m)
    div += list_div
    if text == existing:
        return "skip (exists)", None, None, div
    return "merge", text, map_merge_detail(added), div


def chart_plan(ops, snap, inp, force):
    """Everything a chart would touch, in plan order: labels, the map, then the
    tickets and the edges.

    Label provisioning is IN the plan rather than a silent preflight, because
    the contract's rule is that nothing the run writes may be missing from it --
    and a run that creates three repository-wide labels is writing.
    """
    slug = inp["target"]["slug"]
    entries, div = [], []

    wanted = {MAP_LABEL, TICKET_LABEL}
    wanted |= {TYPE_LABEL % t["type"] for t in inp["tickets"]}
    if snap is not None:
        wanted |= {TYPE_LABEL % snap.ticket_json(k)["type"] for k in snap.keys}
    have = ops.existing_labels()
    for name in sorted(wanted - have):
        entries.append({"path": f"label:{name}", "action": "create", "detail": None})

    map_action, map_body, map_detail, map_div = _plan_map(ops, snap, inp, slug, force)
    # A map whose own label a human stripped is still the map (the key marker is
    # authoritative), but leaving the label off means the fast slug lookup keeps
    # missing it and every future chart pays for the full-repo confirmation
    # sweep. Restore it, and say so in the plan rather than doing it silently.
    relabel = snap is not None and not snap.is_map
    if relabel and map_action == "skip (exists)":
        map_action, map_detail = "merge", f"re-adds the {MAP_LABEL} label"
    elif relabel and map_action == "merge":
        map_detail = f"{map_detail}; re-adds the {MAP_LABEL} label"
    entries.append({"path": "<map>", "action": map_action, "detail": map_detail})
    div += map_div

    existing_keys = set(snap.keys) if snap else set()
    action_by_key = {}
    for t in inp["tickets"]:
        key = t["key"]
        if key not in existing_keys:
            action = "create"
        else:
            action = "OVERWRITE" if force else "skip (exists)"
        action_by_key[key] = action
        entries.append({"path": key, "action": action, "detail": None})

    # The ceiling is GitHub's, not ours, and it must be checked before the write
    # rather than discovered at ticket 101 with a half-charted map on the repo.
    new_count = sum(1 for a in action_by_key.values() if a == "create")
    if len(existing_keys) + new_count > MAX_TICKETS:
        raise ChartValidationError(
            f"this chart would give map {slug!r} "
            f"{len(existing_keys) + new_count} tickets, and GitHub allows at "
            f"most {MAX_TICKETS} sub-issues per parent. Split the effort across "
            "two maps rather than have the run fail partway through")

    # Edges. An existing ticket gains one blockedBy entry and nothing else; one
    # already holding the edge is absent from the plan entirely, because neither
    # `chart` nor `block` writes for an edge that exists.
    by_key = {e["path"]: e for e in entries}
    pending = {}
    for t in inp["tickets"]:
        for blocked in t.get("blocks") or []:
            if action_by_key.get(blocked) in ("create", "OVERWRITE"):
                continue                       # wired as part of creating it
            # Ask "does an edge to THIS ISSUE exist", not "is this key already in
            # the blocker list". `blockers_of` deliberately reports the key of a
            # re-parented ticket so missingBlockers can name it, so testing
            # membership there made a stale edge look like a live one and the
            # genuine new edge was never written -- silently, at exit 0.
            if snap is not None and blocked in snap.tickets \
                    and t["key"] in snap.tickets and snap.holds_edge(blocked, t["key"]):
                continue                       # genuinely no change
            got = pending.setdefault(blocked, [])
            if t["key"] not in got:            # `blocks` may name a target twice
                got.append(t["key"])
    for blocked, blockers in pending.items():
        held = snap.blocker_refs_of(blocked) if (snap and blocked in snap.tickets) else []
        if len(held) + len(blockers) > MAX_BLOCKERS:
            raise ChartValidationError(
                f"ticket {blocked!r} would end up blocked by "
                f"{len(held) + len(blockers)} tickets, and GitHub allows at most "
                f"{MAX_BLOCKERS} dependencies per relationship type")
        entry = by_key.get(blocked)
        if entry is None:                      # not re-listed in tickets[]
            entry = {"path": blocked, "action": "skip (exists)", "detail": None}
            entries.append(entry)
            by_key[blocked] = entry
        entry["action"] = "merge"
        entry["detail"] = "unions blockedBy: " + ", ".join(blockers)

    # The blocker end of every edge the run will write. An edge is drawn at
    # both of its ends (ADR 0064), so the blocker's issue is patched too by the
    # real run's post-write graph-region pass -- and nothing the run writes may
    # be missing from the plan. A blocker that is itself being created or
    # overwritten already carries the edge in its own line, so it is skipped
    # here (mirrors local_map_ops.py's `blocker_gains`).
    blocker_gains = {}
    for t in inp["tickets"]:
        for blocked in t.get("blocks") or []:
            if action_by_key.get(t["key"]) in ("create", "OVERWRITE"):
                continue
            # `t["key"]` is not create/OVERWRITE, so by construction of the
            # tickets loop above it is "skip (exists)" -- an existing ticket.
            if action_by_key.get(blocked) not in ("create", "OVERWRITE"):
                # `blocked` already exists too -- tell an already-unioned edge
                # (no write) from a new one, by issue IDENTITY, the same rule
                # the `pending` pass above uses.
                if snap is not None and blocked in snap.tickets \
                        and t["key"] in snap.tickets and snap.holds_edge(blocked, t["key"]):
                    continue                   # genuinely no change
            # else: `blocked` is being freshly created/overwritten in this same
            # input, so it cannot already hold the edge -- the blocker still
            # gains a child once pass 2 wires it.
            gained = blocker_gains.setdefault(t["key"], [])
            if blocked not in gained:
                gained.append(blocked)
    for blocker_key, gained in blocker_gains.items():
        # `blocker_gains` is keyed by a ticket of THIS input, and the tickets
        # loop above gave every one of those an entry -- so the lookup always
        # hits, and the entry is "skip (exists)" (create/OVERWRITE `continue`d
        # above) or the "merge" the `pending` pass just set.
        entry = by_key[blocker_key]
        entry["action"] = "merge"
        detail = "renders as a child in the graph: " + ", ".join(sorted(gained))
        entry["detail"] = (entry["detail"] + "; " + detail) if entry["detail"] else detail

    # The OTHER end of every edge --force DELETES. An OVERWRITE'd ticket's own
    # blockedBy is reset by `remove_blocked_by`, but the matching child line
    # lives in the blocker's diagram, and every pass above is driven by edges
    # this input ADDS -- so a blocker not re-listed in tickets[] was left
    # drawing an edge that no longer exists, silently and for ever. Announced
    # here, and re-rendered by chart()'s post-write graph pass (mirrors
    # local_map_ops.py's `force_orphans`).
    force_orphans = force_orphaned_blockers(
        action_by_key,
        lambda k: [b for b in snap.blockers_of(k)[0] if b in snap.tickets],
        rewired_edges(inp["tickets"]))
    for blocker_key, lost in force_orphans.items():
        entry = by_key.get(blocker_key)
        if entry is None:                      # not re-listed in tickets[]
            entry = {"path": blocker_key, "action": "skip (exists)", "detail": None}
            entries.append(entry)
            by_key[blocker_key] = entry
        entry["action"] = "merge"
        detail = force_orphan_detail(lost)
        entry["detail"] = (entry["detail"] + "; " + detail) if entry["detail"] else detail

    # Edges whose blocked ticket is being written fresh are wired unconditionally
    # (nothing survives on it to check against) and are NOT announced separately:
    # they are part of that ticket's own `create` / `OVERWRITE` line.
    fresh = {}
    for t in inp["tickets"]:
        for blocked in t.get("blocks") or []:
            if action_by_key.get(blocked) in ("create", "OVERWRITE"):
                fresh.setdefault(blocked, [])
                if t["key"] not in fresh[blocked]:
                    fresh[blocked].append(t["key"])
    return entries, map_body, div, {**fresh, **pending}, force_orphans


def chart(ops, inp, real, force=False):
    # VALIDATE BEFORE DEREFERENCING ANYTHING. Reading inp["target"]["slug"]
    # first meant a map_input.json missing "target" raised a bare KeyError --
    # exit 1 with a traceback, where the local backend exits 2 with one stderr
    # line for the same file. The flow skills key on that difference, and a
    # traceback is not an actionable message.
    #
    # The snapshot is fetched lazily instead, because the `blocks` check needs
    # it (an edge may name a ticket already on the map) but validate_chart_input
    # calls ticket_exists only after it has confirmed target.slug is a safe key.
    fetched = {}

    def ticket_exists(key):
        if "snap" not in fetched:
            fetched["snap"] = ops.try_snapshot(inp["target"]["slug"])
        s = fetched["snap"]
        return s is not None and key in s.tickets

    validate_chart_input(inp, ticket_exists=ticket_exists)
    slug = inp["target"]["slug"]
    snap = fetched["snap"] if "snap" in fetched else ops.try_snapshot(slug)
    entries, map_body, div, edges, force_orphans = chart_plan(ops, snap, inp, force)
    actions = {e["path"]: e["action"] for e in entries}

    if not real:
        # stdout carries JSON or nothing, so `chart --input x | jq` works; the
        # human rendering of the same plan goes to stderr.
        print("DRY RUN — planned changes:", file=sys.stderr)
        for e in entries:
            detail = f"  [{e['detail']}]" if e["detail"] else ""
            print(f"  {e['action']} {e['path']}{detail}", file=sys.stderr)
        for d in div:
            print(f"  divergence: {d}", file=sys.stderr)
        return {"backend": "github", "dryRun": True,
                "planned": entries, "divergence": div}

    for e in entries:
        if e["path"].startswith("label:") and e["action"] == "create":
            ops.ensure_label(e["path"][len("label:"):])

    map_action = actions["<map>"]
    if map_action == "create":
        _assert_map_body(map_body, "the map issue body")
        created = ops.create_issue(inp["map"]["title"], map_body, {MAP_LABEL})
        map_number = created["number"]
        snap_tickets = {}
    else:
        map_number = snap.map["number"]
        snap_tickets = dict(snap.tickets)
        payload = {}
        if map_action in ("merge", "OVERWRITE"):
            if map_body is not None:
                _assert_map_body(map_body, f"the map issue body (#{map_number})")
                payload["body"] = map_body
            if map_action == "OVERWRITE":
                # --force regenerates the WHOLE map document from the input, and
                # on a tracker the name is a field rather than a line of the
                # body. Patching only the body left a --force claiming to have
                # rewritten everything while the title stayed stale -- and the
                # very next additive run would then report that title as a
                # divergence the user had already asked to apply.
                payload["title"] = one_line(inp["map"]["title"])
            if not snap.is_map:
                payload["labels"] = sorted(set(_label_names(snap.map)) | {MAP_LABEL})
            ops.patch_issue(map_number, payload)

    # pass 1: create the tickets that do not exist, and parent each one.
    # pass 2: wire the edges. Create-then-wire, so an edge can always name a
    # ticket this same run created.
    db_id = {k: v["databaseId"] for k, v in snap_tickets.items()}
    number = {k: v["number"] for k, v in snap_tickets.items()}
    repo_of = {k: _repo_of(v, ops.repo) for k, v in snap_tickets.items()}
    for t in inp["tickets"]:
        key = t["key"]
        if actions[key] not in ("create", "OVERWRITE"):
            continue
        body = render_ticket_issue_body(key, t["question"])
        _assert_ticket_body(body, f"ticket {key!r}")
        labels = {TICKET_LABEL, TYPE_LABEL % t["type"]}
        if actions[key] == "create":
            made = ops.create_issue(t["title"], body, labels)
            db_id[key], number[key] = made["id"], made["number"]
            repo_of[key] = ops.repo
            try:
                ops.link_child(map_number, made["id"])
            except GitHubError as e:
                # A create that lands and a link that does not leaves an issue
                # carrying this key but parented nowhere. The join only sees
                # children, so a plain re-run would create a SECOND issue with
                # the same key marker -- and the run after THAT fails the
                # duplicate-key check on a map nobody can chart again. Refuse
                # loudly with the number, rather than leave that trap set.
                raise GitHubError(
                    f"created issue #{made['number']} for ticket {key!r} but "
                    f"could not attach it to map #{map_number}: {e}. That issue "
                    f"now carries the key marker for {key!r} while belonging to "
                    "no map. Attach it by hand (Sub-issues -> Add existing) or "
                    "delete it BEFORE re-running -- a re-run would otherwise "
                    "create a second issue with the same key") from e
        else:
            # --force rewrites the item: body back to the rendered form (which
            # drops the gist), state back to open, assignee cleared. The edges
            # it held are discarded here and re-wired from this input below.
            n, r = number[key], repo_of[key]
            ops.patch_issue(n, {"title": one_line(t["title"]), "body": body,
                                "state": "open", "assignees": [],
                                "labels": sorted(labels)}, repo=r)
            # Remove edges by TARGET IDENTITY, not by key. Looking the blocker up
            # by key in snap.tickets found nothing for a re-parented target, so
            # the removal silently no-op'd and an OVERWRITE ticket stayed blocked
            # by an item that can never close -- permanently quarantined, while
            # the plan had promised its blockedBy was reset.
            for _ref, blocker_db in snap.blocker_refs_of(key):
                ops.remove_blocked_by(n, blocker_db, repo=r)

    # Only the edges the plan announced. Letting the API's 422 make this
    # idempotent instead would mean one wasted write per existing edge on every
    # re-chart, and each one shows in the issue's timeline -- the contract's rule
    # that an existing edge is not written is about not littering a shared
    # tracker with no-op events, not only about avoiding the error.
    for blocked in sorted(edges):
        for blocker in edges[blocked]:
            _ensure_edge(ops, number[blocked], db_id[blocker],
                         repo=repo_of.get(blocked))

    for d in div:
        print(f"chart: divergence: {d}", file=sys.stderr)

    # The closing snapshot below is chart()'s own return value (see the cost
    # table's "1 closing GraphQL") -- fetched AFTER every create, link and edge
    # write above, so it already carries every ticket this run created and
    # every edge this run wired. Reuse it, rather than add a read, to render
    # the position diagram of every ticket an edge touched this run, BOTH ends
    # (spec 1, "Both ends"): the blocked ticket gains a parent node, the
    # blocker gains a child node.
    final_snap = ops.snapshot(str(map_number))
    touched = set(edges) | {blocker for blockers in edges.values() for blocker in blockers}
    # --force disturbs two more diagrams, and neither shows up in `edges`
    # because `edges` is what this input ADDS:
    #
    #   - the OVERWRITE'd ticket itself, whose body was reset to an EMPTY
    #     diagram above. Its parents are gone for real, but its CHILDREN are
    #     edges held on other tickets, which survive -- so without this the
    #     edge stays live in the model and absent from the picture, for ever
    #     (a later chart skips the ticket as "no change", and `block`
    #     correctly refuses to rewrite an edge that already exists);
    #   - a blocker that LOST a child to `remove_blocked_by`, which is the
    #     mirror harm: an edge in the picture and gone from the model.
    #
    # Both are what ADR 0064's both-ends rule requires, and the plan announced
    # each of them. `k in final_snap.tickets` guards the ticket that is being
    # rewritten in another repo's issue the closing snapshot cannot see.
    touched |= {k for k, a in actions.items()
                if a == "OVERWRITE" and k in final_snap.tickets}
    touched |= {k for k in force_orphans if k in final_snap.tickets}
    for key in sorted(touched):
        parents, _missing = final_snap.blockers_of(key)
        _patch_graph_region(ops, final_snap, key, parents,
                            _children_of(final_snap, key))

    out = final_snap.map_json()
    out["divergence"] = div
    return out


def _ensure_edge(ops, blocked_number, blocker_db_id, repo=None):
    """Add the edge unless it is already there.

    GitHub answers **422 "Target issue has already been taken"** on a duplicate,
    verified live, so this cannot be left to the API to make idempotent. The
    callers all check the join first; this catch is the concurrent-run backstop,
    not the primary path. Beyond avoiding the error, a redundant write is visible
    in the issue's timeline -- the contract's rule that `block` must not write
    when the edge exists is about not littering a shared tracker with no-op
    events.
    """
    try:
        ops.add_blocked_by(blocked_number, blocker_db_id, repo=repo)
    except GitHubError as e:
        if "already been taken" in str(e):
            return
        raise


def _children_of(snap, key):
    """Every OTHER ticket in `snap` whose blockers include `key`.

    A ticket's parents are its own blockedBy edges; its children are only
    discoverable by scanning every other ticket in the snapshot -- the same
    price the local backend pays with a directory scan (`map_core.
    position_diagram_region` renders both ends, but only the caller can supply
    which tickets a key unblocks).
    """
    return sorted(k for k in snap.keys
                  if k != key and key in snap.blockers_of(k)[0])


def _patch_graph_region(ops, snap, key, parents, children):
    """Re-render `key`'s position diagram from `parents`/`children` and write
    it back if that changes the stored body.

    Takes the lists rather than deriving them itself, because the two callers
    source them differently. `chart` reads them straight off a snapshot taken
    AFTER every edge this run was wired, so that snapshot already reflects
    them in full. `block` wires exactly one edge and knows both ends' new
    parent/child by hand -- cheaper than a second snapshot fetch mid-call, and
    the same in-memory-augmentation trick the local backend uses for the
    ticket whose file it just wrote (spec 1, "Both ends"; both `parents` and
    `children` are de-duplicated and sorted by `position_diagram_region`
    itself, so a caller may pass either list with an extra entry unsorted).
    """
    body = norm_eol(snap.tickets[key].get("body"))
    new_body = set_graph_region(body, position_diagram_region(key, parents, children))
    if new_body == body:
        return
    _assert_ticket_body(new_body, f"ticket {key!r} (#{snap.number_of(key)})")
    ops.patch_issue(snap.number_of(key), {"body": new_body}, repo=snap.repo_of(key))


# ---------------------------------------------------------------------------
# the other subcommands
# ---------------------------------------------------------------------------
def lint(ops, ref):
    """Check the map as it stands, from the one snapshot the other read-only
    subcommands already take. Writes nothing.

    Two rules behave differently here than on local markdown, and both are
    declared rather than quietly dropped:

    - `resolution-without-diagram` is NOT checked. The resolution body is a
      native comment; the snapshot holds only the machine-readable gist region,
      and walking every ticket's comments would cost one API call per ticket on
      the command whose whole value is being cheap enough to run after every
      session. It comes back under `notChecked`.
    - `closed-without-resolution` keys on the gist region instead -- the half of
      the record `resolve` writes into the ticket body in the same operation.

    `anonymous-claim` cannot fire here at all, and that is correct rather than
    missing: `--user` has no literal default on this backend, it resolves the
    caller through whoami(), so there is no anonymous claim to find.
    """
    snap = ops.snapshot(ref)
    tickets = []
    for key in snap.keys:
        t = snap.ticket_json(key)
        t["resolution"] = None      # lives in a comment; see the docstring
        tickets.append(t)
    findings = lint_findings(norm_eol(snap.map.get("body")), tickets,
                             resolution_bodies=False)
    return {"backend": "github", "map": snap.map_json()["map"]["id"],
            "clean": not findings, "findings": findings,
            "notChecked": list(RULES_NEEDING_RESOLUTION_BODY)}


def read_map(ops, ref):
    return ops.snapshot(ref).map_json()


def frontier(ops, ref):
    """open + unblocked + unclaimed children.

    Reaching the snapshot at all is the existence check: a map that does not
    exist raises, exactly as `read` does, rather than returning three empty
    buckets and exit 0 -- which is byte-identical to a map whose every decision
    is resolved, and is what a flow skill would render to the user (ADR 0061).
    """
    snap = ops.snapshot(ref)
    out = {"frontier": [], "blocked": [], "claimed": []}
    for key in snap.keys:
        t = snap.ticket_json(key)
        if t["status"] != "open":
            continue                       # a closed ticket is done, not actionable
        blockers, missing = snap.blockers_of(key)
        open_blockers = [b for b in blockers
                         if b in snap.tickets
                         and _state(snap.tickets[b].get("state")) == "open"]
        # A blocker with no surviving ticket is not a satisfied blocker.
        open_blockers += [b for b in missing if b not in open_blockers]
        # Bucket precedence is fixed and shared: claimed, then blocked, then
        # frontier. A ticket that is both claimed and blocked appears under
        # claimed ONLY -- both answers mean "not available to pick up", and the
        # blocker list stays visible in map.json's blockedBy.
        # `id` is the issue number in BOTH documents, so `map.json` and
        # `frontier.json` name the same ticket the same way and either value can
        # be handed straight back to `--ticket`. `key` rides alongside because
        # every ticket-to-ticket reference (blockedBy, missingBlockers) is a key.
        base = {"id": t["id"], "key": key, "name": t["name"],
                "milestone": t["milestone"]}
        if t["assignee"]:
            out["claimed"].append({**base, "assignee": t["assignee"]})
        elif open_blockers:
            entry = {**base, "blockedBy": open_blockers}
            if missing:
                entry["missingBlockers"] = missing
            out["blocked"].append(entry)
        else:
            out["frontier"].append({**base, "url": t["url"], "type": t["type"]})
    # The progress the session surface groups by (ADR 0099). Counted over
    # EVERY ticket, closed included -- a milestone's progress is closed/total,
    # and the three buckets above deliberately hold only the open ones.
    out["milestones"] = milestone_progress(
        snap.milestones,
        {k: _state(snap.tickets[k].get("state")) for k in snap.keys})
    return out


def _resolve_ticket(snap, ident, what="ticket id"):
    """-> (key, number, repo) for a `--ticket` value that may be a key or a number.

    Guards the path invariant first: a key becomes a marker payload and reaches
    the join, so `../../evil` must be rejected before anything is looked up. A
    digit string skips that check because it is a number, not a slug.
    """
    if not (isinstance(ident, str) and ident.isdigit()):
        safe_segment(ident, what)
    key = snap.key_for(ident)
    return key, snap.number_of(key), snap.repo_of(key)


def claim(ops, ref, ticket, user):
    """Assign the ticket. An empty --user releases the claim.

    `user` must already be a real login: the local backend's literal "me" is
    meaningless as a GitHub assignee, so main() resolves the caller through
    `gh api user` instead of writing it.
    """
    snap = ops.snapshot(ref)
    key, number, repo = _resolve_ticket(snap, ticket)
    ops.patch_issue(number, {"assignees": [user] if user else []}, repo=repo)
    return {"claimed": key, "assignee": user or None}


def block(ops, ref, ticket, blocked_by):
    snap = ops.snapshot(ref)
    key, number, repo = _resolve_ticket(snap, ticket)
    if not (isinstance(blocked_by, str) and blocked_by.isdigit()):
        safe_segment(blocked_by, "blocked-by ticket id")
    # The target must be a ticket of THIS map -- decision-map models dependencies
    # within one map, and a phantom target would block the ticket for ever with
    # nothing able to close it.
    blocker_key = snap.key_for(blocked_by)
    if blocker_key == key:
        raise InvalidEdgeError(
            f"ticket {key!r} cannot block itself: the edge would leave it "
            "waiting on a ticket that can never close, so it would never "
            "appear on the frontier again")
    held, _missing = snap.blockers_of(key)
    # Identity, not key membership: a stale edge to a re-parented ticket that
    # once held this key used to read as "the edge exists", so the real edge was
    # never written and `block` still reported success.
    if snap.holds_edge(key, blocker_key):
        return {"ticket": key, "blockedBy": held}
    if len(snap.blocker_refs_of(key)) >= MAX_BLOCKERS:
        raise ChartValidationError(
            f"ticket {key!r} already has {MAX_BLOCKERS} blockers and GitHub "
            f"allows at most {MAX_BLOCKERS} per relationship type")
    _ensure_edge(ops, number, snap.tickets[blocker_key]["databaseId"], repo=repo)
    new_held = held + [blocker_key]
    # Both ends (spec 1): `key` gains a parent, `blocker_key` gains a child.
    # `snap` predates this write, so the new edge is folded in by hand on
    # whichever side it changes rather than re-read -- the everything-else
    # about each ticket (its own other parents, its own other children) is
    # unaffected by this one edge and comes straight off `snap`.
    _patch_graph_region(ops, snap, key, new_held, _children_of(snap, key))
    blocker_parents, _blocker_missing = snap.blockers_of(blocker_key)
    _patch_graph_region(ops, snap, blocker_key, blocker_parents,
                        _children_of(snap, blocker_key) + [key])
    return {"ticket": key, "blockedBy": new_held}


def comment(ops, ref, ticket, body_text):
    snap = ops.snapshot(ref)
    key, number, repo = _resolve_ticket(snap, ticket)
    ops.add_comment(number, scrub(body_text), repo=repo)
    return {"commented": key}


def _resolution_comment(gist, link, body):
    detail = f"\nDetail: {scrub(link)}\n" if link else ""
    extra = f"\n{scrub(body)}\n" if body else ""
    return f"## Resolution\n\n{scrub(gist)}\n{detail}{extra}"


def resolve(ops, ref, ticket, gist, link, body):
    """Record the resolution and close the ticket.

    The gist is written TWICE, deliberately and in the same operation. The
    native comment is the record a human reads; the `decision-map:gist` region
    on the ticket body is the field a machine reads, and it exists so
    `map.json` can report every gist from the one snapshot instead of walking
    every ticket's comments at one API call each. They are written together and
    must never be allowed to diverge.
    """
    snap = ops.snapshot(ref)
    key, number, repo = _resolve_ticket(snap, ticket)
    stored_gist = one_line(gist)
    if len(stored_gist) > GIST_MAX:
        print(GIST_TOO_LONG.format(n=len(stored_gist), max=GIST_MAX), file=sys.stderr)

    # Warn BEFORE the first write, so the message cannot read as a failure of it.
    # _type_of falls back to `grilling` for a ticket whose label a human removed,
    # which is the conservative direction here: it asks for the artifact rather
    # than quietly exempting the ticket.
    ttype = _type_of(_label_names(snap.tickets[key]), f"ticket {key!r} (#{number})")
    warning = None
    if ttype in TYPES_EXPECTING_A_DOC and not (link or "").strip():
        warning = MISSING_DOC_LINK.format(type=ttype)
        print(warning, file=sys.stderr)

    ops.add_comment(number, _resolution_comment(gist, link, body), repo=repo)

    tbody = norm_eol(snap.tickets[key].get("body"))
    region = f"{GIST_START}\n{stored_gist}\n{GIST_END}\n"
    if _GIST_BLOCK_RE.search(tbody):
        tbody = _GIST_BLOCK_RE.sub(lambda _m: region, tbody, count=1)
    else:
        # A ticket created before the gist region existed, or one a human
        # stripped it from. Append a fresh region rather than guess where the
        # old one was -- guessing at the boundary of content the tool did not
        # write is the pattern that cost three review rounds.
        tbody = tbody.rstrip("\n") + "\n\n" + region
    _assert_ticket_body(tbody, f"ticket {key!r} (#{number})")
    # Body and state in ONE call: two calls would leave a window where the
    # ticket is closed with no gist, which `read` would report as a resolved
    # decision with no answer.
    ops.patch_issue(number, {"body": tbody, "state": "closed"}, repo=repo)

    _reindex_decisions(ops, snap, key, stored_gist)
    # On the RESULT as well as stderr -- the agent closing a ticket reads stdout.
    out = {"resolved": key, "gist": stored_gist or None}
    if warning:
        out["warning"] = warning
    return out


def _reindex_decisions(ops, snap, just_closed, just_gist):
    """Rebuild the map body's "Decisions so far" index from the snapshot.

    A projection, not accumulated state, so it is regenerated wholesale inside
    its own region and ordered by ticket key. The snapshot predates the close
    that triggered this, so the ticket being resolved is folded in explicitly --
    cheaper and more consistent than re-reading the whole map to observe a
    change this process just made.

    The milestones that group it come from `snap.milestones`, not a fresh
    parse: `resolve` never edits the milestones region, so the snapshot's copy
    cannot be stale for this purpose, and re-parsing the same body the
    snapshot already read would be the duplication `Snapshot.__init__` exists
    to avoid.
    """
    entries = []
    for key in snap.keys:
        t = snap.tickets[key]
        if key == just_closed:
            closed, gist = True, just_gist
        else:
            closed, gist = _state(t.get("state")) == "closed", (snap.gist_of(key) or "")
        if not closed:
            continue
        # The ticket's FULL URL, not `#N`. Inside an issue body `[title](#2)` is
        # a same-page fragment link that goes nowhere -- every entry in the index
        # a human is meant to click was dead. (A bare `#2` would auto-link, but
        # the shared index format is `- [title](link) — gist`, so the link has to
        # be a real one.) The url also survives being copied out of the tracker.
        entries.append((key, one_line(t.get("title") or key),
                        t.get("url") or f"#{t['number']}", gist))
    body = norm_eol(snap.map.get("body"))
    region = decisions_region(entries, snap.milestones)
    if _DECISIONS_BLOCK_RE.search(body):
        body = _DECISIONS_BLOCK_RE.sub(lambda _m: region, body, count=1)
    else:
        heading = "## Decisions so far\n"
        if heading in body:
            body = body.replace(heading, heading + "\n" + region, 1)
        else:
            body = body.rstrip("\n") + f"\n\n## Decisions so far\n\n{region}"
    _assert_map_body(body, f"the map issue body (#{snap.map['number']})")
    ops.patch_issue(snap.map["number"], {"body": body})


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------
EXIT_OK = 0
EXIT_ERROR = 2
# lint only: the map was read successfully AND it has findings. Distinct
# from EXIT_ERROR (the call itself was wrong) and from Python's own 1,
# which an unexpected crash already uses.
EXIT_FINDINGS = 3

_REMEDY = {
    CliUsageError: "run with --help to see the arguments this subcommand needs",
    ChartValidationError: "correct the field named above in map_input.json and re-run",
    UnsafeIdentifierError: "pass the id exactly as chart created it",
    InvalidEdgeError: "a ticket cannot block itself; name a different --blocked-by",
    MarkerIntegrityError: "remove the stray decision-map marker comments from that issue body by hand",
    JoinIntegrityError: "repair the named issue by hand; this refuses to guess rather than risk re-creating tickets",
    MapAbsentError: "chart the map first, or pass its issue number as --map",
    MapNotFoundError: "check --map and --repo, and that `gh auth status` shows a token with repo access",
    GitHubError: "check `gh auth status` and that the token can write issues in that repo",
    json.JSONDecodeError: "make sure --input points at valid JSON",
}
_DEFAULT_REMEDY = "check the arguments and your gh authentication, then re-run"

_REQUIRED_CLI_ARGS = {
    "chart": [("input", "--input")],
    "read": [("ref", "--map")],
    "frontier": [("ref", "--map")],
    "claim": [("ref", "--map"), ("ticket", "--ticket")],
    "resolve": [("ref", "--map"), ("ticket", "--ticket"), ("gist", "--gist")],
    "comment": [("ref", "--map"), ("ticket", "--ticket"), ("body_file", "--body-file")],
    "block": [("ref", "--map"), ("ticket", "--ticket"), ("blocked_by", "--blocked-by")],
    "lint": [("ref", "--map")],
}


def _check_cli_args(a):
    missing = [flag for attr, flag in _REQUIRED_CLI_ARGS[a.cmd]
               if getattr(a, attr, None) is None]
    if missing:
        raise CliUsageError(f"{a.cmd} needs {' and '.join(missing)}")


def _dispatch(a, api=None):
    _check_cli_args(a)
    api = api or GhApi()
    body = None
    if a.body_file:
        with open(a.body_file, encoding="utf-8") as fh:
            body = fh.read()

    if a.cmd == "chart":
        with open(a.input, encoding="utf-8") as fh:
            inp = json.load(fh)
        target = inp.get("target") or {}
        repo = a.repo or (f"{target.get('owner')}/{target.get('repo')}"
                          if target.get("owner") and target.get("repo") else None)
        ops = GitHubOps(api, repo)
        return chart(ops, inp, real=a.real and not a.dry, force=a.force)

    ops = GitHubOps(api, a.repo)
    if a.cmd == "read":
        return read_map(ops, a.ref)
    if a.cmd == "frontier":
        return frontier(ops, a.ref)
    if a.cmd == "lint":
        return lint(ops, a.ref)
    if a.dry:
        # Parity with the local backend, which is the point: --dry-run on a
        # mutating ticket subcommand prints a stub and performs NO lookups. A
        # dry run that quietly resolves the map is not free, and one that
        # renders a plan where local renders a stub is not at parity.
        #
        # It must still validate every identifier the real run would, or it
        # reports success for a call that exits 2 for real -- an inert but
        # untruthful dry run. --map was being skipped here, so
        # `claim --map ../../evil --ticket t --dry-run` exited 0 where the local
        # backend exits 2 for the identical arguments.
        if not (isinstance(a.ref, str) and a.ref.isdigit()):
            safe_segment(a.ref, "map slug")
        if not (isinstance(a.ticket, str) and a.ticket.isdigit()):
            safe_segment(a.ticket, "ticket id")
        if a.cmd == "block" and not (isinstance(a.blocked_by, str)
                                     and a.blocked_by.isdigit()):
            safe_segment(a.blocked_by, "blocked-by ticket id")
        return {"dryRun": True, "wouldRun": a.cmd, "ticket": a.ticket}
    if a.cmd == "claim":
        # --user has no default identity. The local backend's literal "me" is
        # meaningless as a GitHub assignee, so resolve the caller for real; an
        # explicitly empty --user still releases the claim.
        user = a.user if a.user is not None else api.whoami()
        return claim(ops, a.ref, a.ticket, user)
    if a.cmd == "resolve":
        return resolve(ops, a.ref, a.ticket, a.gist, a.link, body)
    if a.cmd == "comment":
        return comment(ops, a.ref, a.ticket, body)
    return block(ops, a.ref, a.ticket, a.blocked_by)


def build_parser():
    ap = argparse.ArgumentParser(
        description="decision-map GitHub Issues backend")
    ap.add_argument("cmd", choices=["chart", "read", "frontier", "claim",
                                    "resolve", "comment", "block", "lint"])
    ap.add_argument("--repo", help="OWNER/REPO. Required except on chart, where "
                                   "map_input.json's target.owner/target.repo "
                                   "may supply it")
    ap.add_argument("--input"); ap.add_argument("--output")
    ap.add_argument("--map", dest="ref",
                    help="the map's issue number, or its slug (resolved through "
                         "the decision-map:map label)")
    ap.add_argument("--ticket")
    ap.add_argument("--user", default=None,
                    help="assignee login. Omitted resolves the authenticated "
                         "user; an empty value releases the claim")
    ap.add_argument("--gist"); ap.add_argument("--link")
    ap.add_argument("--body-file", dest="body_file")
    ap.add_argument("--blocked-by", dest="blocked_by")
    ap.add_argument("--real", action="store_true")
    ap.add_argument("--dry-run", dest="dry", action="store_true")
    ap.add_argument("--force", action="store_true",
                    help="chart only: full rewrite of every item the input "
                         "names. DESTRUCTIVE -- discards their recorded "
                         "resolutions, claims and blocking edges. Not needed to "
                         "add tickets: chart is additive by default")
    return ap


def main(argv=None, api=None):
    a = build_parser().parse_args(argv)
    try:
        result = _dispatch(a, api=api)
    except (CliUsageError, ChartValidationError, InvalidEdgeError,
            UnsafeIdentifierError, MarkerIntegrityError, JoinIntegrityError,
            MapNotFoundError, GitHubError, json.JSONDecodeError, OSError) as e:
        remedy = next((r for cls, r in _REMEDY.items() if isinstance(e, cls)),
                      _DEFAULT_REMEDY)
        problem = " ".join(str(e).split())
        if problem.startswith(f"{a.cmd}: "):
            problem = problem[len(a.cmd) + 2:]
        # stdout stays empty so a caller piping this never sees half a document
        print(f"error: {a.cmd}: {problem} -- {remedy}", file=sys.stderr)
        return EXIT_ERROR
    text = json.dumps(result, indent=2)
    if a.output:
        with open(a.output, "w", encoding="utf-8") as fh:
            fh.write(text)
    print(text)
    if a.cmd == "lint" and not result["clean"]:
        return EXIT_FINDINGS
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
