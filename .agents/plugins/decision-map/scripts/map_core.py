#!/usr/bin/env python3
r"""map_core.py — the parts of every decision-map backend that must not diverge.

Extracted from `local_map_ops.py` when the GitHub backend landed (ADR 0062).
Nothing here touches a filesystem or a network: it is the marker invariant, the
region merge, the input validation and the failure vocabulary — the pieces where
two backends disagreeing is a correctness bug rather than a difference of
storage.

Why extract rather than copy. The marker invariant below cost five review
rounds to get right, and every one of those rounds fixed a defect that passed
the tests of the round before it. A second copy of that logic is a second copy
of every one of those bugs waiting to be reintroduced by a well-meaning edit to
one file. The contract says "every backend must …" in a dozen places; this
module is where "every backend" is a single object rather than a promise.

THE MARKER INVARIANT — the one rule the whole design rests on:

    Every decision-map marker in a generated document was written by the tool,
    because every user-supplied string is escaped on the way in.

Enforced, not asserted. `scrub()` escapes the marker prefix in every
user-supplied string before it reaches storage, and every write goes through
`assert_regions()`, which refuses to write a document that does not hold
exactly zero or one well-formed marker region. That is what lets `resolve()`
find the block it owns by searching for its own markers: the search can no
longer match user text, because user text can no longer contain a marker.

Escaping is ORDER-SENSITIVE: any transformation that runs after the escape can
put a marker back together. `one_line()` therefore flattens line breaks BEFORE
escaping, never after, and flattens using the reader's own `str.splitlines()`
so the writer and the parser cannot disagree about what a line break is.

`local_map_ops` imports these under its own historical `_name` spellings so its
suite keeps testing the same symbols; `github_map_ops` uses the names as
written here.
"""
import re

AFK_TYPES = {"research"}
VALID_TICKET_TYPES = {"research", "prototype", "grilling", "task"}

# Ticket keys AND a map's own target.slug become path segments on the local
# backend (<root>/<slug>/... , tickets/<key>.md) and marker payloads on a
# tracker -- restrict both to a safe slug so neither can escape the intended
# root (e.g. "../../pwned", "C:/Windows/Temp/pwned") nor break an HTML comment.
# Anchored with \Z, not $: in Python, `$` also matches just before a trailing
# newline, so "okname\n" would otherwise pass here and only fail later.
SAFE_SLUG_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]*\Z")

# ---------------------------------------------------------------------------
# Generated-region markers, and the invariant that makes them trustworthy.
#
# Four review rounds established that finding a region by pattern-matching the
# document is the wrong half of the problem to work on:
#   round 1  append-only            -> duplicate blocks stacked
#   round 2  "## Resolution.*\Z"    -> deleted a user comment
#   round 2b lookahead to "\n## "   -> a --body-file's own "## Rationale"
#                                      orphaned the tail (unbounded growth)
#   round 3  these markers          -> same mechanism, longer needle: a marker
#                                      pasted into question/gist/link/body/
#                                      comment reproduced BOTH harms
# The needle was never the problem. What was missing is the write side:
# scrub() escapes the marker prefix in every user-supplied string, so a marker
# in a document is proof the tool put it there. assert_regions() then checks
# that promise on every single write. Widening or re-anchoring the search
# pattern is NOT a fix -- it is round 5.
MARKER_PREFIX = "<!-- decision-map:"
# "&lt;" is Markdown/HTML's own escape for a literal "<", so the scrubbed text
# still RENDERS as the marker the user typed -- it just stops being an HTML
# comment, and stops matching. Escaping, not mangling. Idempotent: the escaped
# form no longer contains MARKER_PREFIX, so re-scrubbing is a no-op and
# repeated read/modify/write cycles cannot cascade.
MARKER_ESCAPED_PREFIX = "&lt;!-- decision-map:"

RESOLUTION_START = "<!-- decision-map:resolution:start -->"
RESOLUTION_END = "<!-- decision-map:resolution:end -->"
DECISIONS_START = "<!-- decision-map:decisions:start -->"
DECISIONS_END = "<!-- decision-map:decisions:end -->"
FOG_START = "<!-- decision-map:fog:start -->"
FOG_END = "<!-- decision-map:fog:end -->"
SCOPE_START = "<!-- decision-map:scope:start -->"
SCOPE_END = "<!-- decision-map:scope:end -->"

# Single-line markers -- a value carrier, not a paired region. `assert_regions`
# counts them separately for that reason: they have no end marker to pair with.
KEY_MARKER = "<!-- decision-map:key:%s -->"
KEY_MARKER_RE = re.compile(r"<!-- decision-map:key:([^\s>]*) -->")
GIST_START = "<!-- decision-map:gist:start -->"
GIST_END = "<!-- decision-map:gist:end -->"
GRAPH_START = "<!-- decision-map:graph:start -->"
GRAPH_END = "<!-- decision-map:graph:end -->"

MAP_REGIONS = ((DECISIONS_START, DECISIONS_END),
               (FOG_START, FOG_END),
               (SCOPE_START, SCOPE_END))
TICKET_REGIONS = ((RESOLUTION_START, RESOLUTION_END),
                  (GRAPH_START, GRAPH_END))
# A tracker records the resolution as a native comment, so the resolution
# markers are local-only; the gist region is the tracker's machine-readable
# home for the same one-liner the local backend keeps in frontmatter. The
# graph region is shared -- a position diagram is as useful on an issue as
# in a file, and rendering it in one place is what stops the two backends
# drifting the way _MAP_DIAGRAM already has.
TRACKER_TICKET_REGIONS = ((GIST_START, GIST_END),
                          (GRAPH_START, GRAPH_END))

