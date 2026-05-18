# Phase 2 Playwright Coverage — Health Module Design

**Date:** 2026-05-18
**Status:** Awaiting User Review
**Approach:** Per-story spec files + fixture pattern + per-domain mock JSON (Approach B)

## Goal

Expand the Playwright end-to-end test coverage for the migraine-tracker health module from **5 partially-covered user stories (10 tests)** to **9 fully-covered user stories (~40 tests)**, with **3 push-related stories explicitly deferred to Phase 3**. Stay within the existing mock-only execution model (no real backend, no MSAL, no push delivery).

The Pay-As-You-Go Azure subscription is currently disabled, so spinning up the .NET backend + Azure SQL + VAPID push server for true end-to-end tests is not possible. This design maximises coverage achievable under that constraint and explicitly documents the gap that requires real infrastructure (Phase 3).

## Scope

### In Scope (9 spec files, ~40 tests)

| # | User story | Spec file | New / Existing |
|---|---|---|---|
| 1 | Wiring, PWA, auth boundary | `health.smoke.spec.ts` | Existing (5 tests, kept) |
| 2 | Quick Log attack | `health.quick-log.spec.ts` | Expand (5 tests) |
| 3 | Active Episode screen | `health.active-episode.spec.ts` | New (5 tests) |
| 4 | Take Medication + 3-category logic | `health.take-medication.spec.ts` | Expand (6 tests) |
| 5 | History list + filter | `health.history.spec.ts` | New (3 tests) |
| 6 | Episode Detail | `health.episode-detail.spec.ts` | New (4 tests) |
| 7 | Drug Master CRUD + photo upload | `health.drug-master.spec.ts` | New (5 tests) |
| 8 | Doctor Report happy path | `health.doctor-report.spec.ts` | New (4 tests) |
| 9 | Settings (excl. real push toggle) | `health.settings.spec.ts` | New (3 tests) |

**Test depth target:** Each story covers Happy → Negative → Business Rules. Critical rules (e.g. Take-Med 3-category categorisation, MOH risk thresholds) are asserted explicitly, not just rendered.

### Out of Scope — deferred to Phase 3

| Story | Reason it requires Phase 3 |
|---|---|
| Follow-up ping +30 min dispatcher | Needs `BackgroundService` running + VAPID server |
| Notification 0-tap response | Needs real `notificationclick` event + push delivery |
| Retro-close modal (missed 3 pings) | Needs persistent state across close-then-reopen sessions |

Phase 3 will be unblocked when:
- Pay-As-You-Go subscription reactivates → backend deployable to staging
- Test user credentials provisioned for Google OAuth + MSAL test app registration
- Playwright tests can use `context.grantPermissions(['notifications'])` + web-push library to fire real pushes

## Architecture

### File layout

```
frontend/e2e/
├─ health.smoke.spec.ts                ← existing, kept as-is
├─ health.quick-log.spec.ts            ← Story 2
├─ health.active-episode.spec.ts       ← Story 3
├─ health.take-medication.spec.ts      ← Story 4 (Task 8 in tracker plan — critical)
├─ health.history.spec.ts              ← Story 5
├─ health.episode-detail.spec.ts       ← Story 6
├─ health.drug-master.spec.ts          ← Story 7
├─ health.doctor-report.spec.ts        ← Story 8
├─ health.settings.spec.ts             ← Story 9
│
├─ fixtures/
│  └─ healthFixture.ts                 ← test.extend() with authedPage + mockApi + capturedRequests
│
├─ helpers/
│  ├─ healthTestUtils.ts               ← existing — applyGoogleAuth, buildGoogleToken kept
│  ├─ mockRoutes/
│  │  ├─ index.ts                      ← composes per-domain mocks into createMockApi(page)
│  │  ├─ episodeRoutes.ts              ← /api/episodes/*
│  │  ├─ drugRoutes.ts                 ← /api/drugs/* + SAS endpoint
│  │  ├─ reportRoutes.ts               ← /api/reports/*, /api/share/:token
│  │  └─ settingsRoutes.ts             ← /api/me, /api/push/*
│  └─ assertions.ts                    ← shared expectations (toast, error state, loading)
│
└─ mocks/
   ├─ episodes/                         ← per-domain JSON fixtures
   ├─ drugs/
   ├─ reports/
   └─ contexts/                         ← all-takeable.json, mixed.json, all-active.json, all-blocked.json
```

### Fixture pattern

Replace per-test manual setup with Playwright fixtures. A test no longer calls `applyGoogleAuth` and `mockHealthApiRoutes` explicitly — it requests `authedPage` and `mockApi` from the fixture.

