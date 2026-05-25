# App Insights Instrumentation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Application Insights telemetry so the maintainer can diagnose bugs forensically — every RTK Query rejection on the frontend and every handled exception in `ExceptionHandlingMiddleware` on the backend gets tracked, correlated end-to-end via W3C traceparent.

**Architecture:** Backend uses the App Service codeless agent (already configured by Bicep) for the baseline HTTP/dependency/unhandled-exception capture; we add `Microsoft.ApplicationInsights.AspNetCore` only so a `TelemetryClient` is available to log handled exceptions before the middleware translates them to ProblemDetails. Frontend uses `@microsoft/applicationinsights-web` + React plugin for auto-collection plus one custom Redux middleware that turns `isRejectedWithValue` actions into `trackException` calls with endpoint name, args, status, and response body.

**Tech Stack:**
- Backend: .NET 10, `Microsoft.ApplicationInsights.AspNetCore`
- Frontend: React 19, Redux Toolkit, `@microsoft/applicationinsights-web` + `@microsoft/applicationinsights-react-js`
- Infra: Bicep (`infra/`)
- CI: GitHub Actions (the existing `main_menunest.yml` and `azure-static-web-apps-*.yml`)
- Pre-commit hook validates backend build/tests + frontend build on every commit; do not skip

**Spec:** [docs/superpowers/specs/2026-05-25-app-insights-instrumentation-design.md](../specs/2026-05-25-app-insights-instrumentation-design.md)

---

## File Structure

### Create

- `backend/tests/MenuNest.WebApi.UnitTests/Middleware/ExceptionHandlingMiddlewareTelemetryTests.cs` (or extend the existing unit-test project — see Task 3 for the discovered project layout)
- `frontend/src/shared/telemetry/appInsights.ts`
- `frontend/src/shared/telemetry/rtkErrorTelemetry.ts`
- `infra/modules/app-insights.bicep` (only if the AI resource does not yet exist in the RG — see Task 4)

### Modify

- `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj` — add SDK package reference
- `backend/src/MenuNest.WebApi/Program.cs` — register telemetry
- `backend/src/MenuNest.WebApi/Middleware/ExceptionHandlingMiddleware.cs` — inject `TelemetryClient`, call `TrackException` in each catch block
- `frontend/src/main.tsx` — import the telemetry init module for its side effect
- `frontend/src/store.ts` — append `rtkErrorTelemetry` to default middleware
- `frontend/src/shared/components/AppLayout.tsx` — wrap children in `AppInsightsErrorBoundary`
- `frontend/src/pages/auth/LoginPage.tsx` — `setAuthenticatedUserContext` after successful login (one call per provider callback)
- `frontend/package.json` — add the two `@microsoft/applicationinsights-*` deps
- `infra/main.bicepparam` — uncomment `existingAppInsightsName` and set to the real resource name
- `infra/main.bicep` — optionally replace the `existing` reference with the new module call from Task 4
- `.github/workflows/azure-static-web-apps-*.yml` — pass `VITE_APPINSIGHTS_CONNECTION_STRING` env var into the build step

---

## Conventions used in this plan

- Connection strings are not secrets in the SPA bundle sense (they are ingestion endpoints, safe to embed), but they are still sourced from secrets in CI for hygiene. Repo secret name: `APPINSIGHTS_CONNECTION_STRING`.
- The xUnit unit test project for the WebApi layer may not exist yet (current unit tests live under `MenuNest.Application.UnitTests`). Task 3 discovers and adapts.
- Commits: one per task, prefixed `feat(telemetry):`, `chore(telemetry):`, `test(telemetry):`, or `infra(telemetry):`. Pre-commit hook must stay green on every commit.
- Local dev: no env var → `disableTelemetry: true` → SDK no-ops. Tasks below treat this as the default-safe state.

---

## Task 1: Backend — register the SDK

Adds `Microsoft.ApplicationInsights.AspNetCore` and wires `AddApplicationInsightsTelemetry()` in `Program.cs`. The agent is already configured by Bicep; adding the SDK makes `TelemetryClient` available to DI without disabling the agent (the SDK and agent are designed to coexist — the SDK takes priority).

**Files:**
- Modify: `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`
- Modify: `backend/src/MenuNest.WebApi/Program.cs`

