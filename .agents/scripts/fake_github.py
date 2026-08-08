#!/usr/bin/env python3
r"""fake_github.py — an in-memory GitHub Issues API for testing the backend.

Why a fake at all. The GitHub backend's interesting behaviour is almost entirely
in what it does with what the API returns: the key join, the additive plan, the
byte-identical no-op, bucket precedence, the loud failures on a hand-edited map.
Testing that against the live API would be slow, would need a throwaway repo,
would burn the 80-writes-per-minute budget, and could not produce a
hand-corrupted map on demand. So the transport is injected and this stands in
for it.

Why it is trustworthy. Every behaviour below that a test could depend on was
**verified against the live API on 2026-08-01** before being written here, and
each one is annotated with what was observed. The three that matter most are
error paths a naive fake would have made succeed:

  * adding a sub-issue that is already a sub-issue  -> 422
    "Issue may not contain duplicate sub-issues and Sub issue may only have one
    parent"
  * adding a dependency that already exists          -> 422
    "Validation failed: Target issue has already been taken"
  * creating a label that already exists             -> 422 `already_exists`

Also replicated, because each one has bitten this design already:

  * the issue **database id is unrelated to the issue number** (live: issue
    number 12439 has id 3789102272), so a test using one where the other is
    needed fails here exactly as it would live;
  * `body` can be JSON **null**, not "";
  * GraphQL returns `state` as **OPEN/CLOSED**, REST as **open/closed**;
  * sub-issues come back in **creation order** — the fake returns them
    deliberately reversed, so any code that forgets to sort key-ascending fails;
  * line endings are echoed back as submitted, so `crlf=True` reproduces the
    real "a human edited this in the browser" body.

`live_parity_notes()` lists every claim this fake makes about the real API, so a
future session can re-verify them in one pass rather than re-deriving them.
"""
import json

from github_map_ops import GitHubError


class FakeError(GitHubError):
    """Stands in for GitHubError: `gh api` exiting non-zero with the API's body.

    It IS a GitHubError, not a look-alike -- this fake substitutes for `GhApi`,
    so it must fail with what `GhApi` fails with or the backend's own except
    clauses would not be under test at all. The backend matches on message
    substrings ("already been taken", "already_exists"), so the messages here
    are the real ones verbatim.
    """


def _db_id(number):
    """A database id that could not be mistaken for the issue number.

    Live values are ~10 digits and share no arithmetic relationship with the
    number; anything derived-but-large reproduces the class of bug where one is
    passed where the other belongs.
    """
    return 5_000_000_000 + number * 7919