# The placeholder the tool writes into an empty list region. It is tool-owned,
# not user content, so the merge drops it as soon as a real line arrives.
EMPTY_LIST_LINE = "- (none)"

# Never recommend --force without saying what it costs. A reviewer followed the
# old advice -- "re-chart with --force to rewrite it" -- and watched it erase a
# resolution, its frontmatter and its index entry. Any text that steers a user
# toward --force must carry this.
FORCE_COST = ("--force fully rewrites every item named in the input and "
              "DISCARDS their recorded resolutions, claims and blocking edges")

# The longest gist that still reads as ONE line in the map's decisions index,
# which is what the index renders it as. Over this, resolve() warns and writes
# anyway (ADR 0066): failing the call would discard a resolved decision to
# enforce a formatting rule.
GIST_MAX = 200

# One wording, printed by both backends. A user who moves a map from local
# to GitHub must not get a different explanation of the same problem.
GIST_TOO_LONG = (
    "warning: this gist is {n} characters. The map's 'Decisions so far' "
    "index renders it as ONE line, so anything past ~{max} makes the index "
    "unreadable. Recording it anyway -- consider re-resolving with a "
    "one-sentence gist and moving the detail into --body-file or --link.")

# How far a map's gists may collectively run past GIST_MAX before the corpus
# itself is worth a finding (ADR 0068). Expressed in whole gists rather than a
# round number so it tracks GIST_MAX: five gists' worth of overage is noise on
# a map, and a map past that is re-reading the overage every single session.
GIST_BUDGET_SLACK = GIST_MAX * 5


class ChartValidationError(ValueError):
    """map_input.json failed validation. Raised before anything is written."""


class UnsafeIdentifierError(ValueError):
    """A value used as a path segment or a marker payload (a map slug, a ticket
    id) is not a safe slug. Raised BEFORE the path or marker is built.

    chart() has slug-validated the keys it creates since round 1, but the
    identifiers the other subcommands accept at runtime were unchecked, so
    `claim --ticket ../../../VICTIM` rewrote an arbitrary .md file outside
    --root at exit code 0."""


class InvalidEdgeError(ValueError):
    """A blocking edge is unusable: it names its own ticket. Raised before
    anything is written. A self-edge permanently quarantines the ticket --
    frontier() reports it blocked by something that can never close, so it is
    never takeable and never resolvable."""


class CliUsageError(ValueError):
    """A subcommand was invoked without an argument it needs. Raised before any
    work, and reported as a one-line message rather than a traceback."""


class MarkerIntegrityError(Exception):
    """A document about to be written does not hold exactly zero or one
    well-formed generated region. Raised INSTEAD of writing, so the tool
    refuses rather than corrupts. Every string written is scrubbed, so reaching
    this means either a bug (a new user-input path that forgot scrub) or a
    hand-edited document with stray markers."""


class JoinIntegrityError(Exception):
    """The key -> item join could not be built unambiguously from what the
    tracker returned: a labelled child with no key marker, two key markers in
    one body, or two children resolving to the same key.

    Every one of these is a LOUD failure rather than a skip, and the reason is
    the worst failure this design can produce: ignoring an unresolvable child
    hides it from the join, so `chart` labels it `create`, and a map whose
    markers were stripped by a bulk edit is silently re-created in full and
    presented to the user as a page of ordinary, approvable `create` lines."""


def norm_eol(s):
    """Normalise line endings to \\n. Run this on EVERY read of stored text.

    A tracker does not normalise line endings and does not promise which form
    it returns -- it echoes back however the text was submitted. Verified live
    on GitHub: `cli/cli#14021` comes back CRLF and `cli/cli#14031` LF, same
    repo, both with HTML comments intact. Without this, a human's web-UI edit
    flips a whole region to CRLF and the next `chart` sees every line as
    changed -- breaking the byte-identical no-op guarantee and emitting
    divergences for text nobody touched.

    `None` becomes "": a tracker returns a null body for an item created with
    no description (verified on two live GitHub issues), and every read must
    treat that as "no markers found" rather than letting a regex throw.
    """
    if s is None:
        return ""
    return s.replace("\r\n", "\n").replace("\r", "\n")


def scrub(value):
    """Escape every decision-map marker in a user-supplied string.

    The write-side half of the marker invariant, and the reason the region
    search is safe: after this, no user-supplied byte in any generated document
    can be read back as a marker. Apply it to EVERY string that originates
    outside the tool -- question, title, destination, notes, fog/out-of-scope
    lines, comment bodies, gist, link, resolution body, assignee, blocked-by.
    `None` becomes "".
    """
    s = "" if value is None else str(value)
    return s.replace(MARKER_PREFIX, MARKER_ESCAPED_PREFIX)


def one_line(v):
    """Flatten to a single line, THEN escape. Both halves are load-bearing.

    1. Flatten FIRST, escape SECOND. Escaping first is a hole:
       "<!--\\ndecision-map:resolution:start -->" contains no marker for
       scrub() to find, and flattening it afterwards RECONSTITUTES a live
       marker that no escape ever saw. Unpaired that raised
       MarkerIntegrityError mid-chart with the map already written; paired it
       sailed through assert_regions and wrote live markers into a generated
       document -- falsifying the invariant the whole design rests on.
    2. Flatten with the READER's own splitter. The local frontmatter parser
       splits on str.splitlines(), so " ".join(s.splitlines()) covers exactly
       what the reader recognises, by construction, and the two cannot drift
       apart. A hand-listed CRLF/LF/CR set missed eight separators splitlines()
       honours -- U+000B, U+000C, U+001C, U+001D, U+001E, U+0085, U+2028,
       U+2029 -- any of which let `claim --user "me<sep>status: closed"` forge a
       real frontmatter key, silently closing the ticket.
    """
    s = "" if v is None else str(v)
    return scrub(" ".join(s.splitlines()))