- [ ] **Step 1: Add the NuGet package reference**

Open `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`. Find the `<ItemGroup>` block holding `<PackageReference>` lines. Add:

```xml
<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.22.0" />
```

(Use the current 2.x stable. If `dotnet restore` complains about the exact version pin, drop the `Version=` and let central-package-management resolve — check `Directory.Packages.props` if it exists.)

- [ ] **Step 2: Register the SDK in Program.cs**

In `backend/src/MenuNest.WebApi/Program.cs`, find this block:

```csharp
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
```

Immediately above it (or below `AddInfrastructure`), add ONE line:

```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

The SDK reads `APPLICATIONINSIGHTS_CONNECTION_STRING` from configuration automatically. App Service already injects that env var via Bicep, so no extra config is needed. Local dev without the env var → SDK initializes with no sink and silently drops events.

- [ ] **Step 3: Build + run unit tests**

```bash
cd backend && dotnet build --nologo
cd backend && dotnet test tests/MenuNest.Application.UnitTests --nologo
```

Expected: clean build, all tests green.

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj backend/src/MenuNest.WebApi/Program.cs
git commit -m "feat(telemetry): register Microsoft.ApplicationInsights.AspNetCore SDK"
```

---

## Task 2: Backend — TrackException in ExceptionHandlingMiddleware

Adds a constructor-injected `TelemetryClient` and a single `TrackException` call inside each `catch` block, so handled exceptions reach App Insights with full stack + request context before being translated to a ProblemDetails response.

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Middleware/ExceptionHandlingMiddleware.cs`

- [ ] **Step 1: Inject TelemetryClient**

Open `backend/src/MenuNest.WebApi/Middleware/ExceptionHandlingMiddleware.cs`. At the top add the using:

```csharp
using System.Globalization;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
```

Update the constructor to take `TelemetryClient`:

```csharp
private readonly RequestDelegate _next;
private readonly ILogger<ExceptionHandlingMiddleware> _logger;
private readonly TelemetryClient _telemetry;

public ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    TelemetryClient telemetry)
{
    _next = next;
    _logger = logger;
    _telemetry = telemetry;
}
```

- [ ] **Step 2: Track each handled exception**

Inside the `try { await _next(context); }` body, the current code has four `catch` blocks (ValidationException, DomainException, UnauthorizedAccessException, generic Exception). For EACH catch block, immediately after the existing `_logger` call and BEFORE `WriteProblemAsync(...)`, add:

```csharp
_telemetry.TrackException(new ExceptionTelemetry(ex)
{
    SeverityLevel = SeverityLevel.Error,
    Properties =
    {
        ["Path"]       = context.Request.Path,
        ["Method"]     = context.Request.Method,
        ["UserId"]     = context.User?.FindFirst("oid")?.Value ?? "anonymous",
        ["StatusCode"] = statusCodeForCatch.ToString(CultureInfo.InvariantCulture),
    },
});
```

Where `statusCodeForCatch` is the literal status from each catch:
- ValidationException → `StatusCodes.Status400BadRequest`
- DomainException → `StatusCodes.Status400BadRequest`
- UnauthorizedAccessException → `StatusCodes.Status401Unauthorized`
- generic Exception → `StatusCodes.Status500InternalServerError`

You can inline the literal directly (e.g. `["StatusCode"] = "400"`) — that's clearer than a local variable when there are only four uses.

Severity for the generic catch should be `SeverityLevel.Critical` (server faults are worse than handled domain errors); the three handled cases use `SeverityLevel.Error` or `Warning` — pick `Warning` for validation/domain (expected business outcomes), `Error` for unauthorized, `Critical` for the generic catch. Final mapping:

| Catch | Severity |
|---|---|
| ValidationException | Warning |
| DomainException | Warning |
| UnauthorizedAccessException | Error |
| generic Exception | Critical |

- [ ] **Step 3: Build + run unit tests**

```bash
cd backend && dotnet build --nologo
cd backend && dotnet test tests/MenuNest.Application.UnitTests --nologo
```

Expected: clean. Existing tests don't construct this middleware so they remain green.

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.WebApi/Middleware/ExceptionHandlingMiddleware.cs
git commit -m "feat(telemetry): TrackException in ExceptionHandlingMiddleware before translate"
```

