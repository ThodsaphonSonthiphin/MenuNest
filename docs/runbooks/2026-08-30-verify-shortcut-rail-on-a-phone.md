# Runbook — verify the Shortcut rail in production, on a real phone

**Boundary.** I cannot sign in as you, and I cannot hold a phone. CLAUDE.md requires an
interactive check on a real device for any mockup-backed UI change. What I ran instead was a
390×844 Chromium — an emulation, not the check. **Every step below is yours.** I read the
deployment, the bundle and the database; I do not click.

> ## ⏰ This has a deadline: 31 August
>
> **Change history** shows min(7 days, since the 1st of the month) — the month is a **hard**
> cut (menunest-194). On **1 September the sheet goes empty**, and any act you record in
> August disappears from it for good. Steps 4–6 cannot be done with an August act after
> 31 August. After that you must create a fresh act in September and start at Step 4.

---

## Baseline

**Re-measured 2026-08-30T08:10Z, and it had moved.** The first baseline (00:47Z) is kept below
it, struck through, because the difference is the point.

| fact | value | measured how |
|---|---|---|
| `origin/main` | `a821517` | `git rev-parse --short origin/main` |
| API `GET /version` | `0.1.0+a821517`, built `2026-08-30T00:57:06Z` | public HTTP, no auth |
| API commit vs `origin/main` | **match** | compared the two above |
| SPA JS bundle | `/assets/index-BnvfCauN.js` | fetched `index.html` from the SWA |
| SPA CSS bundle | `/assets/index-BZ4bw_uv.css` — **unchanged** | same |
| `bdg-rail-fab` / `-glyph` / `-tag` / `-item` in JS bundle | all present | grep of the fetched bundle |
| `bdg-history-sheet` in JS bundle | present | grep of the fetched bundle |
| rail CSS rules in the shipped stylesheet | **19**, all as authored | parsed the fetched CSS |
| the rail's accent | `#4f46e5` on `.bdg-rail` | same parse |
| the FAB colour rule | `.bdg-rail .e-fab.e-btn` + `:hover/:focus/:active` → `background:var(--accent)` | same parse |
| the glyph swap | `⋮` on `.bdg-rail-glyph:before`, `×` under `.is-open` | same parse |

**What changed since 00:47Z, and why it matters more than it looks.** The baseline said
`964ff38`. It is now `a821517` — *the commit that added this runbook*. Pushing it to `main`
deployed to prod (`.github/workflows/main_menunest.yml`), which rebuilt the app, which moved
the **App version** the runbook tells you to check. **The act of recording the baseline
invalidated the baseline.** Had you followed Step 1 unamended you would have read `a821517`,
compared it against a runbook demanding `964ff38`, and reported a deployment fault that does
not exist. Step 1 and the outcome table are corrected to `a821517`.

The JS bundle hash moved with it (`index-AbkhhBiA.js` → `index-BnvfCauN.js`) because the short
SHA is embedded at build time (ADR-107/109). The **CSS** hash did not move — no stylesheet
byte changed. That pairing is the confirmation that this was a rebuild, not a code change.

**Not re-measured at 08:10Z:** the Azure deployment record. `GET /version` is the running app's
own answer and is stronger evidence than the deployment log, so I did not open the ARM call again.

**Never measured:** the `BudgetChanges` row count. Reading it needs a temporary SQL firewall
rule, and I did not open one. Step 0 has you record the equivalent from the screen instead,
which is the surface you will re-check anyway.

**Older than the rest — 2026-08-29T14:15Z, about 18 hours stale:** 2 families in prod, both
with a head, each head a current member, **1 member each**.

---

## Pre-declared assertions

Stated before you act, so the after-check is a test and not an opinion.

| # | assertion | before | after |
|---|---|---|---|
| A1 | the rail's main button is **indigo** `#4f46e5`, the same purple-blue as the page | — | indigo, **not pink** |
| A2 | pressing it shows **exactly 3** circles | — | 3 |
| A3 | order out from the main button | — | Undo, Redo, Change history |
| A4 | one **Envelope**'s Assigned figure, after assign then undo | value **V** (Step 0) | back to **V** |
| A5 | **Ready to Assign**, after assign then undo | value **R** (Step 0) | back to **R** |
| A6 | rows in **Change history** | count **C** (Step 0) | **C + 1** |
| A7 | *blast radius* — the `+` button on the account detail screen | present | still present |
| A8 | *blast radius* — the rail on the account detail screen | absent | still absent |

---

## Step 0 — Write down what the numbers are now

