# Runbook — probe how far ahead WorldTides can predict

Tracking issue: **#146** (decision map #138) · source decision at risk: **#139**

**Date measured:** 2026-09-06
**Boundary:** Claude measured the public WorldTides API, this repo, the GitHub Actions secrets and
the Azure App Service configuration — all read-only. Claude has **no** WorldTides account and cannot
create one. **Every step below is the operator's.** Waiting for Claude to do it is not an option.

## Why this exists

WorldTides was chosen over the Thai Hydrographic Department tables on **#139** for one reason: the
Thai HD tables stop at **31 December** of the current year, and a **Trip** is often planned past that
date. That reason is **load-bearing and untested** — no page on worldtides.info states a maximum
future date (verified 2026-09-06 against `/apidocs` and `/developer`; both are silent).

If the probe fails, **#139 must be re-decided.** Do not work around it. The Thai HD tables become
the primary source again, and their reuse-licence question comes back with them.

---

## Baseline (measured live 2026-09-06, not taken from any document)

### WorldTides API — what an unauthenticated call returns

Measured by Claude, no account involved:

| Request | HTTP | Body |
|---|---|---|
| no `key` at all | `400` | `{"status":400,"error":"API key is invalid"}` |
| `key=notarealkey` | `400` | `{"status":400,"error":"API key is invalid"}` |

Both cases produce the **same** string. This matters: `API key is invalid` after Step 1 means a key
problem, and tells you **nothing** about the date. That is why Step 2 (the control) exists.

### WorldTides published limits (from `/apidocs`, 2026-09-06)

- `heights` — **1 credit for every 7 days** of data at 30-minute intervals
- `extremes` — **1 credit for every 7 days**
- New account — **100 free credits** (`/developer`)
- Maximum future `date` — **not documented anywhere.** The only range limit published is
  `1 to 7 days`, and it applies solely to `plot`, not to `heights` or `extremes`.

This whole runbook costs **2 credits** on the happy path, **6** in the worst case. Out of 100.

### Blast radius — what must NOT change

| Thing | Baseline count | Measured how |
|---|---|---|
| App Service `menunest` app settings | **26** | `az webapp config appsettings list` |
| GitHub Actions secrets | **12** | `gh secret list` |
| Files in this repo naming `worldtides` | **0** outside `docs/adr/` | `grep -ril` |

Full app-setting name list at baseline (names only — no values were read):

```
APPLICATIONINSIGHTS_CONNECTION_STRING  AZURE_CLIENT_ID
ApplicationInsightsAgent_EXTENSION_VERSION
AzureAd__Audience  AzureAd__ClientId  AzureAd__ClientSecret  AzureAd__Instance  AzureAd__TenantId
Cors__AllowedOrigins  GoogleMaps__ApiKey  Google__ClientId  Google__ClientSecret
Jwt__SigningKey  MCP__ServerUrl
Push__VapidPrivateKey  Push__VapidPublicKey  Push__VapidSubject
QrTokenSigningKey  Share__BaseUrl  Share__TokenSigningKey
Storage__BlobEndpoint  Storage__DrugImagesContainer  Storage__EpisodeImagesContainer
VapidPrivateKey  VapidPublicKey  WEBSITE_RUN_FROM_PACKAGE
```

None of them mentions tide or WorldTides. **This probe adds nothing to that list.** Wiring the key
into the app is a later, separate, tracked piece of work — see the "Do not" in Step 1.

---

## Assertions — declared BEFORE anything is run

| # | Assertion | Pass looks like |
|---|---|---|
| A1 | The key works at all | Step 2 returns HTTP `200` and `"status":200` |
| A2 | The station is a real one near Phuket | Step 2 body carries a `station` name and `responseLat`/`responseLon` within ~0.5° of 7.83/98.42 |
| A3 | **The far date is accepted** | Step 3 returns HTTP `200` and `"status":200` |
| A4 | **The far date returns real tide, not flat filler** | Step 3 min→max height spread is within roughly ±50% of the Step 2 spread — a tide that swings, not a straight line |
| A5 | Turn times survive the far date too | Step 4 returns `200` with `extremes` entries alternating `High` / `Low` |
| B1 | App Service untouched | still **26** app settings, same names |
| B2 | GitHub secrets untouched | still **12** secrets, same names |
| B3 | No key in the repo | `grep -ri worldtides` finds nothing outside `docs/` |

**A4 is the one a status check would miss.** A tide model is a formula; it can compute any date you
give it. The dangerous failure is not an error — it is a `200` full of zeros or a flat line. Only
comparing the swing against a known-good near date catches that.

---

## Step 1 — Get a WorldTides key

**Go to:** https://www.worldtides.info/register

**Do:**
1. Create a free account.
2. If a confirmation email arrives, click the link in it.
3. Open your account page and copy the API key.
4. In your terminal, load the key into a variable **without typing it on screen**:
   ```zsh
   read -rs "WT_KEY?Paste the WorldTides key, then press Enter: "
   ```
   Nothing appears as you paste. That is correct.