---

## Task 3: Backend — unit test for middleware telemetry

Verifies the new `TrackException` call fires once per catch with the expected properties and severity. This catches future regressions where someone might inadvertently move the telemetry call below the response-write.

**Files:**
- Investigate which project holds WebApi unit tests; create test file in that project. If no WebApi unit-test project exists, add the test to `MenuNest.Application.UnitTests` (it covers application-layer + adjacent infrastructure already).

- [ ] **Step 1: Check the unit-test project layout**

```bash
ls backend/tests/
```

If `MenuNest.WebApi.UnitTests` exists, place the new test there. Otherwise place it in `MenuNest.Application.UnitTests` under a new `WebApi/Middleware/` folder.

- [ ] **Step 2: Write the failing test**

Create the file (path adjusted per Step 1). Suggested contents:

```csharp
using FluentAssertions;
using MenuNest.Domain.Exceptions;
using MenuNest.WebApi.Middleware;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace MenuNest.Application.UnitTests.WebApi.Middleware;

public class ExceptionHandlingMiddlewareTelemetryTests
{
    private sealed class CapturingChannel : ITelemetryChannel
    {
        public List<ITelemetry> Sent { get; } = new();
        public bool? DeveloperMode { get; set; }
        public string? EndpointAddress { get; set; }
        public void Send(ITelemetry item) => Sent.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }

    private static (ExceptionHandlingMiddleware mw, CapturingChannel channel) Build(RequestDelegate inner)
    {
        var channel = new CapturingChannel();
        var config = new TelemetryConfiguration { TelemetryChannel = channel };
        config.InstrumentationKey = Guid.NewGuid().ToString();
        var client = new TelemetryClient(config);
        var mw = new ExceptionHandlingMiddleware(inner, NullLogger<ExceptionHandlingMiddleware>.Instance, client);
        return (mw, channel);
    }

    [Fact]
    public async Task Tracks_DomainException_before_translating_to_400()
    {
        RequestDelegate inner = _ => throw new DomainException("Group not found.");
        var (mw, channel) = Build(inner);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/budget/groups";
        ctx.Request.Method = "POST";
        ctx.Response.Body = new MemoryStream();

        await mw.InvokeAsync(ctx);

        var ex = channel.Sent.OfType<ExceptionTelemetry>().Should().ContainSingle().Subject;
        ex.SeverityLevel.Should().Be(SeverityLevel.Warning);
        ex.Properties["Path"].Should().Be("/api/budget/groups");
        ex.Properties["Method"].Should().Be("POST");
        ex.Properties["StatusCode"].Should().Be("400");
        ex.Exception.Should().BeOfType<DomainException>();
    }
}
```

(Add one more `[Fact]` covering the generic `Exception` catch with `SeverityLevel.Critical` if you have time — the first test already proves the pattern.)

- [ ] **Step 3: Run the test**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~ExceptionHandlingMiddlewareTelemetry" --nologo
```

Expected: passes. If the test cannot resolve `MenuNest.WebApi.*` types, add a `<ProjectReference>` to `MenuNest.WebApi` from the test `.csproj`.

- [ ] **Step 4: Commit**

```bash
git add backend/tests/MenuNest.Application.UnitTests/WebApi/Middleware/ExceptionHandlingMiddlewareTelemetryTests.cs
git commit -m "test(telemetry): assert TrackException fires for handled exceptions"
```

---

## Task 4: Infra — provision (or activate) the Application Insights resource

The Bicep already references `appInsights` as `existing` and writes the agent's appsettings; the only blocker is that `existingAppInsightsName` is commented out. If the resource truly exists in the RG, uncommenting is enough. If not, add a module that creates it.

**Files:**
- Modify: `infra/main.bicepparam`
- Possibly modify: `infra/main.bicep`
- Possibly create: `infra/modules/app-insights.bicep`

- [ ] **Step 1: Probe whether the resource exists**

```bash
az resource list --resource-group <rg-name> --resource-type "Microsoft.Insights/components" --query "[].name" -o tsv
```

Substitute the actual RG name. (If you're not signed in to Azure, ask the maintainer for the name to pass to `existingAppInsightsName` in Step 2.)

- [ ] **Step 2: Activate the existing reference**

In `infra/main.bicepparam`, find this line near line 45:

```bicep
// param existingAppInsightsName = 'menunest'
```

Uncomment it and replace the value with the actual name from Step 1:

```bicep
param existingAppInsightsName = '<actual-resource-name>'
```

- [ ] **Step 3 (only if the resource does NOT exist): create the module**

Create `infra/modules/app-insights.bicep`:

```bicep
@description('Location for the App Insights component')
param location string