This is the before-snapshot. Without it, Steps 4–6 prove nothing.

**Go to:** https://green-rock-098e70e00.7.azurestaticapps.net/budget

**Do:**
1. Sign in as yourself.
2. Choose one **Envelope** you will use for the whole test. Write its name here: `__________`
3. Write down that Envelope's **Assigned** figure. This is **V** = `__________`
4. Write down the **Ready to Assign** figure at the top. This is **R** = `__________`
5. Press the rail, then press **Change history**. Count the rows. This is **C** = `__________`
6. Close the sheet.

**Do not:**
- Do not pick an **Everyday envelope** — assigning into one re-freezes the **Daily allowance**
  (a **Budgeting event**), which adds a second moving part to every later check.
- Do not skip writing V, R and C down. Reading them off the screen afterwards is not a
  baseline; it is a memory.

**How to verify yourself:** you have three numbers written down and an Envelope name.

**Then report:** write V, R, C and the Envelope name into this file, under Step 0.

---

## Step 1 — Confirm the phone has the new build

This step exists because of the trap below. Do it first, every time.

> **Saved is not applied.** Your phone can hold an old copy of the app. If it does, the rail
> is simply absent and everything after this step reads as a failure that is not real. The
> app's own **App version** is the detector: the SPA `/settings` page shows the app build and
> the API build side by side with a **ตรงกัน / ไม่ตรงกัน** badge (ADR-110).

**Go to:** https://green-rock-098e70e00.7.azurestaticapps.net/settings

**Do:**
1. Read the version badge. It must say **ตรงกัน**.
2. Read the commit shown. It must be `a821517`.
3. If either is wrong: close every tab of the app, then reopen the URL.
4. If it is still wrong: open the page in a private/incognito window.

**Do not:**
- Do not continue past this step on **ไม่ตรงกัน** — every later result would be about the old
  build, and you would report a fault that was fixed yesterday.

**How to verify yourself:** open https://menunest.azurewebsites.net/version in the phone
browser. It needs no sign-in. It must show `"commit":"a821517"`. That is the API's own answer,
independent of anything the app shows you.

**Then report:** the commit you saw.

---

## Step 2 — The rail appears, and looks right at rest

**Go to:** https://green-rock-098e70e00.7.azurestaticapps.net/budget

**Do:**
1. Look at the bottom-right corner.
2. Check the round button is there.
3. Check its colour.

**Expected:** one round **indigo** button, bottom-right, showing `⋮`.

**Do not:**
- Do not accept a **pink** button. It shipped pink once — Syncfusion's theme paints its own
  button Material pink at the same CSS strength as ours and won on load order. Pink means the
  fix regressed. Report it and stop.

**How to verify yourself:** hold the mock beside it — `docs/mocks/budget-shortcut-rail-mock.html`,
first phone frame. The button colour must match the page's own purple-blue, not contrast with it.

**Then report:** indigo or pink (A1).

---

## Step 3 — It opens to exactly three slots, in the right order

**Go to:** the same screen.

**Do:**
1. Press the round button once.
2. Count the circles that appear above it.
3. Read the label beside each circle, bottom to top.
4. Look at the main button.
5. Look at the page behind the circles.

**Expected:** 3 circles, stacking **upward**. Bottom to top: **Undo**, **Redo**,
**Change history**. Each label sits in a dark pill to the **left** of its circle. The main
button has become `×`. The page behind is dimmed.

**Do not:**
- Do not accept text printed **inside** the circles. The circle holds the icon; the words
  belong in the pill to its left. Text inside means the item template regressed.
- Do not expect a keyboard hint such as `Ctrl+Z` on the phone — it is hidden below desktop
  width on purpose (menunest-200). Its absence is correct.

**How to verify yourself:** Undo is the circle **closest to your thumb**. That is the whole
point of the order (menunest-191) — check it by reaching, not by reading.

**Then report:** the count and the order (A2, A3).

---

## Step 4 — The real test: assign money, then undo it

This step **writes to your real budget**. Step 0 is what makes it safe to do so.

**Go to:** the budget screen, your chosen **Envelope**.

**Do:**
1. Assign **100** to your chosen Envelope, using the Envelope's own `+` control.
2. Read the Envelope's Assigned figure. It must now be **V + 100**.
3. Read **Ready to Assign**. It must now be **R − 100**.
4. Press the rail.
5. Press **Undo**.
6. Read the Envelope's Assigned figure again.
7. Read **Ready to Assign** again.

**Expected after step 7:** Assigned is back to **V**. Ready to Assign is back to **R**.