class FakeGitHub:
    def __init__(self, repo="acme/widgets", login="octocat", crlf=False,
                 sub_issue_page_lie=False):
        self.repo = repo
        self.login = login
        self.crlf = crlf
        # Forces GraphQL to report hasNextPage even with few children, to
        # exercise the refuse-rather-than-truncate path without building 101
        # issues.
        self.sub_issue_page_lie = sub_issue_page_lie
        self.issues = {}          # number -> dict
        self.labels = {}          # name -> dict
        self.comments = {}        # number -> [body]
        self._next = 1
        self.calls = []           # every call, for asserting call budgets
        self.writes = []          # the mutating subset

    # --- helpers for tests -------------------------------------------------
    def add_issue(self, title="", body=None, labels=(), state="open",
                  assignees=(), parent=None, repo=None, number=None):
        """`repo` and `number` are settable so a test can build the one shape the
        real API allows and a single-repo fake cannot: a sub-issue living in a
        DIFFERENT repository of the same owner, whose issue number collides with
        an issue in the map's repo. Numbers restart per repository, so #7 in two
        repos are two different issues -- and a backend that keys the join on the
        bare number, or addresses writes at the map's repo, gets it wrong in a way
        no same-repo test can see."""
        if number is None:
            number = self._next
            self._next += 1
        else:
            self._next = max(self._next, number + 1)
        r = repo or self.repo
        self.issues[(r, number)] = {
            "number": number, "id": _db_id(number if r == self.repo
                                           else number + 500_000),
            "repo": r, "title": title, "body": body, "state": state,
            "labels": list(labels), "assignees": list(assignees),
            "parent": parent, "blockedBy": []}
        self.comments[(r, number)] = []
        return number

    def _key(self, number, repo=None):
        return (repo or self.repo, number)

    def issue(self, number, repo=None):
        return self.issues[self._key(number, repo)]

    def body_of(self, number, repo=None):
        return self.issue(number, repo)["body"]

    def comments_on(self, number, repo=None):
        return self.comments[self._key(number, repo)]

    def children_of(self, number, repo=None):
        """(repo, number) of every sub-issue, in creation order."""
        parent = self._key(number, repo)
        return [k for k, i in self.issues.items() if i["parent"] == parent]

    def reset_counters(self):
        self.calls, self.writes = [], []

    @property
    def write_count(self):
        return len(self.writes)

    def _out(self, body):
        if body is None:
            return None
        return body.replace("\n", "\r\n") if self.crlf else body

    def _issue_rest(self, i):
        return {"number": i["number"], "id": i["id"], "title": i["title"],
                "body": self._out(i["body"]), "state": i["state"],
                "state_reason": None,
                "html_url": f"https://github.com/{i['repo']}/issues/{i['number']}",
                "labels": [{"name": x} for x in i["labels"]],
                "assignees": [{"login": x} for x in i["assignees"]]}

    # --- the transport surface --------------------------------------------
    def whoami(self):
        self.calls.append(("GET", "user"))
        return self.login

    def rest(self, path, method=None, payload=None):
        self.calls.append((method or "GET", path))
        # A write is any non-GET, not "a call with a payload". The
        # remove-dependency DELETE carries no body, so counting payloads made it
        # invisible both here and to the backend's rate-limit pacing.
        if method and method.upper() != "GET":
            self.writes.append((method, path, payload))
        base, _, query = path.partition("?")
        parts = base.strip("/").split("/")
        q = dict(kv.split("=", 1) for kv in query.split("&") if "=" in kv)

        if base == "user":
            return {"login": self.login}
        if not base.startswith("repos/") or len(parts) < 3:
            raise FakeError(f"fake: unrouted path {path!r}")
        # The repo is read FROM THE PATH, and an issue is looked up in that repo
        # only. That is what makes a write addressed to the wrong repo fail here
        # the way it would live, instead of silently hitting the map's copy.
        repo = f"{parts[1]}/{parts[2]}"
        rest = parts[3:]

        def get_issue(num):
            i = self.issues.get((repo, int(num)))
            if i is None:
                raise FakeError(
                    f'gh api failed: {{"message":"Not Found","status":"404"}} '
                    f'(no issue #{num} in {repo})')
            return i

        if rest == ["labels"]:
            if method == "POST":
                name = payload["name"]
                if name in self.labels:
                    raise FakeError(
                        'gh api failed: {"message":"Validation Failed","errors":'
                        '[{"resource":"Label","code":"already_exists","field":'
                        '"name"}],"status":"422"}')
                self.labels[name] = dict(payload)
                return dict(payload)
            page = int(q.get("page", 1))
            return [{"name": n} for n in sorted(self.labels)] if page == 1 else []

        if rest == ["issues"]:
            if method == "POST":
                n = self.add_issue(title=payload.get("title", ""),
                                   body=payload.get("body"),
                                   labels=payload.get("labels") or [],
                                   assignees=payload.get("assignees") or [],
                                   repo=repo)
                return self._issue_rest(self.issues[(repo, n)])
            page = int(q.get("page", 1))
            want = q.get("labels")
            hits = [self._issue_rest(i) for k, i in self.issues.items()
                    if k[0] == repo and (not want or want in i["labels"])]
            return hits if page == 1 else []

        if len(rest) == 2 and rest[0] == "issues":
            i = get_issue(rest[1])
            if method == "PATCH":
                for field in ("title", "body", "state"):
                    if field in payload:
                        i[field] = payload[field]
                if "assignees" in payload:
                    i["assignees"] = list(payload["assignees"])
                if "labels" in payload:
                    i["labels"] = list(payload["labels"])
            return self._issue_rest(i)

        if len(rest) == 3 and rest[0] == "issues" and rest[2] == "sub_issues":
            parent = get_issue(rest[1])
            pkey = (repo, parent["number"])
            if method == "POST":
                child = self._by_db_id(payload["sub_issue_id"])
                if child["parent"] is not None:
                    # Live 422: duplicate sub-issue AND one-parent-only share a
                    # single message, so a backend cannot tell them apart either.
                    raise FakeError(
                        'gh api failed: {"message":"An error occurred while '
                        'adding the sub-issue to the parent issue. Issue may not '
                        'contain duplicate sub-issues and Sub issue may only '
                        'have one parent","status":"422"}')
                child["parent"] = pkey
                return self._issue_rest(child)
            return [self._issue_rest(self.issues[c])
                    for c in self.children_of(parent["number"], repo)]

        if len(rest) == 3 and rest[0] == "issues" and rest[2] == "comments":
            i = get_issue(rest[1])
            k = (repo, i["number"])
            if method == "POST":
                self.comments[k].append(payload["body"])
                return {"id": 1, "html_url": "https://example.invalid/c"}
            return [{"id": n, "body": b} for n, b in enumerate(self.comments[k])]

        if (len(rest) == 4 and rest[0] == "issues"
                and rest[2] == "dependencies" and rest[3] == "blocked_by"):
            i = get_issue(rest[1])
            if method == "POST":
                blocker = self._by_db_id(payload["issue_id"])
                bref = (blocker["repo"], blocker["number"])
                if bref in i["blockedBy"]:
                    raise FakeError(
                        'gh api failed: {"message":"An error occurred while '
                        'adding the blocking issue to the issue. Validation '
                        'failed: Target issue has already been taken",'
                        '"status":"422"}')
                i["blockedBy"].append(bref)
                return self._issue_rest(i)
            return [self._issue_rest(self.issues[b]) for b in i["blockedBy"]]

        if (len(rest) == 5 and rest[0] == "issues"
                and rest[2] == "dependencies" and rest[3] == "blocked_by"
                and method == "DELETE"):
            i = get_issue(rest[1])
            blocker = self._by_db_id(int(rest[4]))
            bref = (blocker["repo"], blocker["number"])
            if bref in i["blockedBy"]:
                i["blockedBy"].remove(bref)
            return {}

        raise FakeError(f"fake: unrouted {method or 'GET'} {path!r}")

    def _by_db_id(self, db_id):
        for i in self.issues.values():
            if i["id"] == db_id:
                return i
        raise FakeError(
            f'fake: no issue has database id {db_id}. The database id is NOT '
            f'the issue number -- passing a number here is the bug this '
            f'reproduces. {{"message":"Not Found","status":"404"}}')

    # Every field _SNAPSHOT_QUERY selects. Checked against the query text below,
    # so a field the backend starts selecting but the fake never returns is a
    # test failure rather than a silent None in production.
    REQUIRED_QUERY_FIELDS = ("number", "databaseId", "title", "body", "state",
                             "url", "labels", "subIssues", "assignees",
                             "blockedBy", "repository", "pageInfo", "totalCount")

    def graphql(self, query, variables):
        self.calls.append(("GRAPHQL", variables.get("number")))
        if "subIssues" not in query:
            raise FakeError(f"fake: unrouted graphql query {query[:80]!r}")
        # The fake cannot execute GraphQL, so it cannot verify a query the way a
        # server would. What it CAN do is refuse to answer a query that asks for
        # something it does not model -- otherwise a new field in
        # _SNAPSHOT_QUERY silently reads back as None in every test while
        # working (or not) only in production.
        for field in self.REQUIRED_QUERY_FIELDS:
            if field not in query:
                raise FakeError(
                    f"fake: _SNAPSHOT_QUERY no longer selects {field!r}, which "
                    "this fake still returns. Update both together, or the "
                    "tests stop describing the real read")
        repo = f"{variables['owner']}/{variables['repo']}"
        i = self.issues.get((repo, variables["number"]))
        if i is None:
            return {"repository": {"issue": None}}
        # Creation order is what the live API returns; reversing it here means
        # any code path that forgets to sort key-ascending produces a different
        # document and fails its test.
        kids = sorted(self.children_of(i["number"], repo),
                      key=lambda k: k[1], reverse=True)
        return {"repository": {"issue": {
            **self._gql(i),
            "subIssues": {
                "totalCount": len(kids),
                "pageInfo": {"hasNextPage": bool(self.sub_issue_page_lie)},
                "nodes": [{
                    **self._gql(self.issues[c]),
                    "assignees": {"nodes": [{"login": a}
                                            for a in self.issues[c]["assignees"]]},
                    "labels": {"nodes": [{"name": x}
                                         for x in self.issues[c]["labels"]]},
                    "blockedBy": {
                        "totalCount": len(self.issues[c]["blockedBy"]),
                        "nodes": [{"number": self.issues[b]["number"],
                                   "databaseId": self.issues[b]["id"],
                                   "body": self._out(self.issues[b]["body"]),
                                   "repository": {"nameWithOwner": b[0]}}
                                  for b in self.issues[c]["blockedBy"]
                                  if b in self.issues]},
                } for c in kids],
            },
        }}}

    def _gql(self, i):
        return {"number": i["number"], "databaseId": i["id"],
                "title": i["title"], "body": self._out(i["body"]),
                # GraphQL uppercases the state; REST does not. A backend that
                # compares against "closed" without normalising reads every
                # closed ticket as open.
                "state": i["state"].upper(),
                "url": f"https://github.com/{i['repo']}/issues/{i['number']}",
                # A sub-issue may live in another repo of the same owner, and
                # issue numbers restart per repo -- so the join needs this, and
                # a fake that omitted it could not catch a number collision.
                "repository": {"nameWithOwner": i["repo"]},
                "labels": {"nodes": [{"name": x} for x in i["labels"]]}}


def live_parity_notes():
    """Every claim this fake makes about the real API, so it can be re-verified
    in one pass instead of re-derived. Each was observed on 2026-08-01 against
    ThodsaphonSonthiphin/decision-map-probe with gh 2.93.0."""
    return [
        "issue databaseId is unrelated to number (live: #12439 -> id 3789102272)",
        "duplicate sub_issues POST -> 422 'may not contain duplicate sub-issues'",
        "duplicate dependencies/blocked_by POST -> 422 'has already been taken'",
        "duplicate labels POST -> 422 errors[].code == 'already_exists'",
        "sub_issues listing returns full issue objects including body",
        "GraphQL Issue exposes blockedBy / blocking / parent / subIssues",
        "GraphQL state is OPEN/CLOSED; REST state is open/closed",
        "body may be JSON null on an issue created with no description",
        "line endings are echoed back as submitted; both CRLF and LF occur live",
        "state_reason is nullable on a closed issue -- never key on it",
    ]


if __name__ == "__main__":
    print(json.dumps(live_parity_notes(), indent=2))