def assert_regions(text, regions, what, single_markers=()):
    """Refuse to write `text` unless every declared region is well formed and
    no decision-map marker appears outside them.

    The backstop for scrub(): if a future change adds a user-input path and
    forgets to scrub it, this fails loudly at the write instead of silently
    losing user content on some later resolve() -- which is exactly how the
    same defect survived three fix rounds.

    The final stray-prefix check is the part that generalises: it is what makes
    "a marker in this document was written by the tool" checkable without
    enumerating every marker that could ever exist. Dropping it survived the
    round-5 suite, so it has its own regression test.

    `single_markers` are unpaired one-line carriers (the key marker), counted
    once each rather than as a start/end pair. A tracker item legitimately
    holds exactly one key marker, and without declaring it here the stray-
    prefix check would reject every tracker write.
    """
    problem = None
    accounted = 0
    for start, end in regions:
        n_start, n_end = text.count(start), text.count(end)
        accounted += n_start + n_end
        if n_start > 1 or n_end > 1:
            problem = f"{n_start} start / {n_end} end markers (expected at most one of each)"
        elif n_start != n_end:
            problem = f"unpaired markers ({n_start} start, {n_end} end)"
        elif n_start and text.index(start) > text.index(end):
            problem = "end marker precedes start marker"
        if problem:
            problem = f"region {start!r}: {problem}"
            break
    for marker in single_markers:
        n = text.count(marker)
        accounted += n
        if n > 1 and not problem:
            problem = f"marker {marker!r}: present {n} times (expected at most one)"
    if not problem and text.count(MARKER_PREFIX) != accounted:
        problem = (f"{text.count(MARKER_PREFIX)} decision-map marker(s) present but only "
                   f"{accounted} belong to a declared region")
    if problem:
        raise MarkerIntegrityError(
            f"refusing to write {what}: {problem}. Every string the tool writes is "
            "escaped, so this indicates hand-edited markers in the document (remove "
            "them) or an unscrubbed input path (a bug in the backend).")


def mode(ticket_type):
    return "AFK" if ticket_type in AFK_TYPES else "HITL"


def safe_segment(value, kind):
    """Return `value` if it is safe to use as a path segment / marker payload.

    THE enforcing line for the path half of every backend, so a traversal
    payload is rejected before any path is constructed. It uses the SAME
    SAFE_SLUG_RE that chart() applies to the keys it creates, so it can never
    reject an identifier the tool could legitimately have produced.
    """
    if not isinstance(value, str) or not SAFE_SLUG_RE.match(value):
        raise UnsafeIdentifierError(
            f"invalid {kind} {value!r}: must be a safe slug (letters, digits, "
            "'-', '_'; no path separators, drive letters, '..', or line breaks)")
    return value


# ---------------------------------------------------------------------------
# region text handling
# ---------------------------------------------------------------------------
def region_re(start, end):
    return re.compile(
        re.escape(start) + r".*?" + re.escape(end) + r"\n?", re.DOTALL)


def region_body(text, start, end):
    """The text strictly between one marker pair, or None if absent.

    Literal `str.find` on the exact markers -- no pattern at all, so there is
    nothing to anchor wrongly. Returns None rather than guessing when the
    region is missing.
    """
    i = text.find(start)
    if i < 0:
        return None
    j = text.find(end, i + len(start))
    if j < 0:
        return None
    return text[i + len(start):j]


def replace_region(text, start, end, new_body):
    """Swap the content between one marker pair, leaving the rest byte-identical."""
    i = text.find(start)
    if i < 0:
        return text
    j = text.find(end, i + len(start))
    if j < 0:
        return text
    return text[:i + len(start)] + new_body + text[j:]


def region_text(start, end, lines):
    return start + "\n" + "".join(ln + "\n" for ln in lines) + end


def merge_region_lines(body, items):
    """Union the existing one-line items with `items`, existing order first.

    Never removes and never reorders what is already there (ADR 0057) -- new
    lines are appended. The tool-owned "- (none)" placeholder is dropped as
    soon as a real line exists, and restored if the result is empty.
    """
    existing = [ln for ln in (body or "").splitlines()
                if ln.strip() and ln.strip() != EMPTY_LIST_LINE]
    seen = set(existing)
    for x in items:
        line = f"- {one_line(x)}"
        if line not in seen:
            existing.append(line)
            seen.add(line)
    return existing or [EMPTY_LIST_LINE]


def count_added_lines(body, merged_lines):
    """How many lines the merge actually ADDS to one list region.

    Not `len(merged) - len(existing)`: the tool-owned "- (none)" placeholder is
    dropped when the first real line arrives and restored when the list empties,
    so the arithmetic is off by one in either direction. Count the merged lines
    that were not already there instead.
    """
    existing = {ln for ln in (body or "").splitlines()
                if ln.strip() and ln.strip() != EMPTY_LIST_LINE}
    return sum(1 for ln in merged_lines
               if ln != EMPTY_LIST_LINE and ln not in existing)


