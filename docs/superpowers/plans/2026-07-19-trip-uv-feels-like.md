# UV Index + Feels-Like at the Destination (issue #40) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show each itinerary Stop's UV index + feels-like temperature (full in the detail sheet, both readings) and warn on the compact card when the on-arrival conditions cross the User's own thresholds, set on a new `/settings` page.

**Architecture:** UV + feels-like are parsed from the Google Weather responses MenuNest already fetches (no new call). Thresholds are two nullable-int columns on the existing `UserSettings` entity (tri-state: `null`=default, `0`=off, `N`=value), read via `GET /api/me` and written via the existing `PUT /api/me/settings` (full-replace snapshot). The card alert is a pure `lib/weather.ts` function evaluated on the On-arrival reading only.

**Tech Stack:** .NET (Clean Architecture, Mediator CQRS, EF Core, FluentValidation, xUnit + Moq + FluentAssertions) · React + TypeScript + RTK Query + Vitest (node env, no component harness).

**Spec:** `docs/superpowers/specs/2026-07-19-trip-uv-feels-like-design.md` · **ADRs:** 086–093 · **Glossary:** CONTEXT.md (UV index, UV band, Feels-like, Weather alert, Weather-alert threshold)

## Global Constraints

- **Every commit must leave the WHOLE suite green.** `frontend/.husky/pre-commit` runs backend `dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` + `npm run build` on every commit (~40s). Do not `--no-verify`.
- **EF: entity + config + migration land in the SAME commit** (unmapped/invalid model fails EF validation for every DbContext test). Here no new `DbSet` is added (the `UserSettings` entity already exists) — only two columns.
- **Migrations are applied to prod MANUALLY** (app + CD do not auto-migrate). See Task 7.
- **Stage narrowly:** `git add <explicit paths>` — NEVER `git add -A` / `git add .`. Never stage `daily-state.md`, `AGENTS.md`, or `.claude/settings.json`.
- **Commits reference #40:** the final commit that completes the feature ends with `(closes #40)`; earlier commits use `(#40)`.
- **git remote is `main`** (not `origin`): push with `git push main HEAD:main`. `gh` needs `--repo ThodsaphonSonthiphin/MenuNest`.
- **No emoji in UI** — inline SVG or `@syncfusion/react-icons` only (the existing NavBar/category emoji are pre-existing legacy, untouched).
- **Threshold tri-state encoding (verbatim):** `null` = built-in default (UV `6` / feels-like `40`); `0` = off (never warn); positive `N` = that threshold.
- **UV band words (canonical, from CONTEXT.md):** 0–2 `ต่ำ` · 3–5 `ปานกลาง` · 6–7 `สูง` · 8–10 `สูงมาก` · 11+ `อันตราย`.
- **SPA has NO component/visual test harness** — rendering/layout/CSS is verified with `tsc -b` + `npm run build` + an **interactive smoke test** (run the app / Chrome DevTools) BEFORE push (Task 7). Pure logic lives in `lib/` modules with vitest coverage.
- **Backend tests use Moq** (not NSubstitute); relational tests use `SqliteAppDbContext`.
- **Environment note (this machine):** a write-guard may block Edit/Write outside `…\menunest\backend`. If it fires on a frontend/docs file, write via PowerShell `[IO.File]::WriteAllText($p,$c,[Text.UTF8Encoding]::new($false))` or surgical `ReadAllText`→`.Replace()` on a single-line anchor.

---

## File Structure

**Backend — modify:**
- `src/MenuNest.Application/Abstractions/IWeatherService.cs` — `WeatherReading` record +`UvIndex`,`FeelsLikeC`
- `src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs` — parse the two fields in `ParseReading`; `NoData` passes nulls
- `src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs` — the no-op `WeatherReading` ctor gains two nulls
- `src/MenuNest.Domain/Entities/UserSettings.cs` — `UvWarnThreshold`,`FeelsLikeWarnThreshold` (`int?`) + `SetWeatherAlerts`
- `src/MenuNest.Application/UseCases/Me/MeDto.cs` — +2 fields
- `src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs` — map +2
- `src/MenuNest.Application/UseCases/Me/UserSettingsDto.cs` — +2 fields
- `src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsCommand.cs` — +2 params (defaults)
- `src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsHandler.cs` — call `SetWeatherAlerts`; return extended DTO
- `src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsValidator.cs` — bound the two ints

**Backend — create (generated):**
- `src/MenuNest.Infrastructure/Migrations/<ts>_AddWeatherAlertSettings.cs` — via `dotnet ef migrations add`

**Backend — tests:**
- `tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs` — parse UV/feels
- `tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherHandlerTests.cs` — `StubWeather` ctor +2 args
- `tests/MenuNest.Application.UnitTests/Me/UpdateUserSettingsHandlerTests.cs` — thresholds persist/return
- `tests/MenuNest.Application.UnitTests/Me/GetMeHandlerTests.cs` — thresholds surface

**Frontend — modify:**
- `src/shared/api/api.ts` — `WeatherReadingDto` +2, `MeDto` +2, `updateUserSettings` body/response
- `src/pages/trips/hooks/useStopWeather.ts` — `noData` +2 nulls
- `src/pages/trips/lib/weather.ts` — `uvBand`, `effectiveThreshold`, `weatherAlertBadges`, `UV_WARN_DEFAULT`, `FEELS_WARN_DEFAULT`
- `src/pages/trips/lib/weather.test.ts` — new tests + fix `WeatherReadingDto` literals
- `src/pages/trips/components/WeatherChip.tsx` — feels-like + UV badge
- `src/pages/trips/components/WeatherIcons.tsx` — add `SunIcon`, `ThermoIcon` (inline SVG)
- `src/pages/trips/trips-tokens.css` — UV band tokens, chip `.uv`/`.feels`, card alert pills
- `src/pages/trips/lib/stopSummary.ts` — `alerts` output + threshold inputs
- `src/pages/trips/lib/stopSummary.test.ts` — alert tests + fix literal factory
- `src/pages/trips/components/ItineraryStopCard.tsx` — render alert pills; threshold props
- `src/pages/trips/components/ItineraryTab.tsx` — thread thresholds from `useGetMeQuery`
- `src/router.tsx` — add `/settings` route (do NOT touch `/` → `HomeRedirect`)
- `src/shared/components/NavBar.tsx` — "Settings" entry (account menu + drawer)