@description('Name of the App Insights component')
param name string

@description('Resource ID of an existing Log Analytics workspace; if empty, a new one is created with name {name}-law')
param workspaceResourceId string = ''

resource law 'Microsoft.OperationalInsights/workspaces@2022-10-01' = if (empty(workspaceResourceId)) {
  name: '${name}-law'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource ai 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: empty(workspaceResourceId) ? law.id : workspaceResourceId
    IngestionMode: 'LogAnalytics'
  }
}

output connectionString string = ai.properties.ConnectionString
output instrumentationKey string = ai.properties.InstrumentationKey
```

Replace the `existing` declaration in `main.bicep` (line 59) with:

```bicep
module appInsightsModule 'modules/app-insights.bicep' = {
  name: 'deploy-app-insights'
  params: {
    location: location
    name: appInsightsName
    workspaceResourceId: existingLogAnalyticsWorkspaceId  // empty string is fine
  }
}
```

And anywhere downstream that read `appInsights.properties.ConnectionString`, switch to `appInsightsModule.outputs.connectionString`. Add params `appInsightsName` and `existingLogAnalyticsWorkspaceId` at the top of `main.bicep`.

- [ ] **Step 4: Emit the connection string as a Bicep output**

In `infra/main.bicep` (existing OUTPUTS block near line 131-138), add:

```bicep
output appInsightsConnectionString string = appInsights.properties.ConnectionString
```

(Or `appInsightsModule.outputs.connectionString` if you went through Step 3.)

- [ ] **Step 5: Validate the Bicep**

```bash
cd infra && az bicep build --file main.bicep
```

Expected: succeeds with no errors. (`az bicep` only — do not run `az deployment what-if` here; that's a deploy concern handled when the Pay-As-You-Go subscription is reactivated.)

- [ ] **Step 6: Commit**

```bash
git add infra/
git commit -m "infra(telemetry): activate App Insights wiring in Bicep"
```

---

## Task 5: Frontend — add deps + telemetry module

Adds the two AI npm packages and the `frontend/src/shared/telemetry/` module. No other files touched yet (the init runs as a side effect when imported in Task 7).

**Files:**
- Modify: `frontend/package.json` (via `npm install`)
- Create: `frontend/src/shared/telemetry/appInsights.ts`

- [ ] **Step 1: Install deps**

```bash
cd frontend && npm install @microsoft/applicationinsights-web @microsoft/applicationinsights-react-js
```

- [ ] **Step 2: Create the init module**

Create `frontend/src/shared/telemetry/appInsights.ts`:

```ts
import {ApplicationInsights} from '@microsoft/applicationinsights-web'
import {ReactPlugin} from '@microsoft/applicationinsights-react-js'

const connectionString = import.meta.env.VITE_APPINSIGHTS_CONNECTION_STRING

export const reactPlugin = new ReactPlugin()

export const appInsights = new ApplicationInsights({
  config: {
    connectionString,
    // If the env var is empty, init the SDK in a sink-less state — calls
    // become no-ops instead of throwing.
    disableTelemetry: !connectionString,
    extensions: [reactPlugin],
    enableAutoRouteTracking: true,
    enableCorsCorrelation: true,         // propagates W3C traceparent to /api/*
    enableRequestHeaderTracking: true,
    enableResponseHeaderTracking: true,
    // For local dev: keep the SDK quiet.
    autoTrackPageVisitTime: false,
    disableExceptionTracking: false,
  },
})

appInsights.loadAppInsights()