def map_merge_detail(added):
    """The one-line `detail` for a map-body merge: what it will add.

    The contract requires every `merge` entry to name what it adds, because
    that string is what the ADR-0039 gate shows the user before the write. A
    map-body merge that reported `detail: null` rendered as a bare `merge
    .../map.md` -- an unnamed write, approved blind.

    `added` is [(noun, count), ...] for the regions that gained lines. The
    fallback covers the one way a merge can change bytes without adding a line:
    a hand-edited region whose blank lines or stale placeholder the merge
    normalises away. Saying "adds 0 lines" there would be a lie.
    """
    parts = [f"{n} {noun} line" + ("s" if n != 1 else "") for noun, n in added]
    if not parts:
        return "normalises the map body's list regions (no new lines)"
    return "adds " + ", ".join(parts)


def render_map_body(m, prologue):
    """The generated part of a map document: the two prose slots and the three
    regions, in the order every backend renders them.

    `prologue` is the backend's own top matter -- the local backend's H1 plus
    overview diagram, or just the diagram on a tracker where the item's native
    title already carries the name. Everything below it is shared, because the
    regions are what the additive merge and the decisions projection reach into,
    and the two backends disagreeing about their order or spelling would make a
    map unreadable by the other backend.

    one_line, not scrub, on the prose slots: they are single-line slots in the
    rendered document -- one paragraph under each of two headings -- so they get
    the same flatten-then-escape treatment every other user string gets. scrub
    alone neutralises the marker but leaves line breaks, and both harms were
    silent: a two-line title was truncated by the reader with no error and no
    divergence entry, and a newline-bearing destination injected a second
    "## Notes" heading AHEAD of the real one.
    """
    fog = region_text(FOG_START, FOG_END,
                      merge_region_lines("", m.get("notYetSpecified") or []))
    oos = region_text(SCOPE_START, SCOPE_END,
                      merge_region_lines("", m.get("outOfScope") or []))
    return (
        prologue +
        f"## Destination\n{one_line(m['destination'])}\n\n"
        f"## Notes\n{one_line(m.get('notes') or '')}\n\n"
        f"## Decisions so far\n\n{DECISIONS_START}\n{DECISIONS_END}\n\n"
        f"## Not yet specified\n\n{fog}\n\n"
        f"## Out of scope\n\n{oos}\n")


SCALAR_MAP_FIELDS = ("title", "destination", "notes")


def scalar_divergences(m, stored_text, fields=SCALAR_MAP_FIELDS):
    """Report -- never apply -- an input scalar that differs from what is stored.

    Scalars are never rewritten on the additive path. The only way to change
    them through the tool is --force, which rewrites the WHOLE map and discards
    every recorded resolution, claim and edge, so the message leads with the
    hand-edit instead.

    The check is containment of the flattened value in the flattened document:
    it drives a REPORT only, never a write, so it deliberately avoids splitting
    the document into sections to compare field-by-field.

    `fields` exists because the three scalars do not all live in the same place
    on every backend. The local backend keeps `title` in the map document (its
    H1), so all three are checked against one text. A tracker keeps the title in
    the item's own **title field** and only `destination` / `notes` in the body,
    so it must check the title against the title. Checking all three against the
    body there reported a `title` divergence on EVERY re-chart -- a divergence
    for text nobody had touched, which is precisely the noise these messages
    exist to avoid, and it would have trained users to ignore them.
    """
    flat = " ".join(norm_eol(stored_text).splitlines())
    div = []
    for field in fields:
        value = m.get(field)
        if value and one_line(value) not in flat:
            div.append(f"map {field!r} in the input differs from the map as stored; "
                       f"left unchanged. To apply it you must re-chart, but "
                       f"{FORCE_COST} -- editing the map by hand is usually "
                       "the right move instead")
    return div


def merge_map_lists(text, m):
    """Union the input's fog / out-of-scope lines into a stored map document.

    -> (new_text, added, divergences). `added` is [(noun, count), ...] for
    map_merge_detail. A document predating a region cannot be merged into, and
    that is reported rather than repaired: inserting the markers would mean
    guessing where the pre-marker list ended, which is the pattern that cost
    rounds 1-3.
    """
    div, added = [], []
    for start, end, items, label, noun in (
            (FOG_START, FOG_END, m.get("notYetSpecified") or [],
             "notYetSpecified", "fog"),
            (SCOPE_START, SCOPE_END, m.get("outOfScope") or [],
             "outOfScope", "out-of-scope")):
        body = region_body(text, start, end)
        if body is None:
            if items:
                div.append(
                    f"this map predates the {label} region, so its "
                    f"{len(items)} line(s) were not merged. Add the "
                    f"{label} markers to the map body by hand to enable "
                    f"merging; re-charting would also fix it, but {FORCE_COST}")
            continue
        merged_lines = merge_region_lines(body, items)
        merged = region_text(start, end, merged_lines)
        text = replace_region(text, start, end, merged[len(start):-len(end)])
        gained = count_added_lines(body, merged_lines)
        if gained:
            added.append((noun, gained))
    return text, added, div


def decisions_region(entries):
    """The "Decisions so far" index, regenerated in full inside its own region.

    The index is a projection of the tickets, not accumulated state, so it is
    rebuilt wholesale. There is no per-line pattern to match and nothing
    outside the region is read or touched -- which is what stops a
    user-authored, index-shaped line elsewhere in the map body from being
    substituted away, and stops a multi-line gist from splitting one entry into
    an orphanable pair.

    `entries` is [(title, link, gist), ...]. **Ordered by ticket key
    (ascending)**, not by when each decision was resolved, so the index is a
    deterministic function of the tickets and re-running the projection never
    reorders it. The caller sorts; this only formats.
    """
    lines = []
    for title, link, gist in entries:
        lines.append(f"- [{title}]({link}) — {gist}".rstrip() + "\n")
    return f"{DECISIONS_START}\n{''.join(lines)}{DECISIONS_END}\n"