**Frontend — create:**
- `src/pages/settings/SettingsPage.tsx`, `SettingsPage.css`, `index.ts`
- `src/pages/settings/weatherAlertOptions.ts` + `weatherAlertOptions.test.ts`
- (`src/pages/settings/homeOptions.ts` + `HomeRedirect` already exist under #39 — do NOT modify)

---
## Task 1: Backend — WeatherReading carries UV index + feels-like (parsed from the existing responses)

**Files:**
- Modify: `backend/src/MenuNest.Application/Abstractions/IWeatherService.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs`
- Test (fix ctor): `backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherHandlerTests.cs`

**Interfaces:**
- Produces: `WeatherReading(string StopId, bool HasData, string? ConditionType, string? IconBaseUri, double? TempC, int? RainPct, string? Description, int? UvIndex, double? FeelsLikeC)`

- [ ] **Step 1: Write the failing tests** — add to `GoogleWeatherServiceTests.cs`:

```csharp
[Fact]
public async Task Now_parses_uv_index_and_feels_like()
{
    const string json =
        "{\"weatherCondition\":{\"type\":\"CLEAR\"}," +
        "\"temperature\":{\"degrees\":34.0}," +
        "\"feelsLikeTemperature\":{\"unit\":\"CELSIUS\",\"degrees\":39.4}," +
        "\"uvIndex\":9}";
    var svc = Build(new StubHandler(HttpStatusCode.OK, json));

    var r = (await svc.GetReadingsAsync(OnePoint, WeatherReadingKind.Now, CancellationToken.None))[0];

    r.HasData.Should().BeTrue();
    r.UvIndex.Should().Be(9);
    r.FeelsLikeC.Should().Be(39.4);
}

[Fact]
public async Task OnArrival_parses_uv_index_and_feels_like()
{
    const string json =
        "{\"forecastHours\":[" +
        "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":14}," +
        "\"weatherCondition\":{\"type\":\"CLEAR\"},\"temperature\":{\"degrees\":31.0}," +
        "\"feelsLikeTemperature\":{\"degrees\":35.2},\"uvIndex\":2}]}";
    var svc = Build(new StubHandler(HttpStatusCode.OK, json));
    var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 14, 30, 0)) };

    var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

    r.UvIndex.Should().Be(2);
    r.FeelsLikeC.Should().Be(35.2);
}
```

- [ ] **Step 2: Run the tests to verify they fail (do not compile yet)**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~GoogleWeatherServiceTests"`
Expected: FAIL — `WeatherReading` has no `UvIndex`/`FeelsLikeC`.

- [ ] **Step 3: Add the two fields to the record** in `IWeatherService.cs`:

```csharp
public sealed record WeatherReading(
    string StopId, bool HasData, string? ConditionType, string? IconBaseUri,
    double? TempC, int? RainPct, string? Description,
    int? UvIndex, double? FeelsLikeC);
```

- [ ] **Step 4: Parse the fields** in `GoogleWeatherService.cs` `ParseReading` (after the `rain` line, before `hasData`), and update the return + `NoData`:

```csharp
int? uv = el.TryGetProperty("uvIndex", out var uvi) && uvi.ValueKind == JsonValueKind.Number
    ? uvi.GetInt32() : null;
double? feels = el.TryGetProperty("feelsLikeTemperature", out var fl)
    && fl.TryGetProperty("degrees", out var fd) ? fd.GetDouble() : null;
var hasData = type is not null || temp is not null;
return new WeatherReading(stopId, hasData, type, icon, temp, rain, desc, uv, feels);
```

```csharp
private static WeatherReading NoData(string stopId) => new(stopId, false, null, null, null, null, null, null, null);
```

- [ ] **Step 5: Update the other two `WeatherReading` construction sites** (they only pass the existing values; add the two nulls / sample):

In `MissingConfigWeatherService.cs`:
```csharp
points.Select(p => new WeatherReading(p.StopId, false, null, null, null, null, null, null, null)).ToList());
```
In `GetStopWeatherHandlerTests.cs` `StubWeather` (sample values; not asserted there):
```csharp
.Select(p => new WeatherReading(p.StopId, true, "CLOUDY", "https://maps.gstatic.com/weather/v1/cloudy", 29.1, 20, "มีเมฆมาก", 7, 33.0))
```

- [ ] **Step 6: Run the parse tests + the full Application suite**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests`
Expected: PASS (the two new tests pass; all existing weather/handler tests still pass).

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/Abstractions/IWeatherService.cs \
  backend/src/MenuNest.Infrastructure/Maps/GoogleWeatherService.cs \
  backend/src/MenuNest.Infrastructure/Maps/MissingConfigWeatherService.cs \
  backend/tests/MenuNest.Application.UnitTests/Trips/Maps/GoogleWeatherServiceTests.cs \
  backend/tests/MenuNest.Application.UnitTests/Trips/GetStopWeatherHandlerTests.cs
git commit -m "feat(trips): parse UV index + feels-like from weather responses (#40)"
```

---

## Task 2: Backend — UserSettings weather-alert thresholds (storage, read, write) + migration

One commit: entity columns + config + migration + command/handler/validator + DTO + MeDto + GetMe (EF model must stay valid across the whole suite).

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/UserSettings.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Me/MeDto.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Me/UserSettingsDto.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsValidator.cs`
- Create (generated): `backend/src/MenuNest.Infrastructure/Migrations/<ts>_AddWeatherAlertSettings.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Me/UpdateUserSettingsHandlerTests.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Me/GetMeHandlerTests.cs`

**Interfaces:**
- Consumes: `WeatherReading` (unrelated).
- Produces: `UserSettings.SetWeatherAlerts(int? uv, int? feels)`; `UserSettings.UvWarnThreshold`/`FeelsLikeWarnThreshold` (`int?`); `MeDto(..., int? UvWarnThreshold, int? FeelsLikeWarnThreshold)`; `UpdateUserSettingsCommand(string? HomePath, int? UvWarnThreshold = null, int? FeelsLikeWarnThreshold = null)`; `UserSettingsDto(string? HomePath, int? UvWarnThreshold, int? FeelsLikeWarnThreshold)`.

- [ ] **Step 1: Write the failing tests.** Add to `UpdateUserSettingsHandlerTests.cs`:

```csharp
[Fact]
public async Task Persists_and_returns_weather_alert_thresholds()
{
    using var conn = new SqliteConnection("DataSource=:memory:");
    conn.Open();
    using var ctx = NewContext(conn);
    var user = User.CreateFromExternalLogin("ext-wx", "w@b.com", "W", AuthProvider.Microsoft);
    ctx.Users.Add(user);
    await ctx.SaveChangesAsync();
    var handler = NewHandler(ctx, user);

    var r = await handler.Handle(new UpdateUserSettingsCommand("/trips", 8, 0), CancellationToken.None);

    r.UvWarnThreshold.Should().Be(8);
    r.FeelsLikeWarnThreshold.Should().Be(0);
    var loaded = await ctx.UserSettings.SingleAsync();
    loaded.UvWarnThreshold.Should().Be(8);
    loaded.FeelsLikeWarnThreshold.Should().Be(0);
}
```

Add to `GetMeHandlerTests.cs`:

```csharp
[Fact]
public async Task Returns_weather_alert_thresholds_from_settings()
{
    using var conn = new SqliteConnection("DataSource=:memory:");
    conn.Open();
    using var ctx = NewContext(conn);
    var user = User.CreateFromExternalLogin("ext-wx", "w@b.com", "W", AuthProvider.Microsoft);
    ctx.Users.Add(user);
    var settings = UserSettings.Create(user.Id);
    settings.SetWeatherAlerts(6, 40);
    ctx.UserSettings.Add(settings);
    await ctx.SaveChangesAsync();
    var provisioner = new Mock<IUserProvisioner>();
    provisioner.Setup(p => p.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(user);

    var me = await new GetMeHandler(provisioner.Object, ctx).Handle(new GetMeQuery(), CancellationToken.None);

    me.UvWarnThreshold.Should().Be(6);
    me.FeelsLikeWarnThreshold.Should().Be(40);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~UpdateUserSettingsHandlerTests|FullyQualifiedName~GetMeHandlerTests"`
Expected: FAIL (no `SetWeatherAlerts`, no `UvWarnThreshold` on DTO/MeDto).

- [ ] **Step 3: Add the columns + mutator** to `UserSettings.cs` (after `HomePath`):

```csharp
/// <summary>UV-index warn threshold. Null = default (6); 0 = off; N = warn at UV >= N.</summary>
public int? UvWarnThreshold { get; private set; }
/// <summary>Feels-like warn threshold in C. Null = default (40); 0 = off; N = warn at feels >= N.</summary>
public int? FeelsLikeWarnThreshold { get; private set; }

public void SetWeatherAlerts(int? uv, int? feels)
{
    UvWarnThreshold = uv;
    FeelsLikeWarnThreshold = feels;
    UpdatedAt = DateTime.UtcNow;
}
```

(Nullable `int` properties auto-map to `int NULL` — no `UserSettingsConfiguration` change is required. Verify that file does not `Ignore` unknown members.)

- [ ] **Step 4: Extend the read path.** `MeDto.cs` — append two params:

```csharp
    string? HomePath,
    int? UvWarnThreshold,
    int? FeelsLikeWarnThreshold);
```

`GetMeHandler.cs` — extend the returned `MeDto`:

```csharp
    HomePath: settings?.HomePath,
    UvWarnThreshold: settings?.UvWarnThreshold,
    FeelsLikeWarnThreshold: settings?.FeelsLikeWarnThreshold);
```

- [ ] **Step 5: Extend the write path.** `UserSettingsDto.cs`:

```csharp
public sealed record UserSettingsDto(string? HomePath, int? UvWarnThreshold, int? FeelsLikeWarnThreshold);
```

`UpdateUserSettingsCommand.cs` (defaults keep the existing positional `new UpdateUserSettingsCommand("/x")` test calls compiling):

```csharp
public sealed record UpdateUserSettingsCommand(
    string? HomePath, int? UvWarnThreshold = null, int? FeelsLikeWarnThreshold = null) : ICommand<UserSettingsDto>;
```

`UpdateUserSettingsHandler.cs` — after `settings.SetHomePath(command.HomePath);`:

```csharp
settings.SetHomePath(command.HomePath);
settings.SetWeatherAlerts(command.UvWarnThreshold, command.FeelsLikeWarnThreshold);
await _db.SaveChangesAsync(ct);
return new UserSettingsDto(settings.HomePath, settings.UvWarnThreshold, settings.FeelsLikeWarnThreshold);
```

`UpdateUserSettingsValidator.cs` — add lenient bounds:

```csharp
RuleFor(x => x.UvWarnThreshold).InclusiveBetween(0, 15)
    .When(x => x.UvWarnThreshold.HasValue).WithMessage("UvWarnThreshold must be 0..15.");
RuleFor(x => x.FeelsLikeWarnThreshold).InclusiveBetween(0, 60)
    .When(x => x.FeelsLikeWarnThreshold.HasValue).WithMessage("FeelsLikeWarnThreshold must be 0..60.");
```

- [ ] **Step 6: Run the two tests + full Application suite**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests`
Expected: PASS. (Confirm no other `new MeDto(` site broke — `GetMeHandler` is the only constructor.)

- [ ] **Step 7: Generate the migration**

Run: `cd backend && dotnet ef migrations add AddWeatherAlertSettings --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi`
Expected: a new `Migrations/<ts>_AddWeatherAlertSettings.cs` adding two nullable `int` columns to `UserSettings`. Do NOT apply to a DB here (Task 7).

- [ ] **Step 8: Full backend build + test (matches pre-commit)**

Run: `cd backend && dotnet build -c Release && dotnet test -c Release`
Expected: PASS across all test projects.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/UserSettings.cs \
  backend/src/MenuNest.Application/UseCases/Me/MeDto.cs \
  backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs \
  backend/src/MenuNest.Application/UseCases/Me/UserSettingsDto.cs \
  backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsCommand.cs \
  backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsHandler.cs \
  backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsValidator.cs \
  backend/src/MenuNest.Infrastructure/Migrations/ \
  backend/tests/MenuNest.Application.UnitTests/Me/UpdateUserSettingsHandlerTests.cs \
  backend/tests/MenuNest.Application.UnitTests/Me/GetMeHandlerTests.cs
git commit -m "feat(settings): persist per-User UV + feels-like warn thresholds (#40)"
```

---

## Task 3: Frontend — types + pure weather-alert logic

**Files:**
- Modify: `frontend/src/shared/api/api.ts`
- Modify: `frontend/src/pages/trips/hooks/useStopWeather.ts`
- Modify: `frontend/src/pages/trips/lib/weather.ts`
- Test: `frontend/src/pages/trips/lib/weather.test.ts`
- Fix literal: `frontend/src/pages/trips/lib/stopSummary.test.ts`

**Interfaces:**
- Consumes: backend `WeatherReading`/`MeDto` shapes (Tasks 1–2).
- Produces: `WeatherReadingDto` +`uvIndex:number|null`,`feelsLikeC:number|null`; `MeDto` +`uvWarnThreshold:number|null`,`feelsLikeWarnThreshold:number|null`; `uvBand(uv):{key,word}`; `effectiveThreshold(stored,dflt):number|null`; `weatherAlertBadges(arrival,uvStored,feelsStored):{uv?:number;feels?:number}`; `UV_WARN_DEFAULT=6`, `FEELS_WARN_DEFAULT=40`.

- [ ] **Step 1: Write the failing tests.** Add to `weather.test.ts` (and add `import type {WeatherReadingDto} from '../../../shared/api/api'` + the new names to the existing `./weather` import):

```ts
describe('uvBand', () => {
  it('bands the WHO scale', () => {
    expect(uvBand(2).word).toBe('ต่ำ')
    expect(uvBand(3).key).toBe('mod')
    expect(uvBand(5).word).toBe('ปานกลาง')
    expect(uvBand(6).key).toBe('high')
    expect(uvBand(7).word).toBe('สูง')
    expect(uvBand(8).key).toBe('vhigh')
    expect(uvBand(10).word).toBe('สูงมาก')
    expect(uvBand(11).key).toBe('ext')
    expect(uvBand(13).word).toBe('อันตราย')
  })
})

describe('effectiveThreshold', () => {
  it('null → default', () => expect(effectiveThreshold(null, 6)).toBe(6))
  it('undefined → default', () => expect(effectiveThreshold(undefined, 40)).toBe(40))
  it('0 → off', () => expect(effectiveThreshold(0, 6)).toBeNull())
  it('N → N', () => expect(effectiveThreshold(8, 6)).toBe(8))
})

const wr = (over: Partial<WeatherReadingDto>): WeatherReadingDto => ({
  stopId: 's', hasData: true, conditionType: null, iconBaseUri: null,
  tempC: 30, rainPct: 0, description: null, uvIndex: null, feelsLikeC: null, ...over,
})

describe('weatherAlertBadges', () => {
  it('empty without a usable arrival reading', () => {
    expect(weatherAlertBadges(undefined, null, null)).toEqual({})
    expect(weatherAlertBadges(wr({hasData: false}), null, null)).toEqual({})
  })
  it('uv badge at/above default 6', () => expect(weatherAlertBadges(wr({uvIndex: 9}), null, null)).toEqual({uv: 9}))
  it('no uv badge below threshold', () => expect(weatherAlertBadges(wr({uvIndex: 2}), null, null)).toEqual({}))
  it('feels badge rounds vs default 40', () => expect(weatherAlertBadges(wr({feelsLikeC: 40.4}), null, null)).toEqual({feels: 40}))
  it('0 disables an axis', () => expect(weatherAlertBadges(wr({uvIndex: 11, feelsLikeC: 45}), 0, 0)).toEqual({}))
  it('custom thresholds both fire', () => expect(weatherAlertBadges(wr({uvIndex: 3, feelsLikeC: 38}), 3, 38)).toEqual({uv: 3, feels: 38}))
})
```

- [ ] **Step 2: Run to verify fail**

Run: `cd frontend && npx vitest run src/pages/trips/lib/weather.test.ts`
Expected: FAIL — `uvBand`/`effectiveThreshold`/`weatherAlertBadges` not exported.

- [ ] **Step 3: Implement the helpers** — append to `weather.ts`:

```ts
export const UV_WARN_DEFAULT = 6
export const FEELS_WARN_DEFAULT = 40

export type UvBandKey = 'low' | 'mod' | 'high' | 'vhigh' | 'ext'
/** WHO UV band → key + canonical Thai word (CONTEXT.md). */
export function uvBand(uv: number): {key: UvBandKey; word: string} {
  if (uv <= 2) return {key: 'low', word: 'ต่ำ'}
  if (uv <= 5) return {key: 'mod', word: 'ปานกลาง'}
  if (uv <= 7) return {key: 'high', word: 'สูง'}
  if (uv <= 10) return {key: 'vhigh', word: 'สูงมาก'}
  return {key: 'ext', word: 'อันตราย'}
}

/** Tri-state stored threshold → effective value. null/undefined → default; 0 → null (off); N → N. */
export function effectiveThreshold(stored: number | null | undefined, dflt: number): number | null {
  if (stored == null) return dflt
  if (stored === 0) return null
  return stored
}

/** Compact-card alert (On-arrival only, ADR-092): which threshold-crossing badges to show. */
export function weatherAlertBadges(
  arrival: WeatherReadingDto | undefined,
  uvStored: number | null,
  feelsStored: number | null,
): {uv?: number; feels?: number} {
  if (!arrival || !arrival.hasData) return {}
  const out: {uv?: number; feels?: number} = {}
  const uvT = effectiveThreshold(uvStored, UV_WARN_DEFAULT)
  if (uvT != null && arrival.uvIndex != null && arrival.uvIndex >= uvT) out.uv = arrival.uvIndex
  const feelsT = effectiveThreshold(feelsStored, FEELS_WARN_DEFAULT)
  if (feelsT != null && arrival.feelsLikeC != null && Math.round(arrival.feelsLikeC) >= feelsT) {
    out.feels = Math.round(arrival.feelsLikeC)
  }
  return out
}
```

- [ ] **Step 4: Extend the DTO types + fix every literal.** In `api.ts`:

```ts
export interface WeatherReadingDto {
    stopId: string; hasData: boolean; conditionType: string | null; iconBaseUri: string | null
    tempC: number | null; rainPct: number | null; description: string | null
    uvIndex: number | null; feelsLikeC: number | null
}
export interface MeDto {
    // …existing…
    homePath: string | null
    uvWarnThreshold: number | null
    feelsLikeWarnThreshold: number | null
}
```

Extend the `updateUserSettings` mutation body + response to the full snapshot:

```ts
updateUserSettings: build.mutation<
    { homePath: string | null; uvWarnThreshold: number | null; feelsLikeWarnThreshold: number | null },
    { homePath: string | null; uvWarnThreshold: number | null; feelsLikeWarnThreshold: number | null }
>({
    query: (body) => ({ url: '/api/me/settings', method: 'PUT', body }),
    invalidatesTags: ['Me'],
}),
```

In `useStopWeather.ts`, the `noData` factory:

```ts
const noData = (stopId: string): WeatherReadingDto => ({
  stopId, hasData: false, conditionType: null, iconBaseUri: null, tempC: null, rainPct: null,
  description: null, uvIndex: null, feelsLikeC: null,
})
```

In `weather.test.ts`, add `uvIndex: null, feelsLikeC: null` to the two inline `weatherChipState(...)` reading literals (the `hasData:false` and `hasData:true` cases). In `stopSummary.test.ts`, add `uvIndex: null, feelsLikeC: null` to the `reading` factory defaults. Grep to be sure none remain:

Run: `cd frontend && grep -rln "hasData:" src | xargs grep -Ln "uvIndex"` (any file listed still needs the two fields).

- [ ] **Step 5: Run vitest + tsc**

Run: `cd frontend && npx vitest run src/pages/trips/lib/weather.test.ts && npx tsc -b`
Expected: weather tests PASS; `tsc` reports no errors (all `WeatherReadingDto` literals complete). Also confirm no caller of `useUpdateUserSettingsMutation` broke: `grep -rn "useUpdateUserSettingsMutation" src` (expected: none yet besides the export).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/api/api.ts frontend/src/pages/trips/hooks/useStopWeather.ts \
  frontend/src/pages/trips/lib/weather.ts frontend/src/pages/trips/lib/weather.test.ts \
  frontend/src/pages/trips/lib/stopSummary.test.ts
git commit -m "feat(trips): UV band + feels-like alert helpers and DTO fields (#40)"
```

---
## Task 4: Frontend — detail-sheet chips show feels-like + UV badge

UI-only (rendering verified in Task 7). The `uvBand` logic it uses is already unit-tested (Task 3).

**Files:**
- Modify: `frontend/src/pages/trips/components/WeatherChip.tsx`
- Modify: `frontend/src/pages/trips/components/WeatherIcons.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: `uvBand`, `WeatherReadingDto.uvIndex/feelsLikeC` (Task 3); `SunIcon` (new).

- [ ] **Step 1: Add the icons** to `WeatherIcons.tsx` (match the file's existing export style; inline SVG, no emoji):

```tsx
export function SunIcon() {
  return (
    <svg viewBox="0 0 24 24" width={13} height={13} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" aria-hidden="true">
      <circle cx="12" cy="12" r="4.2" />
      <path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M19.1 4.9l-1.4 1.4M6.3 17.7l-1.4 1.4" />
    </svg>
  )
}
export function ThermoIcon() {
  return (
    <svg viewBox="0 0 24 24" width={12} height={12} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M14 14.76V5a2 2 0 1 0-4 0v9.76a4 4 0 1 0 4 0z" />
    </svg>
  )
}
```

- [ ] **Step 2: Render feels-like + UV badge** in `WeatherChip.tsx` — extend the imports and, in the `data` branch, after the `tempC` span:

```tsx
import {iconUrl, isRainy, weatherChipState, uvBand} from '../lib/weather'
import {RainDropIcon, NoWeatherIcon, SunIcon} from './WeatherIcons'
```

```tsx
      {r.tempC != null && <span className="t">{Math.round(r.tempC)}°</span>}
      {r.feelsLikeC != null && <span className="feels">รู้สึก {Math.round(r.feelsLikeC)}°</span>}
      {r.uvIndex != null && (() => {
        const b = uvBand(r.uvIndex)
        return <span className={`uv ${b.key}`}><SunIcon /> UV {r.uvIndex} {b.word}</span>
      })()}
```

- [ ] **Step 3: Add the styles** to `trips-tokens.css` (tokens + chip styles):

```css
:root {
  --uv-low:#2f8f3e;  --uv-low-bg:#e7f6e8;
  --uv-mod:#a6791a;  --uv-mod-bg:#fdf3d7;
  --uv-high:#c85e0c; --uv-high-bg:#ffe6cf;
  --uv-vhigh:#c5321f;--uv-vhigh-bg:#fdeceb;
  --uv-ext:#7d379f;  --uv-ext-bg:#f3e8fb;
}
.chip.wx .feels { font-size:11px; font-weight:600; color:#64748b; }
.chip.wx .uv { display:inline-flex; align-items:center; gap:4px; border-radius:999px;
  font-size:11px; font-weight:800; padding:2px 8px; white-space:nowrap; }
.chip.wx .uv.low   { background:var(--uv-low-bg);   color:var(--uv-low); }
.chip.wx .uv.mod   { background:var(--uv-mod-bg);   color:var(--uv-mod); }
.chip.wx .uv.high  { background:var(--uv-high-bg);  color:var(--uv-high); }
.chip.wx .uv.vhigh { background:var(--uv-vhigh-bg); color:var(--uv-vhigh); }
.chip.wx .uv.ext   { background:var(--uv-ext-bg);   color:var(--uv-ext); }
```

- [ ] **Step 4: Typecheck + build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/WeatherChip.tsx \
  frontend/src/pages/trips/components/WeatherIcons.tsx \
  frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): show UV badge + feels-like in the stop detail sheet (#40)"
```

---

## Task 5: Frontend — compact-card weather-alert badge (On-arrival only)

**Files:**
- Modify: `frontend/src/pages/trips/lib/stopSummary.ts`
- Test: `frontend/src/pages/trips/lib/stopSummary.test.ts`
- Modify: `frontend/src/pages/trips/components/ItineraryStopCard.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: `weatherAlertBadges` (Task 3), `SunIcon`/`ThermoIcon` (Task 4), `useGetMeQuery` (`uvWarnThreshold`/`feelsLikeWarnThreshold`).
- Produces: `StopSummary.alerts: {uv?: number; feels?: number}`; `buildStopSummary({..., uvWarn?, feelsWarn?})`.

- [ ] **Step 1: Write the failing tests** — add to `stopSummary.test.ts`:

```ts
it('flags UV on arrival at/above the default threshold', () => {
  const s = buildStopSummary({arrivalReading: reading({uvIndex: 9}), dwellMinutes: 60, flag: null})
  expect(s.alerts).toEqual({uv: 9})
})
it('flags feels-like at/above the default threshold', () => {
  const s = buildStopSummary({arrivalReading: reading({feelsLikeC: 41}), dwellMinutes: 60, flag: null})
  expect(s.alerts).toEqual({feels: 41})
})
it('no alerts when both axes are turned off (0)', () => {
  const s = buildStopSummary({arrivalReading: reading({uvIndex: 11, feelsLikeC: 45}), dwellMinutes: 60, flag: null, uvWarn: 0, feelsWarn: 0})
  expect(s.alerts).toEqual({})
})
it('no alerts when arrival has no data', () => {
  const s = buildStopSummary({arrivalReading: reading({hasData: false, uvIndex: 11}), dwellMinutes: 60, flag: null})
  expect(s.alerts).toEqual({})
})
```

- [ ] **Step 2: Run to verify fail**

Run: `cd frontend && npx vitest run src/pages/trips/lib/stopSummary.test.ts`
Expected: FAIL — `s.alerts` is undefined.

- [ ] **Step 3: Implement** in `stopSummary.ts` — import the helper, extend the interface + the signature + the return:

```ts
import {isRainy, weatherAlertBadges} from './weather'
```

```ts
export interface StopSummary {
  weather: {iconBaseUri: string | null; label: string} | null
  dwellText: string
  flag: {severity: FlagSeverity; label: string} | null
  alerts: {uv?: number; feels?: number}
}

export function buildStopSummary({
  arrivalReading,
  dwellMinutes,
  flag,
  uvWarn,
  feelsWarn,
}: {
  arrivalReading?: WeatherReadingDto
  dwellMinutes: number
  flag: StopFlag
  uvWarn?: number | null
  feelsWarn?: number | null
}): StopSummary {
  // …existing weather/label logic unchanged…
  return {
    weather,
    dwellText: `อยู่ ${formatDurationMinutes(dwellMinutes)}`,
    flag: flag ? {severity: flag.severity, label: flagText(flag).reasonLine} : null,
    alerts: weatherAlertBadges(arrivalReading, uvWarn ?? null, feelsWarn ?? null),
  }
}
```

- [ ] **Step 4: Render the pills** in `ItineraryStopCard.tsx`. Import the icons, add two props, pass them to `buildStopSummary`, and render the alerts first in `StopSummaryLine`:

```tsx
import {SunIcon, ThermoIcon} from './WeatherIcons'
```

```tsx
function StopSummaryLine({summary}: {summary: StopSummary}) {
  return (
    <div className="stop-summary">
      {summary.alerts.uv != null && (
        <span className="sum-alert uv"><SunIcon /> UV {summary.alerts.uv}</span>
      )}
      {summary.alerts.feels != null && (
        <span className="sum-alert hot"><ThermoIcon /> รู้สึก {summary.alerts.feels}°</span>
      )}
      {summary.weather && (/* …existing weather span… */)}
      {/* …existing dwell + flag… */}
    </div>
  )
}
```

Add the two props to the component and thread them into `buildStopSummary`:

```tsx
export function ItineraryStopCard({ id, place, arrival, dwell, flag, arrivalReading, tripMonth,
  reorderMode = false, onOpenDetail, uvWarn, feelsWarn,
}: {
  /* …existing… */
  uvWarn?: number | null
  feelsWarn?: number | null
}) {
  const summary = buildStopSummary({arrivalReading, dwellMinutes: dwell, flag, uvWarn, feelsWarn})
  // …rest unchanged…
```

- [ ] **Step 5: Thread thresholds from `GET /api/me`** in `ItineraryTab.tsx` — read `me` and pass to every `<ItineraryStopCard>`:

```tsx
import {useGetMeQuery} from '../../../shared/api/api'
// inside the component:
const {data: me} = useGetMeQuery()
// at each <ItineraryStopCard …/> render site, add:
//   uvWarn={me?.uvWarnThreshold ?? null}
//   feelsWarn={me?.feelsLikeWarnThreshold ?? null}
```

- [ ] **Step 6: Add the pill styles** to `trips-tokens.css`:

```css
.stop-summary .sum-alert { display:inline-flex; align-items:center; gap:3px; border-radius:999px;
  font-size:10.5px; font-weight:800; padding:2px 8px; }
.stop-summary .sum-alert.uv  { background:var(--uv-vhigh-bg); color:var(--uv-vhigh); }
.stop-summary .sum-alert.hot { background:#fdeceb; color:#c5321f; }
```

- [ ] **Step 7: Run vitest + tsc + build**

Run: `cd frontend && npx vitest run src/pages/trips/lib/stopSummary.test.ts && npx tsc -b && npm run build`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/pages/trips/lib/stopSummary.ts frontend/src/pages/trips/lib/stopSummary.test.ts \
  frontend/src/pages/trips/components/ItineraryStopCard.tsx \
  frontend/src/pages/trips/components/ItineraryTab.tsx \
  frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): warn on the itinerary card when arrival UV/heat is high (#40)"
```

---

## Task 6: Frontend — /settings page + "เตือนอากาศ" section + route + NavBar entry

**Files:**
- Create: `frontend/src/pages/settings/weatherAlertOptions.ts`
- Test: `frontend/src/pages/settings/weatherAlertOptions.test.ts`
- Create: `frontend/src/pages/settings/SettingsPage.tsx`, `SettingsPage.css`, `index.ts`
- Modify: `frontend/src/router.tsx`
- Modify: `frontend/src/shared/components/NavBar.tsx`

**Interfaces:**
- Consumes: `useGetMeQuery`/`useUpdateUserSettingsMutation` (Task 3), `UV_WARN_DEFAULT`/`FEELS_WARN_DEFAULT` (Task 3).
- Produces: `UV_ALERT_OPTIONS`, `FEELS_ALERT_OPTIONS`, `selectedAlertValue(stored, dflt)`; `SettingsPage`.

> **Implementation note:** the two dropdowns are **native styled `<select>`s** (as in the mock), not Syncfusion `DropDownList`. Rationale: avoids the known Syncfusion version-skew "trial" banner risk for a simple 4-option control, and matches the confirmed mock. (ADR-085's Syncfusion choice was for #39's Home dropdown, not this section.)

- [ ] **Step 1: Write the failing test** — `weatherAlertOptions.test.ts`:

```ts
import {describe, it, expect} from 'vitest'
import {UV_ALERT_OPTIONS, FEELS_ALERT_OPTIONS, selectedAlertValue} from './weatherAlertOptions'

describe('weatherAlertOptions', () => {
  it('offers the UV presets incl. off', () => expect(UV_ALERT_OPTIONS.map((o) => o.value)).toEqual([3, 6, 8, 0]))
  it('offers the feels presets incl. off', () => expect(FEELS_ALERT_OPTIONS.map((o) => o.value)).toEqual([38, 40, 42, 0]))
  it('preselects the default when stored is null', () => expect(selectedAlertValue(null, 6)).toBe(6))
  it('preselects off (0) verbatim', () => expect(selectedAlertValue(0, 6)).toBe(0))
  it('preselects a stored value', () => expect(selectedAlertValue(8, 6)).toBe(8))
})
```

- [ ] **Step 2: Run to verify fail**

Run: `cd frontend && npx vitest run src/pages/settings/weatherAlertOptions.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement** `weatherAlertOptions.ts`:

```ts
export interface AlertOption { label: string; value: number }

export const UV_ALERT_OPTIONS: AlertOption[] = [
  {label: 'ปานกลางขึ้นไป (≥ 3)', value: 3},
  {label: 'สูงขึ้นไป (≥ 6)', value: 6},
  {label: 'สูงมากขึ้นไป (≥ 8)', value: 8},
  {label: 'ปิดการเตือน', value: 0},
]
export const FEELS_ALERT_OPTIONS: AlertOption[] = [
  {label: '≥ 38°', value: 38},
  {label: '≥ 40°', value: 40},
  {label: '≥ 42°', value: 42},
  {label: 'ปิดการเตือน', value: 0},
]
/** The dropdown value to preselect for a stored setting: null → the built-in default. */
export function selectedAlertValue(stored: number | null | undefined, dflt: number): number {
  return stored == null ? dflt : stored
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd frontend && npx vitest run src/pages/settings/weatherAlertOptions.test.ts`
Expected: PASS.

- [ ] **Step 5: Create the page** `SettingsPage.tsx`:

```tsx
import {useGetMeQuery, useUpdateUserSettingsMutation} from '../../shared/api/api'
import {UV_WARN_DEFAULT, FEELS_WARN_DEFAULT} from '../trips/lib/weather'
import {UV_ALERT_OPTIONS, FEELS_ALERT_OPTIONS, selectedAlertValue} from './weatherAlertOptions'
import './SettingsPage.css'

export function SettingsPage() {
  const {data: me} = useGetMeQuery()
  const [save, {isSuccess, isLoading}] = useUpdateUserSettingsMutation()

  const uvVal = selectedAlertValue(me?.uvWarnThreshold, UV_WARN_DEFAULT)
  const feelsVal = selectedAlertValue(me?.feelsLikeWarnThreshold, FEELS_WARN_DEFAULT)

  const persist = (next: {uv: number; feels: number}) => {
    if (!me) return
    save({homePath: me.homePath, uvWarnThreshold: next.uv, feelsLikeWarnThreshold: next.feels})
  }

  return (
    <div className="settings-page">
      <h1 className="settings-title">การตั้งค่า</h1>
      <section className="settings-section">
        <div className="settings-sec-title">เตือนอากาศ</div>
        <p className="settings-sec-desc">เตือนบนการ์ดเมื่อจุดหมายแดด/ร้อนเกินที่ตั้งไว้</p>

        <label className="settings-row">
          <span>เตือนเมื่อ UV</span>
          <select value={uvVal} disabled={!me || isLoading}
            onChange={(e) => persist({uv: Number(e.target.value), feels: feelsVal})}>
            {UV_ALERT_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        </label>

        <label className="settings-row">
          <span>เตือนเมื่อรู้สึกร้อน</span>
          <select value={feelsVal} disabled={!me || isLoading}
            onChange={(e) => persist({uv: uvVal, feels: Number(e.target.value)})}>
            {FEELS_ALERT_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        </label>

        {isSuccess && <span className="settings-saved">บันทึกแล้ว</span>}
      </section>
    </div>
  )
}
```

`index.ts`:
```ts
export {SettingsPage} from './SettingsPage'
```

`SettingsPage.css` (minimal; mirror the mock spacing):
```css
.settings-page { max-width:560px; margin:0 auto; padding:20px 16px; }
.settings-title { font-size:18px; font-weight:800; margin:0 0 16px; }
.settings-section { background:#fff; border:1px solid #eef2f6; border-radius:16px; padding:16px; }
.settings-sec-title { font-size:14px; font-weight:800; }
.settings-sec-desc { font-size:11.5px; color:#94a3b8; margin:2px 0 12px; }
.settings-row { display:flex; align-items:center; justify-content:space-between; gap:12px; margin-top:10px; }
.settings-row > span { font-size:12.5px; font-weight:600; color:#334155; }
.settings-row select { border:1px solid #d7dee6; border-radius:9px; padding:7px 12px;
  font:inherit; font-size:12.5px; font-weight:700; color:#0f172a; min-width:180px; }
.settings-saved { display:inline-block; margin-top:10px; font-size:11px; font-weight:700; color:#2f8f3e; }
```

- [ ] **Step 6: Wire the route** in `router.tsx` — add the import and the route inside the personal-scope `AppLayout` children (the block that holds `/pomodoro`, `/trips` — NOT under `FamilyRequiredRoute`; do NOT touch `/`):

```tsx
import {SettingsPage} from './pages/settings'
// …
          { path: '/settings', element: <SettingsPage /> },
```

- [ ] **Step 7: Add the NavBar entry** in `NavBar.tsx` — a text link (no emoji, matching the sibling items) in the account menu `<ul className="app-navbar__account-menu">` (before Sign out) and in the drawer `<ul className="app-drawer__links">`:

```tsx
<li role="none"><NavLink to="/settings" role="menuitem">Settings</NavLink></li>
```
```tsx
<NavLink to="/settings" className="app-drawer__link">Settings</NavLink>
```

- [ ] **Step 8: Typecheck + build + full unit suite**

Run: `cd frontend && npx vitest run && npx tsc -b && npm run build`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add frontend/src/pages/settings/ frontend/src/router.tsx frontend/src/shared/components/NavBar.tsx
git commit -m "feat(settings): add /settings page with weather-alert thresholds (closes #40)"
```

---

## Task 7: Interactive verification + prod migration + push (ops — no new commit)

The SPA has no visual test harness and prod deploys on push, so verify interactively and get the DB schema in place BEFORE pushing (CLAUDE.md).

- [ ] **Step 1: Interactive smoke** (run the app locally / Chrome DevTools):
  - Open a trip itinerary → tap a stop → the detail sheet shows, on **both** ตอนนี้ and ไปถึง, "รู้สึก N°" and a coloured UV badge (number + Thai word).
  - A stop arriving in hot midday shows the card badge(s); an evening-arrival stop with low UV shows a clean card (the mock's UV 9 → UV 2 case).
  - `/settings` (reachable from the account menu + drawer) shows the "เตือนอากาศ" section; changing either dropdown shows "บันทึกแล้ว", persists across reload, and "ปิดการเตือน" removes that badge from the cards.
  - A family-less account can open `/settings`.

- [ ] **Step 2: Apply the migration to prod** (additive nullable columns — backward-compatible with the currently-deployed code, so safe to apply before the push). Add a temp SQL firewall rule for your IP, apply, remove:

```bash
# terminal az session must be thodsaphonSP@hotmail.co.th (SQL Entra admin)
az account show   # expect Pay-As-You-Go / thodsaphonSP@hotmail.co.th
IP=<your public IP>
az sql server firewall-rule create --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply --start-ip-address $IP --end-ip-address $IP
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
az sql server firewall-rule delete --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply
```
(Preview first with `dotnet ef migrations script --idempotent` if desired.)

- [ ] **Step 3: Push** (remote is `main`, not `origin`):

```bash
git push main HEAD:main
```
Then confirm the prod deploy succeeded and `GET /api/me` returns the two new fields (no 500).

---

## Self-Review

**Spec coverage:**
- UV + feels-like on the reading (ADR-086, 093) → Task 1.
- Per-User thresholds storage/read/write + migration (ADR-089, 091) → Task 2.
- DTO/type mirror + pure band/threshold/alert logic (ADR-088, 092) → Task 3.
- Full values in the detail sheet (ADR-087, 088) → Task 4.
- Card alert on On-arrival only (ADR-087, 092) → Task 5.
- `/settings` shell + "เตือนอากาศ" section + NavBar entry + route (ADR-089, 090) → Task 6; Home-page section intentionally NOT built (deferred to #39).
- Manual prod migration + interactive smoke (CLAUDE.md) → Task 7.

**Placeholder scan:** none — every code step carries full code; every run step has an expected result.

**Type consistency:** `WeatherReading`/`WeatherReadingDto` gain `UvIndex/uvIndex` + `FeelsLikeC/feelsLikeC` consistently (Tasks 1, 3). `uvBand`, `effectiveThreshold`, `weatherAlertBadges`, `UV_WARN_DEFAULT`, `FEELS_WARN_DEFAULT` defined in Task 3 and consumed unchanged in Tasks 4–6. `buildStopSummary` gains `uvWarn/feelsWarn` and `alerts` (Task 5), consumed by `ItineraryStopCard`. `UpdateUserSettingsCommand`/`UserSettingsDto`/`MeDto` gain `UvWarnThreshold`/`FeelsLikeWarnThreshold` consistently (Task 2), mirrored on the client (Task 3).

**Two implementation choices flagged for the reviewer** (both from the spec's self-review): (1) native `<select>` for the settings dropdowns instead of Syncfusion `DropDownList` (avoids the version-skew banner risk; matches the mock); (2) `UpdateUserSettingsCommand` extended with **defaulted** params + the `0=off / null=default` encoding (full-replace snapshot, ADR-091).