// Convenience for setting / clearing the signed-in user across the app.
// Call after auth success; call clearAuthenticatedUserContext on sign-out.
export function setUser(userId: string | null) {
  if (!connectionString) return
  if (userId) appInsights.setAuthenticatedUserContext(userId, undefined, true)
  else appInsights.clearAuthenticatedUserContext()
}
```

- [ ] **Step 3: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: clean tsc + vite. The new module is unused so far; tsc tolerates unused exports.

- [ ] **Step 4: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/src/shared/telemetry/appInsights.ts
git commit -m "feat(telemetry): add @microsoft/applicationinsights-web + init module"
```

---

## Task 6: Frontend — rtkErrorTelemetry middleware

Adds the Redux middleware and wires it into the store. Once this lands, every rejected RTK mutation produces a `trackException` event.

**Files:**
- Create: `frontend/src/shared/telemetry/rtkErrorTelemetry.ts`
- Modify: `frontend/src/store.ts`

- [ ] **Step 1: Create the middleware**

Create `frontend/src/shared/telemetry/rtkErrorTelemetry.ts`:

```ts
import {isRejectedWithValue, type Middleware} from '@reduxjs/toolkit'
import {appInsights} from './appInsights'

interface RtkRejectedMeta {
  arg?: {
    endpointName?: string
    originalArgs?: unknown
    type?: 'query' | 'mutation'
  }
}

/**
 * Listens for RTK Query rejections and surfaces them as App Insights
 * exception events. Only mutations are tracked by default — queries
 * that fail because of stale caches, route changes, or background
 * refetches are noisy and not what the maintainer needs to diagnose.
 * Flip to `arg?.type !== 'query'` if that ever changes.
 */
export const rtkErrorTelemetry: Middleware = () => (next) => (action) => {
  if (isRejectedWithValue(action)) {
    const meta = (action.meta ?? {}) as RtkRejectedMeta
    if (meta.arg?.type === 'mutation') {
      const payload = action.payload as {status?: number | string; data?: unknown} | undefined
      appInsights.trackException({
        exception: new Error(`RTK ${meta.arg.endpointName ?? 'unknown'} rejected`),
        properties: {
          endpoint:   meta.arg.endpointName ?? 'unknown',
          args:       safeStringify(meta.arg.originalArgs),
          statusCode: String(payload?.status ?? 'unknown'),
          response:   safeStringify(payload?.data ?? null),
        },
      })
    }
  }
  return next(action)
}

function safeStringify(value: unknown): string {
  try {
    return JSON.stringify(value)
  } catch {
    return '<unserializable>'
  }
}
```

- [ ] **Step 2: Wire into store.ts**

Open `frontend/src/store.ts`. Find the existing middleware composition (it adds `api.middleware` to `getDefaultMiddleware()`). Add the import and append the new middleware AFTER `api.middleware`:

```ts
import {rtkErrorTelemetry} from './shared/telemetry/rtkErrorTelemetry'
// …
middleware: (getDefaultMiddleware) =>
  getDefaultMiddleware().concat(api.middleware, rtkErrorTelemetry),
```

(If `store.ts` already destructures something else, append `rtkErrorTelemetry` to the end of the `.concat(...)` call. Order matters: the telemetry middleware MUST sit after `api.middleware` so it observes the rejection action.)

- [ ] **Step 3: Build**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/shared/telemetry/rtkErrorTelemetry.ts frontend/src/store.ts
git commit -m "feat(telemetry): track failed RTK mutations as App Insights exceptions"
```

---

## Task 7: Frontend — wire init + ErrorBoundary + login user context

Runs the telemetry init at app boot, wraps the layout in `AppInsightsErrorBoundary`, and tags events with the signed-in user after each auth path completes.

**Files:**
- Modify: `frontend/src/main.tsx`
- Modify: `frontend/src/shared/components/AppLayout.tsx`
- Modify: `frontend/src/pages/auth/LoginPage.tsx`

- [ ] **Step 1: Side-effect import in main.tsx**

In `frontend/src/main.tsx`, near the top alongside the other side-effect imports (e.g. CSS), add:

```ts
import './shared/telemetry/appInsights'
```

This triggers SDK init exactly once per page load.

- [ ] **Step 2: ErrorBoundary in AppLayout**

In `frontend/src/shared/components/AppLayout.tsx`:

```tsx
import {AppInsightsErrorBoundary} from '@microsoft/applicationinsights-react-js'
import {reactPlugin} from '../telemetry/appInsights'
// …
function Fallback() {
  return (
    <div style={{padding: 32, textAlign: 'center'}}>
      <p>Something went wrong while rendering this page.</p>
      <button type="button" onClick={() => window.location.reload()}>Reload</button>
    </div>
  )
}