def position_diagram_region(key, parents, children):
    """A ticket's position: its blockers, itself, and what it unblocks.

    Three levels and no more (ADR 0063) -- a map may legally hold 100 tickets
    and a whole-map graph is unreadable long before that. Structure only, never
    status or assignee (ADR 0064): a stale "open" on a blocker that has since
    closed tells the reader they cannot pick the ticket up when they can, which
    is the absence-read-as-a-fact shape ADR 0061 exists to prevent.

    Node ids are positional (P0, C0, ...), NOT derived from the key: Mermaid
    ids cannot contain "-", and mapping "-" to "_" would collide "a-b" with
    "a_b", both of which are legal keys.

    Sorted, and de-duplicated, because the bytes this returns are compared for
    equality by the byte-identical no-op guarantee. An unsorted render would
    make an identical re-chart report a spurious change.
    """
    ps, cs = sorted(set(parents)), sorted(set(children))
    lines = ["```mermaid", "graph TD", f'    ME["{key} (this ticket)"]']
    lines += [f'    P{i}["{p}"] --> ME' for i, p in enumerate(ps)]
    lines += [f'    ME --> C{i}["{c}"]' for i, c in enumerate(cs)]
    lines.append("```")
    return GRAPH_START + "\n" + "\n".join(lines) + "\n" + GRAPH_END + "\n"


def set_graph_region(body, region):
    """Replace the graph region, or insert one into a ticket that predates it.

    Insertion goes ABOVE "## Question" so the reader sees the position first.
    A ticket with neither a region nor a Question heading gets the region
    prepended -- never guess at the boundary of content the tool did not
    write, the same conservative rule _reindex_decisions applies to a legacy
    map.md.

    `region` already carries its own trailing newline (position_diagram_region's
    contract), and the block matched by `block_re` extends through that same
    optional trailing newline (region_re's `\\n?`), so the substitution is used
    as-is -- exactly how resolve()'s own `_RESOLUTION_BLOCK_RE.sub` supplies its
    replacement block newline-and-all, with no rstrip needed on either side.
    """
    block_re = region_re(GRAPH_START, GRAPH_END)
    if block_re.search(body):
        return block_re.sub(lambda _m: region, body, count=1)
    heading = "## Question"
    if heading in body:
        return body.replace(heading, region + "\n" + heading, 1)
    return region + body


def force_orphaned_blockers(actions, blockers_of, rewired):
    """The tickets whose position diagram `--force` would otherwise leave
    asserting an edge that no longer exists -> {blocker key: [lost child, ...]}.

    `--force` resets an OVERWRITE'd ticket's own blocking edges, and that is
    documented and intended. What is NOT intended is the other end: the edge
    was drawn at BOTH ends (ADR 0064), so every blocker the ticket named keeps
    a `ME --> C<n>["<child>"]` line for an edge the same run just deleted -- a
    picture of a dead edge, which is the "staleness read as a fact" harm ADR
    0064 exists to prevent. Neither the `pending` nor the `blocker_gains` pass
    sees these tickets: both are driven by the edges an input ADDS, and this is
    an edge it REMOVES.

    Shared rather than written twice because both backends must agree on which
    neighbours a `--force` disturbs -- and therefore on which extra lines its
    dry-run plan shows the user before they approve it (ADR 0039).

    - `actions` is {key: plan action} for every key named in this input.
    - `blockers_of(key)` returns the blockers an OVERWRITE'd key holds RIGHT
      NOW, already narrowed by the caller to the ones that backend can actually
      patch (a file that exists / a ticket still in the snapshot). It is called
      only for OVERWRITE'd keys.
    - `rewired` is {blocked key: container of blocker keys} for the edges this
      same input wires. An edge that is removed and immediately put back is not
      a loss, and announcing it would make an idempotent `--force` re-run report
      a change it does not make.

    A blocker that is itself create/OVERWRITE is skipped: its own plan line
    already rewrites it whole, and the run's own graph pass re-renders it.
    Sorted throughout, because the result feeds both a plan the user reads and
    the bytes of a diagram (see `position_diagram_region` on determinism).
    """
    lost = {}
    for key, action in actions.items():
        if action != "OVERWRITE":
            continue
        for blocker in blockers_of(key):
            if blocker in (rewired.get(key) or ()):
                continue
            if actions.get(blocker) in ("create", "OVERWRITE"):
                continue
            lost.setdefault(blocker, set()).add(key)
    return {k: sorted(v) for k, v in sorted(lost.items())}


def force_orphan_detail(lost):
    """The one-line plan `detail` for a blocker that loses children to --force.

    Non-null by construction: the contract forbids an undescribed `merge`, and
    a bare `merge <path>` is an unnamed write the ADR-0039 gate asks the user to
    approve blind."""
    return "no longer renders as a child in the graph: " + ", ".join(sorted(lost))


def rewired_edges(tickets):
    """{blocked key: {blocker key, ...}} for every edge an input wires.

    The `blocks` direction inverted once, in one place, so both backends feed
    `force_orphaned_blockers` the same shape."""
    out = {}
    for t in tickets:
        for blocked in t.get("blocks") or []:
            out.setdefault(blocked, set()).add(t["key"])
    return out


# ---------------------------------------------------------------------------
# input validation
# ---------------------------------------------------------------------------
REQUIRED_MAP_FIELDS = ("title", "destination")
REQUIRED_TICKET_FIELDS = ("key", "title", "type", "question")