```ts
// fixtures/healthFixture.ts
type Fixtures = {
  authedPage: Page                                          // page with Google auth injected
  mockApi: ReturnType<typeof createMockApi>                 // chainable per-domain mock builders
  capturedRequests: RequestCapture                          // shared capture for payload assertions
}

export const test = base.extend<Fixtures>({
  authedPage: async ({ page }, use) => {
    await applyGoogleAuth(page)
    await use(page)
  },
  mockApi: async ({ page }, use) => use(createMockApi(page)),
  capturedRequests: async ({}, use) => use(createCapture()),
})
```

### Mock builder API

Per-domain builders are chainable and terminate with `.apply()` which registers the routes on the page.

```ts
// helpers/mockRoutes/episodeRoutes.ts
export const createEpisodeMocks = (page: Page) => {
  const config = { /* defaults */ }
  const self = {
    list: (data?) => { config.listResponse = data ?? defaultEpisodes; return self },
    active: (data?) => { config.activeResponse = data ?? activeEpisode; return self },
    detail: (id, data?) => { /* ... */ return self },
    takeMedicationContext: (variant: 'all-takeable' | 'mixed' | 'all-blocked' | 'all-active') => {
      config.takeMedContext = readMock(`contexts/${variant}.json`); return self
    },
    startSuccess: (data?) => { /* ... */ return self },
    startFails: (status, body?) => { /* ... */ return self },
    apply: async () => { await page.route(/* ... */); },
  }
  return self
}
```

Example usage in a test:

```ts
test('3-category render — mixed', async ({ authedPage, mockApi }) => {
  await mockApi.episodes
    .active()
    .takeMedicationContext('mixed')
    .apply()

  await authedPage.goto('/health/take-med/episode-1')

  await expect(authedPage.getByTestId('active-drugs')).toHaveCount(1)
  await expect(authedPage.getByTestId('takeable-drugs')).toHaveCount(2)
  await expect(authedPage.getByTestId('blocked-drugs')).toHaveCount(1)
  await expect(authedPage.getByTestId('blocked-drug-reason'))
    .toContainText('ถึง max daily dose')
})
```

### Mock JSON variants

Stored as files under `mocks/<domain>/`. The `contexts/` directory holds the 4 variants of take-medication context that drive Story 4's business-rule assertions.

### Payload assertion pattern

Replace the Phase 1 callback option (`onStartEpisodeRequest`) with a shared capture fixture that any test can query:

```ts
test('quick log payload', async ({ authedPage, mockApi, capturedRequests }) => {
  await mockApi.episodes.startSuccess().apply()
  await authedPage.goto('/health/log')
  await authedPage.getByRole('button', { name: /บันทึก attack/ }).click()

  const startRequest = await capturedRequests.waitFor('POST', '/api/episodes')
  expect(startRequest.body).toMatchObject({ symptomId: 'symptom-migraine', severity: 7 })
})
```

## Test Inventory

### `health.smoke.spec.ts` — Existing, kept (5 tests)
- public share page handles invalid token gracefully
- app renders without crashing for anonymous user
- login page renders
- service worker registers on first load
- manifest.json is reachable and well-formed

### `health.quick-log.spec.ts` — Expand (5 tests)
- (kept) logs attack → active episode
- (kept) disables save when symptoms empty
- (kept) API 500 error surfaces
- (new) severity slider 1-10 value persists in payload
- (new) optional fields (triggers, notes) — null vs filled both work

### `health.active-episode.spec.ts` — New (5 tests)
- renders timer + drug progress bars
- "กินยาเพิ่ม" button navigates to `/health/take-med/:id`
- "Resolved" button patches episode status, redirects to history
- "Worse" button opens severity re-rate dialog
- 404 (stale episode id) renders not-found state

### `health.take-medication.spec.ts` — Expand (6 tests, critical)
- (kept) logs intake + toast
- (kept) offline/abort error
- (new) 3-category render — `mixed` variant: active in top box, takeable in middle, blocked in bottom with reason text
- (new) blocked drug button is disabled + tooltip shows reason (recently taken, max daily dose)
- (new) active drug shows countdown to next dose
- (new) "ดูยาทั้งหมด" expand renders Drug Master items inline

### `health.history.spec.ts` — New (3 tests)
- renders list, pagination loads next page
- filter by date range — API called with correct query param
- filter by outcome (resolved/missed/active) — list updates

### `health.episode-detail.spec.ts` — New (4 tests)
- renders timeline (start → intakes → ping responses → end)
- edit severity triggers PATCH with payload
- delete confirm dialog → DELETE + redirect
- stale episode (404) renders graceful error

### `health.drug-master.spec.ts` — New (5 tests)
- list drugs + create new drug (form submit)
- update drug name/dose
- soft delete (deactivate) — drug disappears from takeable list
- photo upload happy path — mock SAS endpoint response → blob URL preview
- photo upload negative — SAS failure → error state