export function AppLayout() {
  // existing logic …
  return (
    <AppInsightsErrorBoundary appInsights={reactPlugin} onError={Fallback}>
      {/* existing children */}
    </AppInsightsErrorBoundary>
  )
}
```

Adjust to whatever the current `AppLayout` returns — the wrap goes at the outermost render boundary.

- [ ] **Step 3: User context after login**

In `frontend/src/pages/auth/LoginPage.tsx`, two success paths exist (Microsoft and Google). For each, after the token is acquired (Microsoft) or after `setGoogleToken(...)` (Google), call:

```tsx
import {setUser} from '../../shared/telemetry/appInsights'
// …
setUser(<userId>)  // userId from the decoded token sub/oid claim
```

For Google: decode the JWT `sub` from `credentialResponse.credential`. (A small JWT decoder is already used elsewhere — search for `jwtDecode` or pick the `sub` manually by splitting the token's payload section.)

For Microsoft: the existing MSAL code probably already has access to `account.localAccountId` or `account.homeAccountId` — use whichever the rest of the app uses as the canonical user id.

If extracting the id cleanly requires more than ~5 lines, defer this step to Task 8 — it's the lowest-priority piece. The exception telemetry still works without it, events just say "anonymous".

- [ ] **Step 4: Build**

```bash
cd frontend && npm run build
```

Expected: clean.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/main.tsx frontend/src/shared/components/AppLayout.tsx frontend/src/pages/auth/LoginPage.tsx
git commit -m "feat(telemetry): init at boot, wrap layout in ErrorBoundary, tag user context"
```

---

## Task 8: Frontend — Vitest for rtkErrorTelemetry

Smoke test for the middleware so future RTK upgrades don't silently break the rejection-shape contract.

**Files:**
- Check: does the frontend have a Vitest config? Look for `vitest.config.ts` or `vite.config.ts` referencing `vitest`. If not, skip to Task 9 — the spec calls Vitest "where applicable".

- [ ] **Step 1: Check the test setup**

```bash
ls frontend/vitest.config.* 2>/dev/null
grep -l "vitest" frontend/vite.config.* 2>/dev/null
ls frontend/src/**/*.test.* 2>/dev/null | head
```

If no Vitest is set up, SKIP this task and add `// TODO: add Vitest test for rtkErrorTelemetry` somewhere in the rtkErrorTelemetry file. Move on to Task 9.

- [ ] **Step 2: Write the test (if Vitest exists)**

Create `frontend/src/shared/telemetry/rtkErrorTelemetry.test.ts`:

```ts
import {describe, it, expect, vi, beforeEach} from 'vitest'

vi.mock('./appInsights', () => ({
  appInsights: {trackException: vi.fn()},
  reactPlugin: {},
  setUser: vi.fn(),
}))

import {appInsights} from './appInsights'
import {rtkErrorTelemetry} from './rtkErrorTelemetry'

describe('rtkErrorTelemetry', () => {
  beforeEach(() => vi.clearAllMocks())

  it('tracks rejected mutations with endpoint and args', () => {
    const next = vi.fn(a => a)
    const mw = rtkErrorTelemetry({} as any)(next)

    mw({
      type: 'budget/createGroup/rejected',
      meta: {
        arg: {endpointName: 'createBudgetGroup', originalArgs: {name: 'Bills'}, type: 'mutation'},
        rejectedWithValue: true,
      },
      payload: {status: 400, data: {title: 'Bad'}},
      error: {message: 'Rejected'},
    } as any)

    expect(appInsights.trackException).toHaveBeenCalledTimes(1)
    const call = (appInsights.trackException as any).mock.calls[0][0]
    expect(call.properties.endpoint).toBe('createBudgetGroup')
    expect(call.properties.args).toContain('Bills')
    expect(call.properties.statusCode).toBe('400')
  })

  it('does not track rejected queries', () => {
    const next = vi.fn(a => a)
    const mw = rtkErrorTelemetry({} as any)(next)

    mw({
      type: 'budget/getSummary/rejected',
      meta: {arg: {type: 'query', endpointName: 'getBudgetSummary'}, rejectedWithValue: true},
      payload: {status: 404, data: null},
      error: {message: 'NF'},
    } as any)

    expect(appInsights.trackException).not.toHaveBeenCalled()
  })
})
```