def require(where, container, field, kind, required):
    """Presence + TYPE check for one field, returning its value (or None when an
    optional field is absent/null).

    Presence-only validation was round 4's finding: `title: [1, 2]` reproduced
    the partial-write harm through a wrong type instead of a missing key, and
    `title: null` / a dict / an int were written silently. `null` counts as
    absent for an optional field and as invalid for a required one.
    """
    if field not in container or (container[field] is None and not required):
        if required:
            raise ChartValidationError(f"{where} is missing required field {field!r}")
        return None
    value = container[field]
    if kind is str:
        if not isinstance(value, str):
            raise ChartValidationError(
                f"{where}: field {field!r} must be a string, "
                f"got {type(value).__name__} ({value!r})")
    else:  # list of str
        if not isinstance(value, list) or not all(isinstance(x, str) for x in value):
            raise ChartValidationError(
                f"{where}: field {field!r} must be a list of strings, got {value!r}")
    return value


def validate_key(key, what):
    """A key must be a safe slug AND must not contain '--'.

    The key is a CROSS-BACKEND identity: a tracker carries it in an HTML comment
    (<!-- decision-map:key:<key> -->), and the HTML spec forbids "--" inside
    comment text -- sanitizers and rich-text editors rewrite or truncate such a
    comment, which silently breaks the key->item join and re-creates every
    ticket. Rejected here, at the one place keys are minted, rather than in
    SAFE_SLUG_RE: tightening that would make the local backend silently skip an
    existing "a--b.md" and would reject --ticket for a file that legitimately
    exists.

    Applied to the MAP's own slug too, not only to ticket keys. On a tracker the
    map slug is itself a key marker, so a slug the local backend accepts and a
    tracker cannot carry would make a map unmigratable -- a backend-specific key
    rule is the same defect as no key rule.
    """
    if not SAFE_SLUG_RE.match(key):
        raise ChartValidationError(
            f"invalid {what} {key!r}: must be a safe slug "
            "(letters, digits, '-', '_'; no path separators, drive letters, or '..')")
    if "--" in key:
        raise ChartValidationError(
            f"invalid {what} {key!r}: must not contain '--'. The key is carried "
            "in an HTML comment on ADO and GitHub, where '--' is not allowed "
            "inside comment text; use a single hyphen")


def validate_chart_input(inp, ticket_exists=None):
    """Validate `inp` before chart() writes anything (dry-run or real).

    `ticket_exists`, when given, is a callable key -> bool that lets the
    `blocks` check accept a target that already exists in the map rather than
    requiring it to be re-listed in tickets[] (ADR 0058). It is a pre-write
    check, so nothing is written before it passes.

    - the top-level containers must be the right shape (a missing/mistyped
      "target"/"map"/"tickets" used to raise a bare KeyError/AttributeError
      rather than the ChartValidationError promised here)
    - "map" must have every field chart() reads unconditionally, each a string
    - every ticket must likewise, each a string -- a missing "title"/"question"
      used to raise a bare KeyError mid-write, after the map and earlier tickets
      were already stored
    - optional fields, when present and not null, must be their declared type
    - target.slug and every ticket key must pass validate_key
    - every ticket type must be one of the four valid types
    - every `blocks` target must exist EITHER in this same `inp` OR already in
      the map (ADR 0058)
    """
    if not isinstance(inp, dict):
        raise ChartValidationError(
            f"map_input.json must be a JSON object, got {type(inp).__name__}")
    for name in ("target", "map"):
        if name not in inp:
            raise ChartValidationError(f'map_input.json is missing required key "{name}"')
        if not isinstance(inp[name], dict):
            raise ChartValidationError(
                f'map_input.json\'s "{name}" must be an object, '
                f"got {type(inp[name]).__name__}")
    if "tickets" not in inp:
        raise ChartValidationError('map_input.json is missing required key "tickets"')
    if not isinstance(inp["tickets"], list):
        raise ChartValidationError(
            f'map_input.json\'s "tickets" must be a list, '
            f"got {type(inp['tickets']).__name__}")

    slug = require('map_input.json\'s "target"', inp["target"], "slug", str, True)
    validate_key(slug, "map slug")

    m = inp["map"]
    where_map = 'map_input.json\'s "map"'
    for field in REQUIRED_MAP_FIELDS:
        require(where_map, m, field, str, True)
    require(where_map, m, "notes", str, False)
    for field in ("notYetSpecified", "outOfScope"):
        require(where_map, m, field, list, False)

    keys = set()
    for i, t in enumerate(inp["tickets"]):
        if not isinstance(t, dict):
            raise ChartValidationError(
                f"tickets[{i}] must be an object, got {type(t).__name__}")
        where = f"ticket {t.get('key', f'#{i}')!r}"
        for field in REQUIRED_TICKET_FIELDS:
            require(where, t, field, str, True)
        key = t["key"]
        validate_key(key, "ticket key")
        if key in keys:
            raise ChartValidationError(
                f"ticket key {key!r} appears twice in map_input.json's tickets[]; "
                "keys are unique within a map")
        if t["type"] not in VALID_TICKET_TYPES:
            raise ChartValidationError(
                f"ticket {key!r}: invalid type {t['type']!r}; "
                f"must be one of {sorted(VALID_TICKET_TYPES)}")
        require(where, t, "blocks", list, False)
        keys.add(key)
    for t in inp["tickets"]:
        for blocked in t.get("blocks") or []:
            if blocked == t["key"]:
                raise ChartValidationError(
                    f"ticket {t['key']!r} blocks itself: the edge would leave "
                    "it waiting on a ticket that can never close, so it would "
                    "never appear on the frontier")
            if blocked in keys:
                continue
            if (ticket_exists is not None and isinstance(blocked, str)
                    and SAFE_SLUG_RE.match(blocked) and ticket_exists(blocked)):
                continue
            raise ChartValidationError(
                f"ticket {t['key']!r} blocks unknown ticket {blocked!r} "
                "(not in this map_input's tickets[], and no such ticket "
                "exists in the map)")