### `health.doctor-report.spec.ts` — New (4 tests)
- (kept smoke) invalid token error
- (new) valid token renders all 5 sections: MOH risk, trigger correlation, efficacy table, patterns, timeline
- (new) empty data state (0 episodes) safe render, no crash
- (new) MOH risk = high → red warning band visible

### `health.settings.spec.ts` — New (3 tests)
- renders current push permission state (granted/denied/default)
- revoke share link button → DELETE + UI updates
- revoke confirm dialog cancel → no API call

## Execution Model

### Parallelism

Change [`playwright.config.ts`](../../../frontend/playwright.config.ts):
```ts
fullyParallel: false,                            // intra-file: serial (SW + auth state shared)
workers: process.env.CI ? 4 : 2,                 // inter-file: parallel
```

- Browser context is fresh per worker → service-worker registrations do not collide
- Auth state is applied per test via fixture (not shared cross-file)
- Expected total runtime: **~50–60s on CI** (vs ~200s if serial)

### Timeouts

- Default 30s per test is sufficient
- Photo upload test overrides: `test.setTimeout(60_000)` for the upload happy-path

### Test data isolation

Each test starts with: fresh browser context, freshly registered mock routes via `mockApi.*.apply()`, empty sessionStorage. The fixture guarantees this without per-test boilerplate.

### CI workflow

[`.github/workflows/playwright.yml`](../../../.github/workflows/playwright.yml) already handles Phase 2 needs after the recent changes:
- Artifacts upload `playwright-report` + `playwright-test-results` on always()
- Screenshot / trace / video are now captured on every run

One optional optimisation: add Playwright browser cache step to save ~40s per run:
```yaml
- uses: actions/cache@v4
  with:
    path: ~/.cache/ms-playwright
    key: playwright-${{ runner.os }}-${{ hashFiles('frontend/package-lock.json') }}
```

### Pre-commit hook

[`frontend/.husky/pre-commit`](../../../frontend/.husky/pre-commit) does not run Playwright and remains unchanged. Local commits stay fast.

## Migration Order

Implementation proceeds in 4 batches so each can be reviewed and committed independently.

### Batch 1: Foundation (no impact on existing tests)
1. Create `fixtures/healthFixture.ts`
2. Create `helpers/mockRoutes/` skeleton + one builder (`episodeRoutes.ts`)
3. Create `mocks/episodes/` and `mocks/contexts/` with JSON variants
4. Write first new spec — `health.active-episode.spec.ts` to validate the pattern

### Batch 2: High-value stories
5. `health.take-medication.spec.ts` (3-category critical)
6. `health.doctor-report.spec.ts` (happy path)
7. `health.episode-detail.spec.ts`

### Batch 3: CRUD + listing
8. `health.drug-master.spec.ts` (including mock SAS endpoint)
9. `health.history.spec.ts`
10. `health.settings.spec.ts`
11. Expand `health.quick-log.spec.ts`

### Batch 4: Migrate Phase 1 + cleanup
12. Migrate `health.functional.spec.ts` + `health.negative.spec.ts` to the new fixture
13. Remove `mockHealthApiRoutes` (the old monolithic helper)
14. Update config: `workers: 4`, optional browser-cache CI step
15. Add Phase 3 placeholder comment listing deferred stories

## Decisions Made

These were resolved during brainstorming and are settled, not open questions:

| Decision | Choice | Rationale |
|---|---|---|
| Phase 2 goal | Maximise coverage within mock constraints | Subscription blocked; deliver value now |
| Test depth | Cover business rules (not just smoke) | Smoke-only would miss core categorisation logic |
| Push-related stories (3) | Excluded — deferred to Phase 3 | Reliable simulation not feasible without real VAPID + backend |
| Migrate Phase 1 tests to fixture | Yes, in Phase 2 (Batch 4) | Single coherent style across the suite |
| Mock data storage | JSON files per domain | Easy git diff, no production code coupling |
| File split granularity | Per user story (8 files) | Mirrors plan structure, parallel CI, easier debugging |

## Out-of-Scope Beyond Phase 3

- Visual regression (Percy / Playwright snapshot screenshots)
- Cross-browser matrix (Firefox, WebKit) — chromium only for now
- Mobile viewport / device emulation tests
- Performance budgets / Lighthouse audits
- Accessibility (axe-core) integration

These remain options for Phase 4+ once the functional coverage is stable.

## Success Criteria

- All 8 spec files exist and pass on CI
- Total test count ≥ 35 (target ~40)
- CI runtime ≤ 90s (4 workers + cache)
- No Phase 1 tests removed without equivalent coverage in the new fixture-based files
- Phase 3 boundary documented in code (comment block in each affected spec)
- One reviewer can trace each user story to one spec file
