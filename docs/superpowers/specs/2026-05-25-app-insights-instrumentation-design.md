# App Insights Instrumentation — Design

**Status:** Approved — ready for implementation planning
**Date:** 2026-05-25
**Author:** Brainstormed with Claude (menunest project, using the `azure:appinsights-instrumentation` skill for SDK guidance)

## Problem

A real bug exists in the budget module — `AddGroupDialog` fails to create a group — but there is no observability to find the root cause. The frontend dialog only knows the request was rejected; the backend translates `DomainException`/`ValidationException` to 400 ProblemDetails inside `ExceptionHandlingMiddleware`, so even unhandled exception logging never sees the real stack. We need a forensic surface that the maintainer can open to see (a) which RTK mutation failed with what payload and (b) the full backend exception that produced the 400. The bug is the immediate motivation, but the result should be reusable for every future bug in the same shape.

## Goals

- Capture every failed RTK Query mutation in the SPA with endpoint name, original args, response status, and response body.
- Capture every exception handled in `ExceptionHandlingMiddleware` (DomainException, ValidationException, UnauthorizedAccessException, and the fallthrough Exception) with full stack trace and request context (path, method, user id, correlation id), **before** the translation to ProblemDetails runs.
- Correlate SPA events with the backend request they triggered via W3C `traceparent`, so the maintainer can click an event in the portal and follow it to the EF query that ran.
- Stay within the App Insights free tier (5 GB ingestion / month). No sampling needed for a family-app volume.
- No user-facing UX change. This is observability for the maintainer, not error messaging for the family members. Existing in-dialog error rendering stays.

## Non-goals

- Custom dashboards or alerts. The portal's default "Failures" and "End-to-end transaction" views are sufficient for a single-developer triage workflow. Dashboards can come later.
- PII redaction. The app is a private family budget; data flowing into the maintainer's own AI workspace is the same data the maintainer already owns.
- Replacing the in-dialog error display. Telemetry runs alongside, not instead.
- Browser performance monitoring (Core Web Vitals) — out of scope for this iteration.

## Approach

Two telemetry feeds converge on a single Application Insights workspace (Workspace-based, linked to Log Analytics), inside the same resource group as the App Service that hosts the backend:

```
React SPA  ── @microsoft/applicationinsights-web ──┐
(Static Web App)                                   ├──► Application Insights ──► Log Analytics
ASP.NET .NET 10 ── App Service extension ──────────┘    (Workspace-based)
(App Service)      + Microsoft.ApplicationInsights.AspNetCore SDK
```

Backend uses the **App Service extension** (codeless agent, configured via two appsettings the Bicep already writes) for HTTP / dependency / unhandled-exception baseline. We *also* add the `Microsoft.ApplicationInsights.AspNetCore` SDK so a `TelemetryClient` is available in DI — without that, the agent alone cannot see exceptions that `ExceptionHandlingMiddleware` catches before they reach the framework. The middleware adds a single `TrackException` call in each catch block with structured properties.

Frontend uses `@microsoft/applicationinsights-web` + `@microsoft/applicationinsights-react-js`. Auto-collection captures page views, route changes, fetch/XHR failures, and uncaught exceptions. A custom Redux middleware listens for `isRejectedWithValue` actions from RTK Query and emits a `trackException` with the endpoint name, original args, response status, and response body.

W3C trace correlation is enabled at both ends so the portal renders SPA → backend → DB as one transaction.

## Architecture

### Resource layout

- 1 `Microsoft.Insights/components` (Workspace-based mode) in the existing `infra/` resource group.
- Reuses an existing Log Analytics workspace if present; otherwise the Bicep creates one alongside.
- `connectionString` flows out of Bicep → into App Service appsettings (backend) and into a GitHub Actions secret (frontend build).

### Backend code (3 files touched)

- `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj` — add `<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" />` (latest stable).
- `backend/src/MenuNest.WebApi/Program.cs` — add `builder.Services.AddApplicationInsightsTelemetry();` next to `AddControllers()`. No other config — connection string is picked up from the env var the App Service appsettings emit.
- `backend/src/MenuNest.WebApi/Middleware/ExceptionHandlingMiddleware.cs` — inject `TelemetryClient` via constructor; in each `catch` block, call `TrackException` **before** the response is written. Properties attached:

  ```csharp
  new Dictionary<string, string>
  {
      ["Path"]       = context.Request.Path,
      ["Method"]     = context.Request.Method,
      ["UserId"]     = context.User?.FindFirst("oid")?.Value ?? "anonymous",
      ["StatusCode"] = statusCode.ToString(CultureInfo.InvariantCulture),
  }
  ```

  W3C `traceparent` is attached by the SDK automatically.

### Frontend code (new module + 3 wire-ups)

New folder `frontend/src/shared/telemetry/` with two files:

- `appInsights.ts` — instantiate `ApplicationInsights` with `ReactPlugin`, read `import.meta.env.VITE_APPINSIGHTS_CONNECTION_STRING`, enable auto-route-tracking + CORS correlation + request/response header tracking, set `disableTelemetry: true` when the env var is absent (local dev no-op).
- `rtkErrorTelemetry.ts` — Redux middleware:

  ```ts
  if (isRejectedWithValue(action)) {
    appInsights.trackException({
      exception: new Error(`RTK ${action.type}`),
      properties: {
        endpoint:   action.meta?.arg?.endpointName,
        args:       JSON.stringify(action.meta?.arg?.originalArgs),
        statusCode: String(action.payload?.status ?? 'unknown'),
        response:   JSON.stringify(action.payload?.data ?? null),
      },
    })
  }
  ```

Wire-ups:

- `frontend/src/main.tsx` — `import './shared/telemetry/appInsights'` once for the init side-effect.
- `frontend/src/store.ts` — append `rtkErrorTelemetry` to `getDefaultMiddleware().concat(api.middleware, rtkErrorTelemetry)`.
- `frontend/src/shared/components/AppLayout.tsx` — wrap children with `<AppInsightsErrorBoundary appInsights={reactPlugin} onError={ErrorFallback}>`.
- Wherever login completes (MSAL success callback + Google success callback) — call `appInsights.setAuthenticatedUserContext(userId)` so every subsequent event carries the user id.

### Bicep / provisioning

The infra is *almost* there:

- `infra/main.bicep:59` already declares `resource appInsights 'Microsoft.Insights/components' existing` and `app-service.bicep:122-128` already wires `APPLICATIONINSIGHTS_CONNECTION_STRING` + `ApplicationInsightsAgent_EXTENSION_VERSION = ~3` into App Service appsettings.
- `infra/main.bicepparam:45` has `existingAppInsightsName` commented out.

Changes:

1. Uncomment `existingAppInsightsName` and set it to the real resource name. (If the AI resource does not exist in the RG yet, add `infra/modules/app-insights.bicep` that creates a Workspace-based component and reuses or creates a Log Analytics workspace; replace the `existing =` declaration in `main.bicep` with the module call.)
2. Emit a new output `appInsightsConnectionString` from `main.bicep` so CI can read it.

### CI / connection-string injection

- `.github/workflows/main_menunest.yml` (backend) — no change. App Service appsettings already carry the connection string.
- `.github/workflows/azure-static-web-apps-*.yml` (frontend) — add an env entry that passes the connection string into `npm run build`:

  ```yaml
  env:
    VITE_APPINSIGHTS_CONNECTION_STRING: ${{ secrets.APPINSIGHTS_CONNECTION_STRING }}
  ```

  Local dev keeps the env var unset; SDK no-ops.

## What we capture (operational view)

| Event | Source | Properties |
|---|---|---|
| Page view | React Plugin auto | URL, route, duration |
| Fetch/XHR failure | SDK auto | URL, status, duration |
| Uncaught React render error | `AppInsightsErrorBoundary` | component stack |
| RTK mutation rejected | `rtkErrorTelemetry` middleware | endpoint, args, status, response body |
| Backend HTTP request | App Service extension | path, method, duration, status |
| EF Core dependency call | App Service extension | SQL text (parameterised), duration |
| Backend unhandled exception | App Service extension | stack |
| Backend handled exception | `ExceptionHandlingMiddleware.TrackException` | stack, path, method, user id, status code |

Every event from both ends carries the same W3C `traceparent`, so the portal's "End-to-end transaction" view renders them as one timeline.

## Cost

App Insights free tier provides 5 GB ingestion per month. A family-scale app's traffic is far below that. No sampling configured. If volume ever climbs, switch on adaptive sampling at the SDK level — single config flag.

## Migration

- No data migration.
- Backwards-compatible at every layer: removing the env var or NuGet package degrades gracefully to "no telemetry, app still works".
- Local dev is unaffected — the SDK self-disables when the connection string is empty.

## Testing

- **Backend unit:** add one xUnit test that instantiates `ExceptionHandlingMiddleware` with a mocked `TelemetryClient` (or `ITelemetry` interceptor), throws a `DomainException` from the inner delegate, and asserts `TrackException` was called exactly once with the expected properties before the 400 response was written.
- **Frontend unit:** add one Vitest test for `rtkErrorTelemetry`: dispatch a synthetic `isRejectedWithValue` action and assert the mocked `appInsights.trackException` was called with the right properties.
- **Manual smoke after deploy:** reproduce the create-group failure → open App Insights → "Failures" tab → confirm both the SPA `trackException` event and the backend exception are linked under the same operation ID.

## Open questions

None for v1.

Phase 2 candidates (explicitly out of scope here):

- Custom dashboard with the top 5 most-frequent failure shapes.
- Alert rule emailing the maintainer when a new failure shape appears.
- PII redaction policy (only relevant if the app ever ships to non-family users).
- Browser performance monitoring (Core Web Vitals, LCP, INP).
- Server-side log forwarding from `ILogger` to App Insights traces (auto-instrument already captures warnings/errors; this would add structured info-level traces).