# ---------------------------------------------------------------------------
# the key -> item join, for any backend that stores the key in a body
# ---------------------------------------------------------------------------
def key_of_body(body, what, labelled):
    """The single key a stored body carries, or None when it carries none.

    The rules are the contract's "Rules for a map a human has edited", and each
    one is a loud failure rather than a workaround:

    - **two key markers is an error.** Do not take the first, the last, or the
      one that matches the input.
    - **a labelled child with no key marker is an error**, not a skip. Ignoring
      it hides the ticket from the join, so `chart` labels it `create`, and a
      map whose markers were stripped is silently re-created in full and
      presented as a page of ordinary, approvable `create` lines.
    - a child with neither the label nor a marker is simply not a decision-map
      ticket: it returns None and the caller ignores it, so an unrelated
      hand-created child cannot collide with the join.
    - a body that is absent is not an empty body -- norm_eol turns None into ""
      so the search finds no marker rather than throwing.

    **The key marker is authoritative; the label is decorative.** A child
    carrying a key marker IS a decision-map ticket whether or not it still has
    the label. Keying membership on the label instead means one bulk label edit
    hides every ticket from the join.
    """
    found = KEY_MARKER_RE.findall(norm_eol(body))
    if len(found) > 1:
        raise JoinIntegrityError(
            f"{what} carries {len(found)} decision-map key markers ({', '.join(found)}); "
            "exactly one is required. Remove the extras by hand -- guessing which "
            "one is real would silently re-create or mis-target tickets")
    if not found:
        if labelled:
            raise JoinIntegrityError(
                f"{what} carries the decision-map label but no "
                "<!-- decision-map:key:... --> marker, so it cannot be joined to a "
                "ticket key. Restore the marker or remove the label. Carrying on "
                "would re-create the ticket and present that as an ordinary "
                "'create' line for approval")
        return None
    key = found[0]
    if not SAFE_SLUG_RE.match(key):
        raise JoinIntegrityError(
            f"{what} carries the key marker {key!r}, which is not a safe slug. "
            "The marker is tool-owned; correct it by hand")
    return key


# --- lint -------------------------------------------------------------------
#
# Every other subcommand validates the ONE call being made and then writes.
# lint validates the map AS IT STANDS and writes nothing, which is what makes
# it the check an agent can run unattended: it returns a pass/fail an agent
# reads in the conversation, instead of "looks done" being the only signal.
#
# Each rule exists because a flow skill states the invariant in prose and
# nothing enforces it. Prose is advisory; this is the deterministic half.
# Rules live here rather than in either backend so that a map lints the same
# on both (ADR 0062), the way every other shared rule already does.

# The one rule that needs the resolution BODY rather than just the gist.
# A backend that cannot see the body declares this unchecked instead of
# pretending; walking every ticket's comments to check it would cost one
# API call per ticket on the command whose whole value is being cheap
# enough to run after every session.
RULES_NEEDING_RESOLUTION_BODY = ("resolution-without-diagram",)

LINT_ERROR = "error"
LINT_WARNING = "warning"

# argparse's default for `claim --user`. An OPEN ticket still holding it names
# nobody, which is the state work-map calls out: "the files cannot tell you
# who holds it".
ANONYMOUS_ASSIGNEE = "me"

# The fog rule is the only heuristic one here, and a check that cries wolf is
# worse than no check -- a warning nobody trusts trains the reader to skip the
# errors next to it. These thresholds are deliberately strict: a fog line must
# share at least three significant words with a ticket title AND those words
# must be most of the shorter side before it is called a match.
_FOG_MIN_SHARED = 3
_FOG_MIN_RATIO = 0.7
_FOG_STOPWORDS = frozenset("""
a an and are as at be but by can do does for from has have how in into is it
its need not of on or should so than that the their them then there these
this to via we what when where which who why will with you your
""".split())


def _finding(rule, severity, ticket, message):
    return {"rule": rule, "severity": severity, "ticket": ticket,
            "message": message}


def _significant_tokens(s):
    return {t for t in re.findall(r"[a-z0-9]+", (s or "").lower())
            if len(t) > 2 and t not in _FOG_STOPWORDS}


def blocker_cycles(edges):
    """Every strongly connected component of size > 1 in the blocked_by graph.

    Reported per COMPONENT, not per member: a three-ticket cycle is one
    problem with three names, and three findings would read as three separate
    problems to fix. Tarjan rather than path enumeration because the latter is
    exponential on a dense map and the cap is 100 tickets.

    Self-loops are left out -- `self-blocked` names them better, and a ticket
    appearing in a "cycle" with itself reads as noise next to it.
    """
    index, low, onstack, stack, counter, out = {}, {}, {}, [], [0], []

    def connect(v):
        index[v] = low[v] = counter[0]
        counter[0] += 1
        stack.append(v)
        onstack[v] = True
        for w in edges.get(v, ()):
            if w not in index:
                connect(w)
                low[v] = min(low[v], low[w])
            elif onstack.get(w):
                low[v] = min(low[v], index[w])
        if low[v] == index[v]:
            comp = []
            while True:
                w = stack.pop()
                onstack[w] = False
                comp.append(w)
                if w == v:
                    break
            if len(comp) > 1:
                out.append(sorted(comp))

    for v in sorted(edges):
        if v not in index:
            connect(v)
    return sorted(out)


