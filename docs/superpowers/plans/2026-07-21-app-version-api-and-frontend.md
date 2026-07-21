# App & API Version (auto-from-git) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Both the API and the SPA report a git-derived version (`MAJOR.MINOR.PATCH+<shortSHA>`), the API at a public `GET /version`, the SPA on `/settings` showing app + API side by side with an in-sync badge.

**Architecture:** The version is *embedded into each build artifact at compile time* (never runtime config). Backend: `<Version>` base + short SHA via MSBuild `SourceRevisionId`, read back by reflection, served by a minimal-API endpoint. Frontend: base from `package.json` + short SHA computed in `vite.config.ts`, exposed as `define` constants, shown on the settings page (API version fetched via the existing no-auth `publicApi`).

**Tech Stack:** .NET 10 (ASP.NET Core minimal API, MSBuild), React 19 + Vite 8 + TypeScript, RTK Query, vitest, xUnit + Moq + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-07-20-app-version-api-and-frontend-design.md` · **ADRs:** 107–111 · **Mock:** MenuNest design system → Screens → issue-41-version

## Global Constraints

- **Version format:** `MAJOR.MINOR.PATCH+<shortSHA>`, base `0.1.0`, SemVer-2.0 build metadata (ADR-107).
- **Canonical short SHA = first 7 chars of the full 40-char commit SHA**, everywhere — CI (`${GITHUB_SHA::7}`), local backend (`git rev-parse HEAD` → `.Substring(0,7)`), local frontend (`GITHUB_SHA`/`git rev-parse HEAD` → `.slice(0,7)`). **Never** `git rev-parse --short` (variable length).
- **Both bases equal, bumped in lockstep** (ADR-111): `frontend/package.json` `version` and backend `<Version>` both `0.1.0`.
- **Embed at build, never runtime config** (ADR-109).
- **API `/version` is public** (`.AllowAnonymous()`), payload `{ version, commit, buildTime }` (ADR-108).
- **Commit hygiene (CLAUDE.md):** every commit ends with `(closes #41)` on the final code task or `(#41)` / `Refs #41` otherwise; stage narrowly with explicit paths (never `git add -A`; never stage `daily-state.md`, `AGENTS.md`, `.claude/settings.json`); never `--no-verify`.
- **Pre-commit hook runs the FULL suite** (backend build+test + frontend `tsc`+build, ~40s+) on every commit — the whole suite must stay green each commit. Requires `node_modules` + restore present (see Setup).
- **Icons = inline SVG, never emoji** (menunest UI convention). Labels stay Thai.

---

## Setup (do once before Task 1)

The worktree was created without dependencies; the pre-commit hook can't pass until they're installed.

- [ ] **S1. Install frontend deps:** `cd frontend && npm ci` (also wires husky). Expected: completes, `frontend/node_modules` present.
- [ ] **S2. Restore backend:** `dotnet restore backend/MenuNest.sln`. Expected: restores all projects.
- [ ] **S3. Baseline green:** `dotnet build backend/MenuNest.sln -c Release` then `dotnet test backend/MenuNest.sln -c Release`; `cd frontend && npx tsc --noEmit && npm run test && npm run build`. Expected: all pass. If anything fails pre-change, STOP and report.
- [ ] **S4. Commit the approved design docs** (already on disk, uncommitted) so the branch carries the design:

```bash
git add CONTEXT.md docs/adr/107-app-version-is-semver-base-plus-short-git-sha.md \
        docs/adr/108-api-exposes-version-via-public-minimal-api-endpoint.md \
        docs/adr/109-version-embedded-at-build-from-git.md \
        docs/adr/110-spa-shows-app-and-api-version-on-settings.md \
        docs/adr/111-frontend-backend-share-semver-base.md \
        docs/superpowers/specs/2026-07-20-app-version-api-and-frontend-design.md \
        docs/superpowers/plans/2026-07-21-app-version-api-and-frontend.md
git commit -m "docs(version): ADR 107-111 + spec + plan for app/API git version (#41)"
```

---

## File Structure

**Backend:**
- `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj` — MODIFY: `<Version>`, git-SHA target, `BuildTimestamp` metadata.
- `backend/src/MenuNest.WebApi/BuildVersion.cs` — CREATE: pure parse helper + reflection reader.
- `backend/src/MenuNest.WebApi/Program.cs` — MODIFY: `MapGet("/version")`.
- `backend/tests/MenuNest.WebApi.UnitTests/BuildVersionTests.cs` — CREATE: unit tests for `BuildVersion.Parse`.

**Frontend:**
- `frontend/src/shared/version/versionCompare.ts` — CREATE: pure `commitOf` / `inSync`.
- `frontend/src/shared/version/versionCompare.test.ts` — CREATE: vitest.
- `frontend/src/shared/version/buildInfo.ts` — CREATE: re-export the injected `define` globals.
- `frontend/vite.config.ts` — MODIFY: compute version, `define` constants.
- `frontend/src/vite-env.d.ts` — MODIFY: declare the globals.
- `frontend/package.json` — MODIFY: `version` → `0.1.0`.
- `frontend/src/shared/api/api.ts` — MODIFY: `getVersion` query on `publicApi` + export hook.
- `frontend/src/pages/settings/SettingsPage.tsx` — MODIFY: add เวอร์ชัน section.
- `frontend/src/pages/settings/SettingsPage.css` — MODIFY: version-block styles.

**CI:**
- `.github/workflows/main_menunest.yml` — MODIFY: pass `SourceRevisionId` to the Build step.

---

## Task 1: Spike & lock the backend version-injection mechanism

**Top risk (spec ⚠).** Prove the MSBuild mechanism before building on it. **No commit** — this is a throwaway probe; leave the csproj clean at the end (Task 2 makes the real edit).

**Files:** temporary edit to `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj` (revert after).

- [ ] **Step 1: Temporarily add** `<Version>0.1.0</Version>` to the first `<PropertyGroup>` of `MenuNest.WebApi.csproj`.

- [ ] **Step 2: Confirm SDK append with an explicit SHA.**

Run: `dotnet build backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj -c Release -p:SourceRevisionId=deadbee`
Then inspect the DLL:
`dotnet exec` is not needed — read the attribute:
```bash
find backend/src/MenuNest.WebApi -path '*/Release/*/MenuNest.WebApi.dll' -print
# in PowerShell:
# [Reflection.Assembly]::LoadFile("<abs path>").GetCustomAttributes([System.Reflection.AssemblyInformationalVersionAttribute],$false)
```
Expected: `InformationalVersion` == `0.1.0+deadbee`.

- [ ] **Step 3: Confirm the local-git fallback + git-less degrade.** Add the `SetGitShaFromLocalRepo` target (see Task 2 for exact XML), build with **no** `-p:SourceRevisionId`. Expected: `InformationalVersion` == `0.1.0+<7-char HEAD sha>`. Then simulate git-less (e.g. build from a copy outside the repo, or temporarily rename `.git` is unsafe — instead trust `ContinueOnError` + the `Condition`); Expected acceptable fallback: `0.1.0` (commit → `local` at read time).

- [ ] **Step 4: Decision gate.**
  - **PASS** (InformationalVersion carries `+sha`) → proceed to Task 2 as written.
  - **FAIL** (target ordering wrong / no append on this SDK) → **fallback:** replace the approach with a generated source file. Add to csproj:
    ```xml
    <Target Name="WriteBuildInfo" BeforeTargets="CoreCompile">
      <Exec Command="git rev-parse HEAD" ConsoleToMSBuild="true" ContinueOnError="true">
        <Output TaskParameter="ConsoleOutput" PropertyName="_FullSha" />
      </Exec>
      <PropertyGroup><_Sha Condition="'$(_FullSha)'!=''">$(_FullSha.Substring(0,7))</_Sha><_Sha Condition="'$(_FullSha)'==''">local</_Sha></PropertyGroup>
      <WriteLinesToFile File="$(IntermediateOutputPath)BuildInfo.g.cs" Overwrite="true"
        Lines="namespace MenuNest.WebApi%3B public static class BuildInfoConst { public const string Version = &quot;0.1.0+$(_Sha)&quot;%3B public const string BuildTime = &quot;$([System.DateTime]::UtcNow.ToString('o'))&quot;%3B } " />
      <ItemGroup><Compile Include="$(IntermediateOutputPath)BuildInfo.g.cs" /></ItemGroup>
    </Target>
    ```
    and have `BuildVersion.Read` read `BuildInfoConst` instead of the assembly attribute. Note the deviation in the task's commit message and update ADR-109 if taken.

- [ ] **Step 5: Revert** the temporary csproj edits (`git checkout -- backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`). Record the outcome (PASS / FALLBACK) for Task 2.

---

## Task 2: Backend — version injection in the csproj

**Files:**
- Modify: `backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj`

**Interfaces:**
- Produces: an assembly whose `AssemblyInformationalVersionAttribute` == `0.1.0+<shortSha>` and an `AssemblyMetadata("BuildTimestamp", <iso-utc>)`.

- [ ] **Step 1: Add `<Version>` to the existing first `<PropertyGroup>`** (after `<UserSecretsId>`):

```xml
    <Version>0.1.0</Version>
```

- [ ] **Step 2: Add the SHA target + build-timestamp metadata** before `</Project>` (use the PASS form; if Task 1 chose FALLBACK, use that form instead):

```xml
  <!-- Auto-version: fill SourceRevisionId from git for local/dev builds when CI
       didn't pass it. Take the FULL sha then truncate to 7 (canonical rule).
       The .NET SDK appends +$(SourceRevisionId) to InformationalVersion. -->
  <Target Name="SetGitShaFromLocalRepo"
          BeforeTargets="AddSourceRevisionToInformationalVersion;GetAssemblyVersion"
          Condition="'$(SourceRevisionId)' == ''">
    <Exec Command="git rev-parse HEAD"
          ConsoleToMSBuild="true" ContinueOnError="true" StandardOutputImportance="low">
      <Output TaskParameter="ConsoleOutput" PropertyName="_FullSha" />
    </Exec>
    <PropertyGroup Condition="'$(_FullSha)' != ''">
      <SourceRevisionId>$(_FullSha.Substring(0, 7))</SourceRevisionId>
    </PropertyGroup>
  </Target>

  <PropertyGroup Condition="'$(BuildTimestamp)' == ''">
    <BuildTimestamp>$([System.DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))</BuildTimestamp>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyMetadata Include="BuildTimestamp" Value="$(BuildTimestamp)" />
  </ItemGroup>
```

- [ ] **Step 3: Verify.** `dotnet build backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj -c Release`; inspect the DLL's `InformationalVersion` == `0.1.0+<HEAD 7-char sha>`. Expected: PASS.

- [ ] **Step 4: Commit.**

```bash
git add backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj
git commit -m "build(api): embed version + git sha + build timestamp in WebApi assembly (#41)"
```

---

## Task 3: Backend — `BuildVersion` helper (TDD) + `/version` endpoint

**Files:**
- Create: `backend/src/MenuNest.WebApi/BuildVersion.cs`
- Create: `backend/tests/MenuNest.WebApi.UnitTests/BuildVersionTests.cs`
- Modify: `backend/src/MenuNest.WebApi/Program.cs` (add `MapGet` after `app.MapControllers();`)

**Interfaces:**
- Produces: `MenuNest.WebApi.BuildVersion.Parse(string?, string?) -> VersionInfo`; `BuildVersion.Read(Assembly?) -> VersionInfo`; `readonly record struct VersionInfo(string Version, string Commit, string? BuildTime)`. Endpoint `GET /version` → `{ version, commit, buildTime }` (camelCase JSON), anonymous.

- [ ] **Step 1: Write the failing test** — `backend/tests/MenuNest.WebApi.UnitTests/BuildVersionTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.WebApi;
using Xunit;

public class BuildVersionTests
{
    [Fact]
    public void Parse_splits_version_and_commit_on_plus()
    {
        var v = BuildVersion.Parse("0.1.0+a1b2c3d", "2026-07-21T04:12:00Z");
        v.Version.Should().Be("0.1.0+a1b2c3d");
        v.Commit.Should().Be("a1b2c3d");
        v.BuildTime.Should().Be("2026-07-21T04:12:00Z");
    }

    [Fact]
    public void Parse_no_plus_yields_local_commit()
    {
        var v = BuildVersion.Parse("0.1.0", null);
        v.Version.Should().Be("0.1.0");
        v.Commit.Should().Be("local");
        v.BuildTime.Should().BeNull();
    }

    [Fact]
    public void Parse_null_or_empty_informational_defaults_to_zero()
    {
        BuildVersion.Parse(null, "").Version.Should().Be("0.0.0");
        BuildVersion.Parse("", null).Commit.Should().Be("local");
        BuildVersion.Parse(null, "").BuildTime.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run — verify it fails.** `dotnet test backend/tests/MenuNest.WebApi.UnitTests -c Release --filter BuildVersionTests`. Expected: FAIL (type `BuildVersion` not found).

- [ ] **Step 3: Implement** `backend/src/MenuNest.WebApi/BuildVersion.cs`:

```csharp
using System.Reflection;

namespace MenuNest.WebApi;

public readonly record struct VersionInfo(string Version, string Commit, string? BuildTime);

public static class BuildVersion
{
    public static VersionInfo Read(Assembly? asm)
    {
        var info = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var buildTime = asm?.GetCustomAttributes<AssemblyMetadataAttribute>()
                            .FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value;
        return Parse(info, buildTime);
    }

    public static VersionInfo Parse(string? informational, string? buildTime)
    {
        var version = string.IsNullOrEmpty(informational) ? "0.0.0" : informational;
        var plus = version.IndexOf('+');
        var commit = plus >= 0 ? version[(plus + 1)..] : "local";
        return new VersionInfo(version, commit, string.IsNullOrEmpty(buildTime) ? null : buildTime);
    }
}
```

- [ ] **Step 4: Run — verify it passes.** Same filter. Expected: PASS (3 tests).

- [ ] **Step 5: Wire the endpoint.** In `Program.cs`, add `using System.Reflection;` at the top with the other usings, and after `app.MapControllers();` add:

```csharp
// Public build-version probe (anonymous). Infra metadata read from the
// assembly at runtime — not a domain use case (ADR-108).
app.MapGet("/version", () =>
{
    var v = BuildVersion.Read(Assembly.GetEntryAssembly());
    return Results.Ok(new { version = v.Version, commit = v.Commit, buildTime = v.BuildTime });
}).AllowAnonymous();
```

- [ ] **Step 6: Manual verify the endpoint.** `dotnet run --project backend/src/MenuNest.WebApi` then in another shell `curl -s http://localhost:5xxx/version` (use the port it logs). Expected: `200` with `{"version":"0.1.0+<sha>","commit":"<sha>","buildTime":"<iso>"}` and **no** auth required. Stop the app.

- [ ] **Step 7: Commit.**

```bash
git add backend/src/MenuNest.WebApi/BuildVersion.cs \
        backend/tests/MenuNest.WebApi.UnitTests/BuildVersionTests.cs \
        backend/src/MenuNest.WebApi/Program.cs
git commit -m "feat(api): add public GET /version reporting build version + commit + time (#41)"
```

---

## Task 4: Frontend — `versionCompare` pure lib (TDD)

**Files:**
- Create: `frontend/src/shared/version/versionCompare.ts`
- Create: `frontend/src/shared/version/versionCompare.test.ts`

**Interfaces:**
- Produces: `commitOf(version: string): string`; `inSync(appCommit: string, apiCommit: string | undefined | null): boolean`.

- [ ] **Step 1: Write the failing test** — `versionCompare.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { commitOf, inSync } from './versionCompare'

describe('commitOf', () => {
  it('returns the part after +', () => expect(commitOf('0.1.0+a1b2c3d')).toBe('a1b2c3d'))
  it('passes through a bare sha', () => expect(commitOf('a1b2c3d')).toBe('a1b2c3d'))
})

describe('inSync', () => {
  it('true when commits equal', () => expect(inSync('a1b2c3d', 'a1b2c3d')).toBe(true))
  it('false when commits differ', () => expect(inSync('a1b2c3d', '9f3e0c1')).toBe(false))
  it('false when api commit missing', () => expect(inSync('a1b2c3d', undefined)).toBe(false))
  it('false when app commit empty', () => expect(inSync('', 'a1b2c3d')).toBe(false))
})
```

- [ ] **Step 2: Run — verify it fails.** `cd frontend && npx vitest run src/shared/version/versionCompare.test.ts`. Expected: FAIL (module not found).

- [ ] **Step 3: Implement** `versionCompare.ts`:

```ts
export function commitOf(version: string): string {
  const i = version.indexOf('+')
  return i >= 0 ? version.slice(i + 1) : version
}

export function inSync(appCommit: string, apiCommit: string | undefined | null): boolean {
  return !!apiCommit && appCommit.length > 0 && appCommit === apiCommit
}
```

- [ ] **Step 4: Run — verify it passes.** Same command. Expected: PASS (6 assertions).

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/shared/version/versionCompare.ts frontend/src/shared/version/versionCompare.test.ts
git commit -m "feat(web): add pure version-compare helpers (commitOf, inSync) (#41)"
```

---

## Task 5: Frontend — build-time version injection

**Files:**
- Modify: `frontend/package.json` (version)
- Modify: `frontend/vite.config.ts`
- Modify: `frontend/src/vite-env.d.ts`
- Create: `frontend/src/shared/version/buildInfo.ts`

**Interfaces:**
- Produces: compile-time globals `__APP_VERSION__`, `__APP_COMMIT__`, `__BUILD_TIME__` (all `string`); re-exported as `APP_VERSION`, `APP_COMMIT`, `BUILD_TIME` from `buildInfo.ts`.

- [ ] **Step 1: Bump base version.** In `frontend/package.json` change `"version": "0.0.0"` → `"version": "0.1.0"`.

- [ ] **Step 2: Compute + inject in `vite.config.ts`** (full replacement):

```ts
/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { readFileSync } from 'node:fs'
import { execSync } from 'node:child_process'

const pkg = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf-8')) as { version: string }

function shortSha(): string {
  let full = process.env.GITHUB_SHA ?? ''
  if (!full) {
    try { full = execSync('git rev-parse HEAD').toString().trim() } catch { /* git-less build */ }
  }
  return full ? full.slice(0, 7) : 'local'   // canonical: first 7 of the full sha
}

const sha = shortSha()

export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(`${pkg.version}+${sha}`),
    __APP_COMMIT__: JSON.stringify(sha),
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
  },
  test: {
    include: ['src/**/*.test.ts'],
    environment: 'node',
  },
})
```

- [ ] **Step 3: Declare the globals** — append to `frontend/src/vite-env.d.ts`:

```ts
declare const __APP_VERSION__: string
declare const __APP_COMMIT__: string
declare const __BUILD_TIME__: string
```

- [ ] **Step 4: Create `frontend/src/shared/version/buildInfo.ts`:**

```ts
// Build-time constants injected by vite.config.ts `define`.
export const APP_VERSION: string = __APP_VERSION__
export const APP_COMMIT: string = __APP_COMMIT__
export const BUILD_TIME: string = __BUILD_TIME__
```

- [ ] **Step 5: Verify build + injection.** `cd frontend && npx tsc --noEmit && npm run build`. Expected: build succeeds; grep the output bundle for the version string to confirm replacement:
`grep -ro "0.1.0+[0-9a-f]\{7\}" dist/assets/*.js | head -1` → prints `0.1.0+<sha>`.

- [ ] **Step 6: Commit.**

```bash
git add frontend/package.json frontend/vite.config.ts frontend/src/vite-env.d.ts frontend/src/shared/version/buildInfo.ts
git commit -m "build(web): inject app version + commit + build time via vite define (#41)"
```

---

## Task 6: Frontend — `getVersion` query on `publicApi`

**Files:**
- Modify: `frontend/src/shared/api/api.ts` (add endpoint to the existing `publicApi`; export the hook)

**Interfaces:**
- Consumes: nothing new.
- Produces: `useGetVersionQuery()` returning `{ data?: { version: string; commit: string; buildTime: string | null }, isLoading, isError }`.

- [ ] **Step 1: Add the endpoint** to the `publicApi` `endpoints: (build) => ({ ... })` block (alongside `getDoctorReport`):

```ts
        getVersion: build.query<{ version: string; commit: string; buildTime: string | null }, void>({
            query: () => '/version',
        }),
```

- [ ] **Step 2: Export the hook.** Find where `publicApi` hooks are exported (e.g. `export const { useGetDoctorReportQuery } = publicApi`) and add `useGetVersionQuery`:

```ts
export const { useGetDoctorReportQuery, useGetVersionQuery } = publicApi
```

(If no such destructure exists yet, add `export const { useGetVersionQuery } = publicApi` near the other exports.)

- [ ] **Step 3: Verify types.** `cd frontend && npx tsc --noEmit`. Expected: PASS.

- [ ] **Step 4: Commit.**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(web): add publicApi getVersion query for GET /version (#41)"
```

---

## Task 7: Frontend — เวอร์ชัน section on the Settings page

**Files:**
- Modify: `frontend/src/pages/settings/SettingsPage.tsx`
- Modify: `frontend/src/pages/settings/SettingsPage.css`

**Interfaces:**
- Consumes: `useGetVersionQuery` (Task 6), `inSync` (Task 4), `APP_VERSION`/`APP_COMMIT`/`BUILD_TIME` (Task 5).

- [ ] **Step 1: Add imports** near the top of `SettingsPage.tsx`:

```ts
import { useUpdateUserSettingsMutation, useGetVersionQuery } from '../../shared/api/api'
import { inSync } from '../../shared/version/versionCompare'
import { APP_VERSION, APP_COMMIT, BUILD_TIME } from '../../shared/version/buildInfo'
```

(merge `useGetVersionQuery` into the existing `../../shared/api/api` import line.)

- [ ] **Step 2: Call the query** inside the component, near the other hooks:

```ts
  const { data: apiVersion, isLoading: apiLoading, isError: apiError } = useGetVersionQuery()
  const buildDate = new Date(BUILD_TIME).toLocaleDateString('th-TH', { day: 'numeric', month: 'short', year: 'numeric' })
```

- [ ] **Step 3: Add the section** as the last `.settings-row` before the closing `{saved && …}` / `</section>`:

```tsx
      <div className="settings-row">
        <div className="settings-row__label">
          <span className="settings-row__icon" aria-hidden="true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
              <path d="M12 2l8 4v6c0 5-3.5 8.2-8 10-4.5-1.8-8-5-8-10V6z" />
              <path d="M9.2 12l1.9 1.9 3.7-3.8" />
            </svg>
          </span>
          <div>
            <div className="settings-row__title">เวอร์ชัน</div>
            <div className="settings-row__sub">รุ่นที่กำลังใช้งานของแอปและ API</div>
          </div>
        </div>

        <div className="settings-version">
          <div className="settings-version__line">
            <span className="settings-version__k">แอป</span>
            <code className="settings-version__v">{APP_VERSION}</code>
          </div>
          <div className="settings-version__line">
            <span className="settings-version__k">API</span>
            {apiLoading && <span className="settings-version__skel" aria-label="กำลังโหลด" />}
            {!apiLoading && apiError && <span className="settings-version__unavail">ไม่พร้อมใช้งาน</span>}
            {!apiLoading && !apiError && apiVersion && (
              <>
                <code className="settings-version__v">{apiVersion.version}</code>
                {inSync(APP_COMMIT, apiVersion.commit)
                  ? <span className="settings-version__badge settings-version__badge--ok">ตรงกัน</span>
                  : <span className="settings-version__badge settings-version__badge--warn">ไม่ตรงกัน</span>}
              </>
            )}
          </div>
          <div className="settings-version__meta">อัปเดตแอปเมื่อ {buildDate}</div>
        </div>
      </div>
```

- [ ] **Step 4: Add styles** to `SettingsPage.css`:

```css
.settings-version { display: flex; flex-direction: column; gap: 9px; min-width: 240px; }
.settings-version__line { display: flex; align-items: center; gap: 10px; font-size: 14px; }
.settings-version__k { width: 32px; color: var(--color-text-muted); font-size: 12.5px; font-weight: 600; }
.settings-version__v { font-family: Consolas, 'SFMono-Regular', ui-monospace, monospace;
  background: #f2efe9; padding: 2px 7px; border-radius: 5px; font-size: 12.5px; color: #8a4b00; }
.settings-version__badge { display: inline-flex; align-items: center; font-size: 11.5px; font-weight: 700;
  border-radius: 99px; padding: 3px 9px; white-space: nowrap; }
.settings-version__badge--ok { color: #2e7d32; background: #e8f5e9; }
.settings-version__badge--warn { color: #b26a00; background: #fff3e0; border: 1px solid #f0d9ad; }
.settings-version__meta { font-size: 12px; color: var(--color-text-muted); }
.settings-version__unavail { color: var(--color-text-muted); font-size: 13px; font-style: italic; }
.settings-version__skel { display: inline-block; width: 118px; height: 13px; border-radius: 4px; background: #ececec; }
```

- [ ] **Step 5: Verify.** `cd frontend && npx tsc --noEmit && npm run test && npm run build`. Expected: all PASS.

- [ ] **Step 6: Commit.**

```bash
git add frontend/src/pages/settings/SettingsPage.tsx frontend/src/pages/settings/SettingsPage.css
git commit -m "feat(web): show app + API version on the settings page (#41)"
```

---

## Task 8: CI — pass the short SHA to the backend build

**Files:**
- Modify: `.github/workflows/main_menunest.yml` (the `Build` step)

- [ ] **Step 1: Edit the Build step** to pass the canonical short SHA:

```yaml
      - name: Build
        run: dotnet build backend/MenuNest.sln --configuration Release --no-restore -p:SourceRevisionId=${GITHUB_SHA::7}
```

- [ ] **Step 2: Sanity-check YAML** (indentation, single line). No local run possible; confirm by re-reading the file.

- [ ] **Step 3: Commit (closes the issue).**

```bash
git add .github/workflows/main_menunest.yml
git commit -m "ci(api): stamp deployed build with the commit short sha (closes #41)"
```

---

## Post-implementation (outside the task loop)

- **Interactive verify on prod after deploy** (SPA has no render gate — CLAUDE.md): open `/settings`; confirm app + API versions show and, after both pipelines finish, the SHAs match (green ตรงกัน); `curl https://<api>/version` returns 200 anonymously with CORS headers for the SPA origin; block `/version` (devtools offline) → API row shows ไม่พร้อมใช้งาน without breaking the page.
- **Wrap:** merge branch → `main`, then delete the worktree (the user's stated close-out).

---

## Self-Review

**Spec coverage:** format (Task 2/5) · public `/version` (Task 3) · build-embed + auto-everywhere + buildTime (Tasks 2,3,5) · SPA display + match badge + silent-fail (Task 7) · shared base 0.1.0 (Tasks 2,5) · CI edit (Task 8) · canonical SHA rule (Global Constraints, Tasks 2,5,8) · MSBuild spike-first (Task 1) · design docs committed (Setup S4). All spec sections map to a task.

**Placeholder scan:** none — every code/step is concrete. The one conditional is Task 1's PASS/FALLBACK gate, which carries the full fallback XML.

**Type consistency:** `VersionInfo(Version, Commit, BuildTime)` used identically in Task 3 helper, test, and endpoint. `{ version, commit, buildTime }` payload matches between Task 3 (endpoint), Task 6 (query type), and Task 7 (consumption). `commitOf`/`inSync` signatures match between Task 4 (def) and Task 7 (use). `APP_VERSION`/`APP_COMMIT`/`BUILD_TIME` defined in Task 5, consumed in Task 7. `__APP_VERSION__` etc. declared (Task 5 vite-env) and injected (Task 5 vite.config).

**Hook/setup:** Setup installs deps so every task's commit-time full-suite hook can pass; commits are task-level (not per micro-step) to keep hook runs bounded.