**Do not:**
- Do not use the rail's Undo before checking that step 2 and 3 actually moved. If they did not,
  the assign failed and Undo has nothing to act on — a different fault with a different fix.
- Do not expect the money to be *restored from a copy*. Undo sends an **opposite assign**, not
  a rollback (menunest-193). The figure returning to V is the correct evidence; a new row in
  **Change history** is not a bug.

**How to verify yourself:** the two figures both returned to what you wrote in Step 0. If
Assigned returned but Ready to Assign did not, stop — that is a real defect, not a display lag.

**Then report:** V, V+100, and the figure after Undo (A4, A5).

---

## Step 5 — Change history lists the act, and marks it undone

**Go to:** the rail → **Change history**.

**Do:**
1. Count the rows. Compare with **C** from Step 0.
2. Find your assign.
3. Read who it says did it.
4. Read the button on that row.

**Expected:** **C + 1** rows. Your row names you. Because you already undid it, its button
reads **ทำซ้ำ** (redo), not ยกเลิก.

**Do not:**
- Do not expect the undone row to vanish. An undone row **stays** on the list so it can be
  redone (menunest-195). A missing row is the fault, not a present one.
- Do not read an empty sheet as "broken" without checking the date first — see the deadline
  box at the top. Empty on 1 September is correct behaviour.

**How to verify yourself:** the row count went up by exactly one, not two. Two would mean the
assign was recorded twice.

**Then report:** C and the new count (A6).

---

## Step 6 — Redo puts it back

**Go to:** the open **Change history** sheet.

**Do:**
1. Press **ทำซ้ำ** on your row.
2. Close the sheet.
3. Read the Envelope's Assigned figure.

**Expected:** **V + 100** again.

**Do not:**
- Do not leave the test here. Step 7 puts your budget back.

**Then report:** the figure you saw.

---

## Step 7 — Put your budget back

**Go to:** the rail.

**Do:**
1. Press **Undo**.
2. Read the Envelope's Assigned figure.
3. Read **Ready to Assign**.

**Expected:** **V** and **R** — exactly the numbers from Step 0.

**Do not:**
- Do not stop before this step. Steps 4 and 6 both leave 100 assigned; only this returns it.

**How to verify yourself:** both figures match your Step 0 notes character for character.

**Then report:** confirmed, or the difference.

---

## Step 8 — Hide on scroll

**Go to:** the budget screen.

**Do:**
1. Flick the list **downward**, hard.
2. Look at the bottom-right corner.
3. Stop moving. Wait about one second.

**Expected:** the rail slides away and fades out as you flick down. It comes back on its own
once you stop.

**Do not:**
- Do not judge this on a short page. If the screen does not scroll, the rail cannot hide, and
  that is not a fault. Add envelopes or use a month with more rows.

**Then report:** hides, and returns.

---

## Step 9 — Blast radius: the rail must NOT appear on the account screen

This is the check that catches over-reach. Nothing above would fail if the rail leaked here.

**Go to:** the budget screen → press an account card.

**Do:**
1. Look at the bottom-right corner.
2. Check the `+` button is still there.
3. Check there is no `⋮` rail.

**Expected:** the `+` button is present. The rail is absent.

**Do not:**
- Do not confuse the two. The `+` is the account screen's own control and has always been
  there. The rail is opt-in per page (menunest-199) and this page does not opt in — which is
  exactly why the two never had to fight over the corner.

**How to verify yourself:** one round button in that corner, not two.

**Then report:** present / absent (A7, A8).

---

## What is still owed after this

- **The two head-UI issues** are not created yet — see
  `docs/runbooks/2026-08-29-open-head-ui-issues.md`. Independent of this runbook.
- **The permission defect** (issue 2 in that file) **cannot be reproduced by this runbook**,
  because it needs a **Family** with two members and both prod families have one. Nothing you
  do here will surface it.
- **`CONTEXT.md` is stale in two places:** it has no entry for the family head, and its
  **Change history** entry still says "Which acts it lists, and how far back, is not yet
  decided" — menunest-194 and menunest-196 decided both.

## Record the outcome here

Write the result into this file and commit it. This file is the record; the chat is not.

| step | assertion | result | when (UTC) |
|---|---|---|---|
| 1 | commit is `a821517` | | |
| 2 | A1 indigo | | |
| 3 | A2 = 3, A3 order | | |
| 4 | A4, A5 return to V and R | | |
| 5 | A6 = C + 1 | | |
| 7 | back to V and R | | |
| 9 | A7 present, A8 absent | | |
