# Runbook — restore the Google Maps API keys (both surfaces)

Tracking issue: #136 · follow-up on logging: #137

**Date measured:** 2026-09-05
**Boundary:** measured read-only by Claude against the live systems. Every change below is the
operator's; Claude holds `gcloud`/`az` credentials and deliberately did not write.
**Symptom that started this:** "cannot resolve place in Google Maps" — a **Capture** by pasted link
returns "Could not look up that place right now."

## Baseline (measured live 2026-09-05, not taken from any document)

### Two faults, two different Google projects

| | Backend (App Service) | SPA (Static Web App) |
|---|---|---|
| Key source | `GoogleMaps__ApiKey` app setting | GitHub secret `VITE_GOOGLE_MAPS_BROWSER_KEY` (build-time) |
| Key sha256 (first 16) | `29646ada9a4766e8` | `f17e3f2670e33fb6` |
| Google project | `gmp-demo-project-425941023` / **483442944628** | `my-web-a47f8` / **583496789414** |
| Google verdict | `403 API_KEY_HTTP_REFERRER_BLOCKED` | `403 SERVICE_DISABLED` (Places API New off) |
| Cause | a **browser** key used as a **server** key; server sends no `Referer` | key from an unrelated leftover project |

### Project 483442944628 (`Maps Platform Demo Project`) — backend's project
- Billing: **enabled** (`billingAccounts/01CBE1-875191-C4C75E`)
- Enabled: `geocoding-backend`, `maps-backend`, `mapstools`, `places`, `routes`, `weather`
- API keys: **1**
  - `Maps Platform Demo API Key`, uid `443879f6-73b1-4496-b1e9-c6518d71844c`
  - referrers: `https://menunestweb.azurestaticapps.net/*`, `https://*.azurestaticapps.net/*`,
    `http://localhost:*`, `http://127.0.0.1:*`
  - apiTargets: 8 services
  - **`updateTime` = 2026-08-23T01:48:16Z** — first 403 in telemetry 2026-08-23T05:45:33Z

### Project 583496789414 (`my-web`) — SPA's project
- Billing: enabled (same account)
- Enabled: `maps-backend` (so the map still renders), `places-backend` (**legacy**),
  `geocoding-backend`, static/embed/android/ios
- **NOT enabled:** `places.googleapis.com` (Places API New), `routes`, `weather`
- API keys: 1 — `Maps API Key`, uid `06f1645d-7ba3-46ed-997f-ca3462439f56`, same 4 referrers

### Azure App Service `menunest`
- `GoogleMaps__ApiKey` proven byte-identical to project 483442944628's key (sha256 match)
- 26 app settings total (verified by name diff); only `GoogleMaps__ApiKey` is in scope
- Outbound IPs — **32 possible** (allowlist all of them):
```
4.144.139.96,4.144.140.104,4.144.137.170,4.144.138.86,4.144.138.35,4.144.137.167,
4.144.137.160,4.144.137.171,4.144.137.202,4.144.139.109,4.144.139.115,4.144.139.132,
4.144.139.189,4.144.139.191,4.144.138.104,4.144.140.15,4.144.140.27,4.144.140.66,
4.144.140.75,4.144.140.100,4.144.140.107,4.144.140.113,4.144.140.119,4.144.139.50,
4.144.137.190,4.144.137.137,4.144.137.164,4.144.137.215,4.144.138.142,4.144.138.235,
20.212.64.17,20.212.64.31
```
- `VITE_GOOGLE_MAPS_MAP_ID` resolves to the literal `DEMO_MAP_ID` in the shipped bundle.
  `DEMO_MAP_ID` is **not** project-scoped, so moving the browser key between projects is safe.

### Server-side last-good days (AppInsights `AppDependencies`, `HTTP` rows = server)
| Google service | Feeds | Last 200 |
|---|---|---|
| `places.googleapis.com` | **Place** data for a **Capture** | 2026-08-12 |
| `routes.googleapis.com` | travel time for a **Leg** | 2026-08-15 |
| `weather.googleapis.com` | each **Weather reading** on a **Stop** | 2026-08-17 |

## Rollback
No secret is stored in this document. To roll back, re-read the old value at any time:
```
gcloud services api-keys get-key-string \
  projects/483442944628/locations/global/keys/443879f6-73b1-4496-b1e9-c6518d71844c
```
and set `GoogleMaps__ApiKey` back to it. The existing keys are not modified or deleted by this runbook.

