# MenuNest — Project Instructions

## Azure access — ALWAYS target the personal subscription explicitly

This machine is signed into **two** Azure identities, and the Azure MCP tools do
**not** use your terminal `az` session. They resolve identity through the
**Visual Studio / VS Code** signed-in account — which is the **work** account — so
Azure MCP calls default to the **wrong** subscription. Re-running `az login` or
clearing the cache does **NOT** fix this, because the MCP never looks at `az`.

|  | Work — DO NOT USE | menunest — USE THIS |
|---|---|---|
| Subscription | `AzureSubscriptionInALSO` | `Pay-As-You-Go` |
| Subscription ID | `fdd3b863-6504-40c0-ba81-e60e37df0d19` | `01473a32-351a-4cf5-9956-674d68e2ccbf` |
| Tenant ID | `2f17f859-d189-40a2-9452-c1e0dd9a0669` | `d500e2f4-1325-41d2-9f92-2f2f39e8ea19` |
| Account | `thodsaphon.sonthipin@cartagena.no` | `thodsaphonSP@hotmail.co.th` |

### The rule
On **every** Azure MCP call for menunest, pass BOTH parameters explicitly:

- `tenant`: `d500e2f4-1325-41d2-9f92-2f2f39e8ea19`
- `subscription`: `01473a32-351a-4cf5-9956-674d68e2ccbf`

With the `tenant` set, the credential chain falls through to the personal `az`
token and returns the correct subscription. Without it, you silently get the
**work** subscription.

### Gotchas
- `01473a32-…` is the **subscription** ID, NOT a tenant. `az login --tenant 01473a32-…`
  fails with `TS002: account not found in tenant`. The personal tenant is `d500e2f4-…`.
- Verify the terminal `az` session with `az account show` → expect
  `Pay-As-You-Go` / `thodsaphonSP@hotmail.co.th`. The terminal `az` and the MCP
  credential are **independent** — fixing one does not fix the other.
- Never create, modify, or incur cost in `AzureSubscriptionInALSO` (work). Read-only
  diagnostics on menunest's own resources only, in the personal sub.

## menunest Azure resources (Resource Group `MenuNest`, region southeastasia)
- App Service: `menunest` · Application Insights: `menunest` · Static Web App: `MenuNestWeb`
- SQL: server `menunest-sql.database.windows.net`, database `MenuNest` (Entra-only auth)

## Querying App Insights / logs — it is WORKSPACE-BASED
The `menunest` Application Insights is workspace-based (`ingestionMode: LogAnalytics`),
backed by Log Analytics workspace `DefaultWorkspace-01473a32-351a-4cf5-9956-674d68e2ccbf-SEA`
(RG `DefaultResourceGroup-SEA`, customerId `587ba1f6-9c1c-4c74-9f0e-4581f3f765a2`, 30-day retention).

- `az monitor app-insights query` (the *classic* API) returns **`[]`** here even when data
  exists — do NOT trust its empty result. Query the workspace directly instead:
  `az monitor log-analytics query --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf --workspace 587ba1f6-9c1c-4c74-9f0e-4581f3f765a2 --analytics-query "<KQL>"`
- Use the **workspace-schema** table names: `AppRequests`, `AppPageViews`, `AppDependencies`,
  `AppExceptions`, `AppTraces`, `AppEvents` (NOT `requests`/`pageViews`/`dependencies`).
- `log-analytics query` returns a **raw JSON array of rows** — read `-o json` directly; do
  NOT pass `--query "tables[0].rows"` (yields null, unlike `app-insights query`).
- Don't use KQL reserved words as column names (`kind` fails to parse — use `sessionKind`).
- Frontend browser telemetry (SPA `AppPageViews`/`AppDependencies`/`AppExceptions`, via
  `enableAutoRouteTracking`) AND backend `AppRequests` land in the SAME workspace.
## Database migrations are applied MANUALLY
Neither the app (Program.cs has **no** `db.Database.Migrate()`) nor the CD pipeline
(`.github/workflows/main_menunest.yml`) applies EF Core migrations. After adding a
migration you MUST apply it to the prod DB by hand — otherwise the deployed API
throws `Microsoft.Data.SqlClient.SqlException: Invalid object name '<Table>'` (HTTP
500, surfaced in the SPA as "An unexpected error occurred."):

```bash
cd backend
# AZURE_TOKEN_CREDENTIALS=AzureCliCredential is REQUIRED — without it, SqlClient's
# "Active Directory Default" picks the Visual Studio work account and the login fails
# against the personal-tenant SQL server. This forces it onto your personal `az` token.
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```

(Requires the terminal `az` session to be `thodsaphonSP@hotmail.co.th`, who is the SQL Entra admin.
 Prefer `dotnet ef migrations script --idempotent` to preview SQL before applying to prod.)

## Commit messages — ALWAYS reference the tracking ticket
Every commit MUST reference the GitHub issue it belongs to, so the code is
traceable back to the tracker. Keep the conventional-commit style already in the
log (`type(scope): summary`) and add the issue reference:

- **Closes the issue** → end the subject with `(closes #<n>)` (or a `Closes #<n>`
  body line). GitHub auto-closes the issue when the commit merges to `main`.
- **Only relates** (partial work, follow-up, or one of several commits) → use
  `(#<n>)` in the subject or a `Refs #<n>` body line — no auto-close.

Examples (from real history):
- `fix(trips): show route-map band on mobile/tablet itinerary (closes #8)`
- `docs(trips): ADR-026 + design spec/plan for map band (#8)`

If a change genuinely has no ticket, open the issue first — the default
expectation is that every commit maps to exactly one tracked item.

## Committing — a pre-commit hook runs the FULL suite; stage narrowly

`frontend/.husky/pre-commit` (`set -e`) runs, on **every** commit: backend
`dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` +
`npm run build` (~40s). The commit fails if anything is red — expect the wait;
do **not** `--no-verify` to skip it.

Because the hook runs the **whole** suite, every commit must leave the entire suite
green — a task's own filtered tests passing is not enough. In particular, adding an
EF entity/property and its EF configuration/mapping must land in the **same** commit:
an unmapped collection navigation (or otherwise invalid model) fails EF model
validation for **every** test that touches the `DbContext`, so an "entity now / mapping
next commit" split can never pass pre-commit (learned on #33).

Always `git add <explicit paths>` — **never** `git add -A` / `git add .`.
`daily-state.md` (tracked, usually dirty) and `AGENTS.md` (untracked) are
working files that must never be swept into a feature commit.

## Frontend has NO component/visual test harness

The SPA's vitest runs in `environment: 'node'` (see `frontend/vite.config.ts`) with **no**
jsdom / React-Testing-Library — only pure hook/lib/slice `*.test.ts` files exist. So
`tsc -b` + `npm run build` + the unit suite **cannot** catch rendering, layout, CSS, or
DOM-interaction bugs. A real example (#33): a popover was clipped invisibly by an
`overflow:hidden` ancestor — every automated gate passed. **Any UI change MUST be verified
interactively** (run the app / Chrome DevTools) or against an `docs/mocks/` visual mock
before it is considered done; extract pure logic into a `lib/` module to get real vitest
coverage for the parts that can be unit-tested.