**Do not:**
- Do not paste the key into this chat — Claude does not need it, and a chat log is not a vault.
- Do not `export WT_KEY=<key>` on the command line — that writes the key into `~/.zsh_history`.
- Do not add the key to the App Service settings or to GitHub secrets yet — this probe is a
  question, not an implementation. Wiring it in before #146 is answered would put a live secret in
  prod for a source we may still have to abandon (see #139).

**How to verify yourself:** `echo ${#WT_KEY}` prints a number bigger than 0. It prints the
**length**, not the key.

**Then report:** nothing yet. Go to Step 2.

---

## Step 2 — Control probe: prove the key works, and learn the normal tide swing

Order matters: this must run **before** Step 3. Without it, a failure in Step 3 cannot be told apart
from a dead key — and Step 1 showed both produce the same message.

**Go to:** your terminal, same shell session as Step 1.

**Do:**
1. Run:
   ```zsh
   curl -s "https://www.worldtides.info/api/v3?heights&date=today&days=1&lat=7.83&lon=98.42&key=$WT_KEY" \
     > ~/wt-control.json
   ```
2. Summarise it:
   ```zsh
   python3 -c "import json;d=json.load(open('$HOME/wt-control.json'));h=[x['height'] for x in d.get('heights',[])];print('status',d.get('status'),'| error',d.get('error'),'| station',d.get('station'),'| respLat',d.get('responseLat'),'respLon',d.get('responseLon'),'| points',len(h),'| min',min(h) if h else None,'max',max(h) if h else None,'| callCount',d.get('callCount'))"
   ```

**Do not:**
- Do not skip this because "the key is obviously fine". This line is the only reference swing you
  will have to judge Step 3 against.

**How to verify yourself:** the summary prints `status 200`, a `station` name, `points 48`, and a
`min`/`max` that are different numbers.

If it prints `API key is invalid`: the key is not live yet. WorldTides does not publish whether a
new key activates instantly — **Claude could not measure this.** Wait a few minutes, confirm your
email, and re-run. Do **not** conclude anything about dates from this message.

**Then report:** paste the one summary line into the chat, and record it on **#146**. It contains no
key.

---

## Step 3 — The actual question: a date 21 months out

**Go to:** your terminal, same shell session.

**Do:**
1. Run:
   ```zsh
   curl -s "https://www.worldtides.info/api/v3?heights&date=2028-06-15&days=7&lat=7.83&lon=98.42&key=$WT_KEY" \
     > ~/wt-horizon.json
   ```
2. Summarise it with the same line as Step 2, but reading `wt-horizon.json`:
   ```zsh
   python3 -c "import json;d=json.load(open('$HOME/wt-horizon.json'));h=[x['height'] for x in d.get('heights',[])];print('status',d.get('status'),'| error',d.get('error'),'| station',d.get('station'),'| points',len(h),'| min',min(h) if h else None,'max',max(h) if h else None,'| callCount',d.get('callCount'))"
   ```

**Do not:**
- Do not accept `status 200` on its own as a pass. Check the `min` and `max` differ by roughly the
  same amount as Step 2. A flat line at `200` is a failure wearing a success costume.
- Do not change the date to something nearer to "make it work". The far date is the point.

**How to verify yourself:** compare the two summary lines side by side. Same station, `points 336`,
and a `max - min` spread close to Step 2's.

**Then report:** paste both summary lines into the chat, and record them on **#146**.

---

## Step 4 — Only if Step 3 passed: do turn times reach that far too?

**Tide reading** on the **StopDetailSheet** shows turn times, and those come from `extremes`, not
`heights` (menunest-233). Step 3 proves nothing about `extremes`.

**Go to:** your terminal, same shell session.

**Do:**
1. Run:
   ```zsh
   curl -s "https://www.worldtides.info/api/v3?extremes&date=2028-06-15&days=7&lat=7.83&lon=98.42&key=$WT_KEY" \
     > ~/wt-extremes.json
   ```
2. Summarise:
   ```zsh
   python3 -c "import json;d=json.load(open('$HOME/wt-extremes.json'));e=d.get('extremes',[]);print('status',d.get('status'),'| error',d.get('error'),'| n',len(e),'| first',e[0] if e else None,'| types',sorted({x['type'] for x in e}))"
   ```

**How to verify yourself:** `status 200`, `n` around 26–28 for 7 days, and `types` is
`['High','Low']`.

**Then report:** paste the summary line into the chat and onto **#146**.

---

## Step 5 — Only if Step 3 FAILED: find the wall

Do this only when Step 3 returned an error while Step 2 succeeded. The goal is to answer two things:
does WorldTides reach past **2026-12-31** at all, and where exactly does it stop.

**Do:** run Step 3 again, once for each date below, **stopping at the first one that succeeds**:

1. `2027-06-15`
2. `2027-01-15`
3. `2026-12-31`

**Do not:**
- Do not run all three at once. Stop at the first success — that brackets the wall and saves credits.

**Then report:** the last date that failed and the first that succeeded, onto **#146**.

**What it means:** if even `2026-12-31` fails, WorldTides gives us nothing the Thai HD tables do not
already give, and the reason #139 chose it is void.

---

## Step 6 — Blast-radius check (Claude runs this; read-only)

After you report, Claude re-measures B1–B3 and states before → after. Nothing in this runbook should
move any of them; the check exists because "add the key while I'm in there" is the natural next
click, and it is out of scope for #146.

---

## What could NOT be measured

- **Whether a new WorldTides key is live immediately.** Neither `/apidocs` nor `/developer` says.
  Step 2 is written so that this uncertainty cannot be mistaken for a date failure.
- **What a date-out-of-range error looks like.** WorldTides publishes no error vocabulary at all —
  `/apidocs` only promises "a stable error string". Claude has no key and cannot provoke one. So the
  failure signal in Step 3 is defined negatively: **any** `error` string other than
  `API key is invalid`, after Step 2 has already succeeded.
- **Whether free credits expire.** Not published. If the account is old by the time you run this,
  a credit failure may masquerade as something else — `callCount` in every summary line is there to
  catch that.
- **Data quality at the far date.** A `200` with a plausible swing proves the API answered. It does
  not prove the prediction is accurate 21 months out. No public source can settle that; only
  comparison against observed water levels could, and that is not this ticket.

## Rollback

Nothing here changes any system. If you want to undo it entirely: delete the WorldTides account, and
remove `~/wt-control.json`, `~/wt-horizon.json`, `~/wt-extremes.json`. None of those files contains
the key.