## Assertions — declared BEFORE the change

**Success**
- A1 server key, no `Referer`, → `places.googleapis.com` : `403` → **`200`**
- A2 same for `routes.googleapis.com` and `weather.googleapis.com` : `403` → **`200`**
- A3 SPA live search → `places.googleapis.com` : `403` → **`200`**

**Blast radius — must NOT change**
- B1 project 483442944628 key count: `1` → `2` (exactly one added)
- B2 existing key `443879f6-…` untouched: still 4 referrers, 8 apiTargets, `updateTime` unchanged
- B3 `maps.googleapis.com` browser calls stay `200` (the map keeps rendering)
- B4 App Service: 26 settings, names identical, only `GoogleMaps__ApiKey` value differs
- B5 project `my-web` (583496789414): unchanged, no keys added or removed

---

# Steps

Do one step, verify it, then start the next. Do not batch.

## Step 1 — Create a server key in project 483442944628

**Go to:** https://console.cloud.google.com/apis/credentials?project=gmp-demo-project-425941023

**Do:**
1. Click **+ CREATE CREDENTIALS**, then **API key**.
2. Click **Edit API key** on the new key.
3. Set **Name** to `MenuNest server key`.
4. Under **Application restrictions**, select **IP addresses**.
5. Add all 32 outbound IP addresses listed in the Baseline above.
6. Under **API restrictions**, select **Restrict key**.
7. Select exactly these three: **Places API (New)**, **Routes API**, **Weather API**.
8. Click **Save**.
9. Copy the key string. Keep it for Step 2.

**Do not:**
- Do not edit `Maps Platform Demo API Key` — the SPA uses it, and changing its referrers
  is what broke the backend on 23 Aug.
- Do not choose **HTTP referrers** for this key — the server sends no `Referer`, which is
  the whole fault being fixed.
- Do not leave **Application restrictions** as **None** — the key would then be usable by
  anyone who obtains it, and it is billed to your account.

**How to verify yourself:** you **cannot** get a `200` from your own machine. The key is
restricted to the App Service IP addresses, so any call from a laptop is refused by design.
What you check instead is *which* refusal Google gives. Run this and read the `reason`:
```
curl -s -X POST "https://places.googleapis.com/v1/places:searchText" \
  -H "Content-Type: application/json" -H "X-Goog-Api-Key: <NEW_KEY>" \
  -H "X-Goog-FieldMask: places.id" -d '{"textQuery":"Wat Arun"}'
```
| `reason` you see | What it means |
|---|---|
| `API_KEY_IP_ADDRESS_BLOCKED` | **Correct.** The key is a server key. It refuses you because you are not the App Service. |
| `API_KEY_HTTP_REFERRER_BLOCKED` | Wrong. You created another browser key. Redo the restriction. |
| `SERVICE_DISABLED` | Wrong project, or the API is not enabled. |
| `200` | The key has no IP restriction. Add one — an open key is billed to you. |

The success assertion `403 → 200` is **not** checkable here. It is checked in Step 2, when the
App Service itself calls Google from an allowed address.

**Then report:** paste the status code here, or record it on the ticket.

## Step 2 — Point the App Service at the new key

Do this only after Step 1 prints `200`.

**Go to:** https://portal.azure.com → subscription `Pay-As-You-Go` → resource group `MenuNest`
→ App Service `menunest` → **Settings** → **Environment variables**

**Do:**
1. Find `GoogleMaps__ApiKey`.
2. Replace its value with the new server key from Step 1.
3. Click **Apply**, then **Confirm**.
4. Wait for the app to restart. This takes about 30–60 seconds.

**Do not:**
- Do not change any other setting. The 25 others hold live secrets.
- Do not skip the restart wait — the app reads this value at startup, so a request sent
  during the restart still uses the old key and still fails.

**How to verify yourself:** open the app, open a **Trip**, and **Capture** a **Place** by
pasting a Google Maps link. It must show the preview instead of "Could not look up that place
right now."

**Then report:** say whether the preview appeared. Claude measures App Insights independently.

## Step 3 — Point the SPA at the demo project's browser key

This fixes **live search** and **map-tap** **Capture**, which fail for a different reason
(`SERVICE_DISABLED` in project `my-web`).

**Go to:** https://github.com/ThodsaphonSonthiphin/MenuNest/settings/secrets/actions

