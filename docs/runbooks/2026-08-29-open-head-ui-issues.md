# Runbook — open the two follow-up issues for #106

**Boundary.** `gh` is not installed on this machine and `gh auth login` is interactive, so
Claude cannot create GitHub issues. **Both issues below are yours to create.** The bodies are
written out in full so this is copy-and-paste, not a research task.

**Measured, not inherited.** Every number in this document was read from the live system on
**2026-08-29**, not from a ticket or a plan. Provenance is given per line.

| fact | value | measured how | when (UTC) |
|---|---|---|---|
| backend deployment | `67d80b09-…`, status 4, active | Azure ARM deployments API | 14:25 |
| deployed JS bundle | `/assets/index-C75Rzh3k.js` | fetched `index.html` from the SWA | 14:25 |
| `bdg-rail-fab` in bundle | present (1) | grep of the fetched bundle | 14:25 |
| `families/head` in bundle | **absent (0)** | grep of the fetched bundle | 14:25 |
| families in prod | 2, both with a head | direct SQL against prod | 14:15 |
| each head is a current member | yes, both | direct SQL (join to `Users.FamilyId`) | 14:15 |
| members per family | 1 each | direct SQL | 14:15 |

**Not measured:** the `BudgetChanges` row count. The firewall rule was closed before that read
and was not reopened. It is not needed for either issue below.

---

## Issue 1 — the family head role has no UI

**Type:** feature gap. **Severity:** the role cannot be transferred at all.

### Title

```
Family head has no UI — the role can be seen by nobody and transferred by no one
```

### Body

```markdown
Shipped in #106: `Family.HeadUserId`, `POST /api/families/head`, and
`FamilyMemberDto.IsHead`. All three are live in prod.

**Nothing in the SPA uses any of them.** Measured 2026-08-29 against the deployed
bundle `/assets/index-C75Rzh3k.js`: `families/head` appears 0 times. There is no
`isHead` reference anywhere in `frontend/src`.

## Consequence

menunest-201 rule 2 — "only the current head hands the role over" — cannot be
exercised by a user, because there is no control that calls the endpoint. The head
is whoever the `AddFamilyHead` backfill selected, permanently.

Prod today: 2 families, each headed by its creator, each with 1 member. The problem
is invisible until a family has two people.

## Why it happened

Plan 3 (the shortcut rail frontend) was written before Plan 2 (the family head role)
and scoped no screen for it. That is recorded on the `build-ship` ticket of the
`shortcut-rail-106` decision map as a KNOWN GAP.

## What is needed

- A head badge on the family members screen, fed by `FamilyMemberDto.IsHead`.
  menunest-201 recorded that where the badge goes was **derived, not asked** — so it
  is a design decision, not a settled one.
- A transfer control, visible only to the current head, calling
  `POST /api/families/head` with `{ "newHeadUserId": "<guid>" }` (204 on success).
- The `LeaveFamily` guard already returns "You are the family head. Hand the role
  over to another member before you leave." The screen should make that recoverable
  rather than a dead end — a member who hits it needs the transfer control in reach.

## Backend is done — do not rebuild it

`TransferHeadHandler` checks membership; `Family.TransferHeadTo` checks that the
caller is the current head. Both are covered by tests. This issue is frontend only.

Refs #106
```

---

## Issue 2 — `canUndo` ignores who is allowed to undo

**Type:** defect. **Severity:** latent in prod today; live the moment any family has two members.

### Title

```
Change history offers Undo on rows the member is not allowed to undo
```

### Body

```markdown
`ListChangesHandler` computes `CanUndo: !gone`, where `gone` means the envelope was
deleted (menunest-197). That is the **only** thing it accounts for.

It does not account for **who may undo the row**. menunest-198 says a member may undo
their own change, and the family head may undo anyone's — enforced in
`UndoChangeHandler` and `RedoChangeHandler`.

## Consequence

The Change history sheet lists every family member's changes and renders each row's
button with `disabled={!r.canUndo || busy}`. So an ordinary member sees an **enabled**
Undo button on another member's row. Pressing it reaches the handler, fails the
ownership check, and throws `DomainException("You can only undo your own changes.")`.

The rail's own Undo button has the same flaw by inheritance: `latestUndoable` picks
the newest row with `!isUndone && canUndo`, which may be a row authored by somebody
else, so the rail can arm a button that cannot work.

## Why it is not visible yet

Measured 2026-08-29: both prod families have exactly 1 member, so no member can see
another member's row. This ships broken the first time a second person joins.

## Suggested fix

Move the permission into `CanUndo` so the server stays the single source of truth,
rather than duplicating menunest-198 in the SPA:

- `ListChangesHandler` already resolves the caller via `RequireFamilyAsync`. It
  currently discards the user (`var (_, familyId) = …`). Take the user, read the
  family's `HeadUserId` once, and set
  `CanUndo = !gone && (row.UserId == user.Id || user.Id == family.HeadUserId)`.
- Give the blocked case its own `BlockedReason`, distinct from the deleted-envelope
  one — something like "Only <name> or the family head can undo this."
- `latestUndoable` / `latestRedoable` then need no change: they already filter on
  `canUndo`.

## Test to add

An ordinary member listing history that contains another member's change gets
`canUndo: false` on that row and `canUndo: true` on their own; the family head gets
`true` on both. `HeadUndoesAnyoneTests` already has the fixture shape to copy.

Refs #106
```

---

## How to create them

**Go to:** https://github.com/ThodsaphonSonthiphin/MenuNest/issues/new

**Do:**
1. Paste the Issue 1 title into the title field.
2. Paste the Issue 1 body (the fenced `markdown` block, without the fence lines) into the body.
3. Press **Submit new issue**. Write the number it gets on this file.
4. Go to https://github.com/ThodsaphonSonthiphin/MenuNest/issues/new again.
5. Repeat steps 1–3 for Issue 2.

**Do not:**
- Do not reopen #106 — it is closed correctly. The rail and the undo engine shipped and work;
  these two are follow-ups, which is why both bodies end in `Refs #106` rather than `Closes`.
- Do not paste the outer `### Body` heading or the triple-backtick fence lines — they are this
  document's scaffolding, not issue content.
- Do not fix Issue 2 by disabling the button in the SPA — that duplicates menunest-198 in a
  second place, which is the thing the single-seam design in `UndoChangeHandler` exists to avoid.

**How to verify yourself:** open
https://github.com/ThodsaphonSonthiphin/MenuNest/issues?q=is%3Aissue+is%3Aopen+%23106
Two open issues must be listed, both referencing #106.

**Then report:** write both issue numbers into this file and commit it, so the next session
finds them without asking. This file is the record — not the chat.

---

## Pre-declared assertions

State them before acting, so the after-check is a test and not a description.

| assertion | before | after |
|---|---|---|
| open issues referencing #106 | 0 | **2** |
| #106 itself | closed | **still closed** (blast radius) |
| any other issue modified | — | **none** (blast radius) |

## What is still owed after this

Neither issue is verified as *fixed* by creating it. The verification owed later is:

- **Issue 1:** a second person joins a family, the head badge shows on exactly one member,
  and the head can move the role to the other member and back.
- **Issue 2:** with two members, an ordinary member's Change history shows a disabled Undo
  on the other member's row with a reason, and the head's shows enabled.

Both need a two-member family, which prod does not have. That is the real blocker on
checking either one, and it is worth saying rather than leaving the reader to discover it.