def lint_findings(map_text, tickets, resolution_bodies=True):
    """Every problem with one map, errors first, warnings after.

    `tickets` is the read shape plus one field: each dict carries `key`,
    `name`, `status`, `assignee`, `blockedBy`, `gist` and `resolution` -- the
    text of the ticket's resolution region, or None when it has none. Both
    backends assemble exactly that and call this, so "clean" means the same
    thing on a folder of markdown and on a tracker.

    `resolution_bodies` is False on a backend that cannot cheaply see a
    resolution's body. Rules needing it are then skipped rather than
    guessed at -- and the caller reports them under `notChecked`, because
    a check silently not run reads as a check that passed.

    Returns a list; the caller decides what an empty list is worth.
    """
    keys = {t["key"] for t in tickets}
    errors, warnings, edges = [], [], {}
    gist_chars, gist_over, gist_excess = 0, 0, 0

    for t in tickets:
        key = t["key"]
        blockers = t.get("blockedBy") or []
        for b in blockers:
            if b == key:
                errors.append(_finding(
                    "self-blocked", LINT_ERROR, key,
                    f"{key!r} lists itself under blocked_by, so it can never "
                    "reach the frontier"))
            elif b not in keys:
                errors.append(_finding(
                    "dangling-blocker", LINT_ERROR, key,
                    f"{key!r} is blocked by {b!r}, which is not a ticket on "
                    "this map; frontier counts a missing blocker as unsatisfied, "
                    "so the ticket stays blocked forever"))
        edges[key] = [b for b in blockers if b in keys and b != key]

    for comp in blocker_cycles(edges):
        errors.append(_finding(
            "blocker-cycle", LINT_ERROR, comp[0],
            "blocking cycle: " + " -> ".join(comp + [comp[0]]) +
            " -- every ticket in it stays blocked forever, and the map reads "
            "as stalled rather than broken"))

    for t in tickets:
        key = t["key"]
        status = t.get("status") or "open"
        resolution = t.get("resolution") or ""
        if status != "open":
            # A tracker keeps the resolution BODY in a native comment the
            # snapshot does not hold, so there the machine-readable record is
            # the gist region instead. Same rule, same name, the half of the
            # record that backend actually stores.
            what = "resolution block" if resolution_bodies else "gist"
            record = resolution if resolution_bodies else one_line(t.get("gist") or "")
            if not record.strip():
                errors.append(_finding(
                    "closed-without-resolution", LINT_ERROR, key,
                    f"{key!r} is closed but carries no {what}, so the map "
                    "indexes a decision whose answer was never recorded"))
            elif resolution_bodies and "```mermaid" not in record:
                warnings.append(_finding(
                    "resolution-without-diagram", LINT_WARNING, key,
                    f"{key!r} resolves without a mermaid diagram of the answer; "
                    "a reader opening it sees prose before they see what was "
                    "decided (ADR 0065)"))
        gist = one_line(t.get("gist") or "")
        gist_chars += len(gist)
        if len(gist) > GIST_MAX:
            gist_over += 1
            gist_excess += len(gist) - GIST_MAX
            warnings.append(_finding(
                "gist-too-long", LINT_WARNING, key,
                f"{key!r} carries a {len(gist)}-character gist (limit "
                f"{GIST_MAX}); resolve warns and records it anyway, so the map "
                "index is unreadable until it is shortened by hand"))
        if status == "open" and (t.get("assignee") or "") == ANONYMOUS_ASSIGNEE:
            warnings.append(_finding(
                "anonymous-claim", LINT_WARNING, key,
                f"{key!r} is claimed by the anonymous default "
                f"{ANONYMOUS_ASSIGNEE!r}, so the map cannot say which session "
                "holds it and no other session can tell whether it is live"))

    # The corpus, not the ticket. `read` returns EVERY stored gist, so an
    # over-long one is not a local blemish -- it is re-read in full by every
    # session that opens the map, and N separate `gist-too-long` warnings read
    # as N formatting nits rather than as the recurring cost they actually are.
    # Raising `gist-too-long` to an error would instead let one map's
    # accumulated debt block sessions that never touched it, and ADR 0066
    # already settled that a formatting rule must not cost a recorded
    # decision. So: name the total, block nothing (ADR 0068).
    if gist_excess > GIST_BUDGET_SLACK:
        warnings.append(_finding(
            "gist-budget", LINT_WARNING, None,
            f"{gist_over} gists exceed the {GIST_MAX}-character limit; this "
            f"map's gists total {gist_chars} characters, {gist_excess} of "
            f"them over budget. `read` carries every one into every session, "
            f"so shortening those {gist_over} is the single edit that makes "
            "each future session on this map cheaper"))

    by_tokens = [(t["key"], _significant_tokens(t.get("name") or t["key"]))
                 for t in tickets]
    fog = region_body(norm_eol(map_text or ""), FOG_START, FOG_END) or ""
    for raw in fog.splitlines():
        line = raw.strip()
        if not line or line == EMPTY_LIST_LINE:
            continue
        ftoks = _significant_tokens(line)
        if not ftoks:
            continue
        for key, ttoks in by_tokens:
            shared = ftoks & ttoks
            if not ttoks or len(shared) < _FOG_MIN_SHARED:
                continue
            if len(shared) / min(len(ftoks), len(ttoks)) < _FOG_MIN_RATIO:
                continue
            warnings.append(_finding(
                "fog-line-graduated", LINT_WARNING, key,
                f"the fog line {line.lstrip('- ')!r} reads as ticket {key!r}, "
                "which already exists; an additive chart never deletes, so the "
                "line has to go by hand or the map keeps advertising fog it "
                "has already answered"))
            break

    return errors + warnings