**Do:**
1. Get the demo project's browser key string:
   `gcloud services api-keys get-key-string projects/483442944628/locations/global/keys/443879f6-73b1-4496-b1e9-c6518d71844c`
2. Edit the secret `VITE_GOOGLE_MAPS_BROWSER_KEY`.
3. Paste that key string. Save.
4. **Re-run the last successful deploy.** The workflow has **no `workflow_dispatch` trigger**,
   so there is no "Run workflow" button and `gh workflow run` fails. Re-run the previous run
   instead — GitHub reads secrets at run time, so a re-run picks up the new value:
   `gh run rerun 33867700917`
   (or Actions → the 2026-09-04 `main` run → **Re-run all jobs**)
5. Wait for the run to finish green.

**Do not:**
- **Do not stop after saving the secret.** `VITE_*` values are compiled into the bundle at
  build time. Saving the secret changes nothing that a browser can see until a new build ships.
  This is the single most likely way this step is left half-done.
- Do not enable Places API (New) in `my-web` instead. That works, but it leaves MenuNest
  spread over two Google projects and two sets of quotas for no benefit.

**How to verify yourself:** hard-reload the app, then type a place name into live search.
Suggestions must appear.

**Then report:** say whether suggestions appeared, and record it on the ticket.

## Step 4 — Claude verifies in a second channel

Report Steps 1–3 done. Claude then measures **App Insights**, not the UI you just used, and
reports before → after for A1–A3 and B1–B5.

---

# Result — measured 2026-09-05 09:13Z

All three steps done by the operator. Verified by Claude in a second channel (App Insights
`AppDependencies` + live GCP/SWA reads), not in the UI used to make the changes.

## Success assertions

| | Check | Before | After | |
|---|---|---|---|---|
| A1 | server → `places.googleapis.com` | 403 | **not exercised** | ⚠️ see below |
| A2 | server → `routes.googleapis.com` | 403 | **200** | ✅ |
| A2 | server → `weather.googleapis.com` | 403 | **200** | ✅ |
| A3 | SPA → `places.googleapis.com` (`AutocompletePlaces`) | 403 | **200** | ✅ |

## Blast radius

| | Check | Result | |
|---|---|---|---|
| B1 | key count in 483442944628 | 1 → 2, exactly one added | ✅ |
| B2 | `Maps Platform Demo API Key` untouched | `updateTime` 2026-08-23T01:48:16.064620Z, 4 referrers, 8 targets — all unchanged | ✅ |
| B3 | `maps.googleapis.com` browser calls | still 200 (25 calls); the **map** still draws | ✅ |
| B4 | App Service settings | 26 → 26, names identical by diff | ✅ |
| B5 | project `my-web` (583496789414) | 1 key, unchanged; nothing added or removed | ✅ |

Deploy evidence: bundle `index-EB5hTOiq.js` → `index-CwBm32oE.js`; shipped key sha256
`f17e3f2670e33fb6` (my-web) → `29646ada9a4766e8` (demo project browser key).

## Still owed

**A1 is not verified.** No server-side call to `places.googleapis.com` has been made since the
key change, so the paste-a-link **Capture** path is *inferred* working, not measured. It uses the
same key and IP allowlist as Routes and Weather, which both return 200, and `places` is in the
key's API targets — but that is reasoning, not evidence. To close it: **Capture** one **Place**
by pasting a Google Maps link, then re-run:

```
az monitor log-analytics query --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --workspace 587ba1f6-9c1c-4c74-9f0e-4581f3f765a2 \
  --analytics-query "AppDependencies | where TimeGenerated > ago(30m) and Target=='places.googleapis.com' and DependencyType=='HTTP' | project TimeGenerated, ResultCode"
```

## Known future break

The new server key is restricted to the App Service's 32 `possibleOutboundIpAddresses`.
**Changing the App Service plan tier or scale changes those addresses** and will reproduce this
outage as `API_KEY_IP_ADDRESS_BLOCKED`. Re-read the IPs and update the key if the plan changes.

## Corrections made to this runbook while executing it

1. Step 1's check said "expect `200`" from a laptop curl. Impossible against an IP-restricted
   key. The real signal is the `reason` changing to `API_KEY_IP_ADDRESS_BLOCKED`.
2. Baseline said 28 App Service settings; the true count is 26 (a warning line was miscounted).
3. Step 3 said to click "Run workflow". The workflow has no `workflow_dispatch` trigger, so that
   button does not exist — re-run the previous run instead.