- [ ] **Step 3: Run + commit**

```bash
cd frontend && npx vitest run src/shared/telemetry/
git add frontend/src/shared/telemetry/rtkErrorTelemetry.test.ts
git commit -m "test(telemetry): vitest covers rtkErrorTelemetry shape contract"
```

---

## Task 9: CI — pass the connection string into the frontend build

The Static Web App build needs `VITE_APPINSIGHTS_CONNECTION_STRING` at build time. The repo will use a GitHub Actions secret `APPINSIGHTS_CONNECTION_STRING` (maintainer creates it once via repo settings).

**Files:**
- Modify: `.github/workflows/azure-static-web-apps-*.yml`

- [ ] **Step 1: Locate the Static Web App workflow**

```bash
ls .github/workflows/azure-static-web-apps-*.yml
```

There should be one file matching this pattern.

- [ ] **Step 2: Add the env var**

In that workflow, find the step that builds the SPA — typically a `azure/static-web-apps-deploy@v1` step or an explicit `npm run build` step. Static Web Apps' deploy action accepts an `env` block. Add:

```yaml
- uses: Azure/static-web-apps-deploy@v1
  with:
    # … existing inputs
  env:
    VITE_APPINSIGHTS_CONNECTION_STRING: ${{ secrets.APPINSIGHTS_CONNECTION_STRING }}
```

If the workflow runs `npm run build` directly (no deploy action), add the same env block under that step.

- [ ] **Step 3: Document the secret in the repo README or .env.example**

Add a one-line note in `frontend/.env.example` (create the file if it doesn't exist):

```
VITE_APPINSIGHTS_CONNECTION_STRING=
# Set in CI from the APPINSIGHTS_CONNECTION_STRING repo secret.
# Local dev: leave empty — the SDK self-disables.
```

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/azure-static-web-apps-*.yml frontend/.env.example
git commit -m "ci(telemetry): pass App Insights connection string into SPA build"
```

---

## Task 10: Smoke verification after deploy (manual)

This is not an automated step but worth recording in the plan so the implementer remembers to perform it once the Pay-As-You-Go subscription is reactivated and Bicep deploys.

- [ ] After `azd deploy` or the equivalent, confirm:
  - App Insights resource is provisioned in the expected RG with Workspace-based mode.
  - App Service has both `APPLICATIONINSIGHTS_CONNECTION_STRING` and `ApplicationInsightsAgent_EXTENSION_VERSION = ~3` set.
  - Static Web App's build log shows `VITE_APPINSIGHTS_CONNECTION_STRING` being injected.
- [ ] Reproduce the create-group bug:
  1. Open `/budget` and tap `+ Add Group`.
  2. Submit; observe the failure.
- [ ] Open Application Insights → Failures pane:
  - Confirm the SPA event (`RTK createBudgetGroup rejected`) is present with `endpoint`, `args`, `statusCode`, `response` properties.
  - Confirm a backend ExceptionTelemetry is linked under the same operation ID with full stack trace.
- [ ] Use the captured stack + payload to file a proper bug-fix task. (This is the outcome the whole spec is designed to enable.)

---

## Done

After Task 9, the maintainer has end-to-end forensic visibility for any future failed mutation, and the existing create-group bug becomes diagnosable on the next reproduction.

### Spec coverage

- Backend SDK + middleware TrackException: Tasks 1, 2, 3
- Frontend SDK + RTK middleware + ErrorBoundary + user context: Tasks 5, 6, 7, 8
- Bicep / resource provisioning: Task 4
- CI connection-string injection: Task 9
- Manual smoke after deploy: Task 10
- Cost / sampling / non-goals: no work needed — covered by SDK defaults and Bicep settings

No backend domain or schema changes. No data migration. Local dev unaffected.
