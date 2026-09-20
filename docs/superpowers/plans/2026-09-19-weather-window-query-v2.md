# Weather Window Query Implementation Plan (v2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use sp-subagent-driven-development (recommended) or sp-executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the assistant answer "at this location, which days or hours are not rainy / not too hot / not too sunny?" over MCP and HTTP, so a **User** can choose a date before a **Trip** exists (issue #153).

**Architecture:** A read-only CQRS query in the Trips module. It calls the existing `IWeatherService.GetHourlyAsync` (the same `forecast/hours:lookup` walk the **Hourly forecast** already uses — no new provider, no new billing SKU), reads the **User**'s stored **Weather-alert threshold** from `UserSettings`, and cuts the hourly series into **Weather window**s with three pure, fully unit-tested classes. It is exposed as the MCP tool `find_weather_windows` and as `POST api/trips/weather/windows`. Nothing is persisted.

**Tech Stack:** .NET / C# · Mediator (source-generated `IQuery`/`IQueryHandler`) · FluentValidation · EF Core (read-only, existing `DbSet<UserSettings>`) · xUnit + Moq + FluentAssertions · ModelContextProtocol 1.0.0 (`[McpServerTool]`)

**Spec:** `docs/superpowers/specs/2026-09-19-weather-window-query-design.md`

**Supersedes:** `docs/superpowers/plans/2026-09-19-weather-window-query.md`. Execute **this** file only. What changed and why is listed in the last section.

## Global Constraints

- **Never persist weather.** No entity, no `DbSet`, no migration, no change to any of the four `IApplicationDbContext` implementers (ADR-033). `UserSettings` is **read** through the existing `IApplicationDbContext.UserSettings`.
- **No new Google call shape.** Only `IWeatherService.GetHourlyAsync`. No new provider, no new billing SKU (ADR-093, ADR-119).
- **Degrade, never throw, on provider failure.** `GetHourlyAsync` already returns an empty list on failure (ADR-030). An empty list means `WeatherWindowMissReason.NoWeatherData`, never `AllHoursBlocked` (ADR-031).
- **Built-in defaults, exact values:** rain `60` % (`RAIN_TINT_THRESHOLD`), **Feels-like** `40` °C (`FEELS_WARN_DEFAULT`), **UV index** `6` (`UV_WARN_DEFAULT`) — all three from `frontend/src/pages/trips/lib/weather.ts`.
- **Stored threshold encoding** (`UserSettings.UvWarnThreshold` / `FeelsLikeWarnThreshold`, `int?`): `null` = built-in default · `0` = **off** (the signal never blocks) · `N` = that value. **A missing `UserSettings` row behaves exactly like `null`** — the row is created lazily on first write, so most **User**s have none.
- **The blocking rule is `>=`.** An hour is **blocked** by a signal when its value is **at or above** that signal's threshold. This is the web app's rule (`isRainy`: `rainPct >= 60`; the entity doc: "warn at UV >= N"). An hour the app would badge must never be inside a window the assistant calls good.
- **Horizon:** 240 hours / 10 days (ADR-031). `GetHourlyAsync` clamps `hours` to `[1, 240]` itself.
- **Time comes from `IClock`**, never `DateTime.UtcNow` in the handler. Tests use `Support/FixedClock`.
- **Mocking is Moq, never NSubstitute.** `Substitute.For<>` does not compile in this repo.
- **Every commit leaves the WHOLE suite green.** `frontend/.husky/pre-commit` runs backend `dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` + `npm run build`. Never `--no-verify`. **In some checkouts the hook is not wired** (`core.hooksPath` unset, no `.git/hooks/pre-commit`) — so every task below runs the full backend suite itself before committing. The rule is the green suite, not the hook.
- **Stage narrowly.** Always `git add <explicit paths>`. Never `git add -A` / `git add .` — `daily-state.md` and `AGENTS.md` must never enter a feature commit.
- **Every commit references the ticket.** Subject ends `(#153)`; the last commit of the feature uses `(closes #153)`.
- **No frontend work in this plan.**
- **Run every command from the repository root.**

---

## File Structure

| File | Responsibility |
|---|---|
| `backend/src/MenuNest.Domain/Enums/WeatherSignal.cs` (new) | the three **selected signal**s `Rain`, `Heat`, `Sun`. Declared order is the tie-break order |
| `backend/src/MenuNest.Domain/Enums/WeatherWindowMissReason.cs` (new) | why zero windows came back |
| `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowThresholds.cs` (new) | **pure.** `WeatherThresholds` record + `Resolve`: explicit > stored > default, `0` = off. Server twin of `effectiveThreshold` in `weather.ts` |
| `.../FindWeatherWindows/WeatherHourJudge.cs` (new) | **pure.** Hours-of-day band membership (incl. midnight wrap), band-date attribution, per-hour verdict |
| `.../FindWeatherWindows/WeatherWindowSelection.cs` (new) | **pure.** Filter to band + date range, group good hours into runs, apply `MinWindowHours`, build windows or the miss record |
| `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (modify, append) | `WeatherWindowDto`, `WeatherWindowMissDto`, `WeatherWindowResultDto` |
| `.../FindWeatherWindows/FindWeatherWindowsQuery.cs` (new) | the query record |
| `.../FindWeatherWindows/FindWeatherWindowsValidator.cs` (new) | FluentValidation rules |
| `.../FindWeatherWindows/FindWeatherWindowsHandler.cs` (new) | fetch sizing, `UserSettings` read, orchestration, horizon truncation |
| `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` (modify) | `POST api/trips/weather/windows` |
| `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (modify) | `find_weather_windows` |

Tests: one file per pure class, plus validator, handler, controller and tool tests.

---

### Task 1: Signals and threshold resolution (pure)

This is the **first** server-side reader of the **Weather-alert threshold**. Until now only the web app decoded it.

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/WeatherSignal.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowThresholds.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowThresholdsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum WeatherSignal { Rain, Heat, Sun }` in `MenuNest.Domain.Enums`
  - `sealed record WeatherThresholds(IReadOnlyCollection<WeatherSignal> Selected, int? RainPct, double? FeelsLikeC, int? UvIndex)` with method `double? Of(WeatherSignal signal)`. **A `null` threshold means that signal never blocks an hour** (unselected, or selected but switched off).
  - `static class WeatherWindowThresholds` with `const int DefaultRainPct = 60`, `const int DefaultFeelsLikeC = 40`, `const int DefaultUvIndex = 6`, and `static WeatherThresholds Resolve(IReadOnlyCollection<WeatherSignal> signals, int? explicitRainPct, double? explicitFeelsLikeC, int? explicitUvIndex, int? storedUvWarn, int? storedFeelsWarn)`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowThresholdsTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class WeatherWindowThresholdsTests
{
    private static readonly WeatherSignal[] All = { WeatherSignal.Rain, WeatherSignal.Heat, WeatherSignal.Sun };

    [Fact]
    public void Unselected_signals_get_no_threshold_even_when_one_is_stored()
    {
        var t = WeatherWindowThresholds.Resolve(
            new[] { WeatherSignal.Rain }, null, null, null, storedUvWarn: 8, storedFeelsWarn: 38);

        t.RainPct.Should().Be(60);
        t.FeelsLikeC.Should().BeNull();
        t.UvIndex.Should().BeNull();
        t.Selected.Should().BeEquivalentTo(new[] { WeatherSignal.Rain });
    }

    [Fact]
    public void A_missing_UserSettings_row_resolves_to_the_built_in_defaults()
    {
        // The handler passes null/null when the User has no UserSettings row at all.
        var t = WeatherWindowThresholds.Resolve(All, null, null, null, storedUvWarn: null, storedFeelsWarn: null);

        t.RainPct.Should().Be(60);
        t.FeelsLikeC.Should().Be(40);
        t.UvIndex.Should().Be(6);
    }

    [Fact]
    public void A_stored_value_beats_the_built_in_default()
    {
        var t = WeatherWindowThresholds.Resolve(All, null, null, null, storedUvWarn: 8, storedFeelsWarn: 38);

        t.FeelsLikeC.Should().Be(38);
        t.UvIndex.Should().Be(8);
    }

    [Fact]
    public void A_stored_zero_means_off_so_the_selected_signal_never_blocks()
    {
        var t = WeatherWindowThresholds.Resolve(All, null, null, null, storedUvWarn: 0, storedFeelsWarn: 0);

        t.FeelsLikeC.Should().BeNull();
        t.UvIndex.Should().BeNull();
        t.RainPct.Should().Be(60, "rain has no stored threshold, so 'off' cannot apply to it");
        t.Selected.Should().BeEquivalentTo(All, "an off signal is still selected — its Worst* value is still reported");
    }

    [Fact]
    public void An_explicit_value_beats_the_stored_value_and_the_default()
    {
        var t = WeatherWindowThresholds.Resolve(All, 70, 42.5, 9, storedUvWarn: 0, storedFeelsWarn: 38);

        t.RainPct.Should().Be(70);
        t.FeelsLikeC.Should().Be(42.5);
        t.UvIndex.Should().Be(9, "an explicit value is honoured even when the stored threshold is off");
    }

    [Fact]
    public void Of_reads_the_threshold_for_one_signal()
    {
        var t = new WeatherThresholds(All, RainPct: 60, FeelsLikeC: 40, UvIndex: null);

        t.Of(WeatherSignal.Rain).Should().Be(60);
        t.Of(WeatherSignal.Heat).Should().Be(40);
        t.Of(WeatherSignal.Sun).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherWindowThresholdsTests"`
Expected: FAIL — build error, `WeatherSignal` / `WeatherWindowThresholds` do not exist.

- [ ] **Step 3: Write the enum**

Create `backend/src/MenuNest.Domain/Enums/WeatherSignal.cs`:

```csharp
namespace MenuNest.Domain.Enums;

/// <summary>A per-hour cue a Weather window is judged on, chosen per call from the User's question
/// (menunest-220). Heat (Feels-like) and Sun (UV index) stay separate (menunest-221). The declared
/// order is the tie-break order when naming the blocking signal of an empty result (menunest-225).</summary>
public enum WeatherSignal
{
    Rain,
    Heat,
    Sun,
}
```

- [ ] **Step 4: Write the resolver**

Create `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowThresholds.cs`:

```csharp
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>The effective threshold for each signal. A null threshold means that signal never blocks
/// an hour — it is unselected, or selected with its stored Weather-alert threshold switched off.
/// An hour is BLOCKED when its value is AT OR ABOVE the threshold: the web app's own rule
/// (isRainy: rainPct &gt;= 60; UserSettings: "warn at UV &gt;= N").</summary>
public sealed record WeatherThresholds(
    IReadOnlyCollection<WeatherSignal> Selected, int? RainPct, double? FeelsLikeC, int? UvIndex)
{
    public double? Of(WeatherSignal signal) => signal switch
    {
        WeatherSignal.Rain => RainPct,
        WeatherSignal.Heat => FeelsLikeC,
        WeatherSignal.Sun => UvIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };
}

/// <summary>Server twin of <c>effectiveThreshold</c> in frontend/src/pages/trips/lib/weather.ts and of
/// frontend/src/pages/settings/weatherAlertControl.ts. Change the rule in all three places or none.</summary>
public static class WeatherWindowThresholds
{
    public const int DefaultRainPct = 60;    // RAIN_TINT_THRESHOLD
    public const int DefaultFeelsLikeC = 40; // FEELS_WARN_DEFAULT
    public const int DefaultUvIndex = 6;     // UV_WARN_DEFAULT

    /// <summary>Explicit per-call value &gt; stored Weather-alert threshold &gt; built-in default
    /// (menunest-219). Only selected signals get a threshold. Rain has no stored threshold.</summary>
    public static WeatherThresholds Resolve(
        IReadOnlyCollection<WeatherSignal> signals,
        int? explicitRainPct, double? explicitFeelsLikeC, int? explicitUvIndex,
        int? storedUvWarn, int? storedFeelsWarn)
    {
        var selected = signals.Distinct().ToList();
        return new WeatherThresholds(
            selected,
            RainPct: selected.Contains(WeatherSignal.Rain)
                ? explicitRainPct ?? DefaultRainPct
                : null,
            FeelsLikeC: selected.Contains(WeatherSignal.Heat)
                ? explicitFeelsLikeC ?? (double?)Stored(storedFeelsWarn, DefaultFeelsLikeC)
                : null,
            UvIndex: selected.Contains(WeatherSignal.Sun)
                ? explicitUvIndex ?? Stored(storedUvWarn, DefaultUvIndex)
                : null);
    }

    /// <summary>null → the default · 0 → off (null: never blocks) · N → N.</summary>
    private static int? Stored(int? stored, int builtInDefault) => stored switch
    {
        null => builtInDefault,
        0 => null,
        var n => n,
    };
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherWindowThresholdsTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Run the whole backend suite**

Run: `dotnet test backend --configuration Release`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums/WeatherSignal.cs \
        backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowThresholds.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowThresholdsTests.cs
git commit -m "feat(weather): resolve Weather window thresholds from the User's settings (#153)"
```

---

### Task 2: Band membership and the per-hour verdict (pure)

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherHourJudge.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/WeatherHourJudgeTests.cs`

**Interfaces:**
- Consumes: `WeatherSignal`, `WeatherThresholds` (Task 1); `HourlyReading(DateTime DisplayLocal, bool IsDaytime, double? TempC, double? FeelsLikeC, string? ConditionType, string? IconBaseUri, int? RainPct, int? UvIndex)` from `MenuNest.Application.Abstractions`.
- Produces:
  - `sealed record HourVerdict(bool IsGood, IReadOnlyList<WeatherSignal> Failed)` with computed `bool IsBlocked => Failed.Count > 0`. An hour that is neither good nor blocked is **unjudgeable**.
  - `static class WeatherHourJudge` with
    - `static bool InBand(int hour, int fromHour, int toHour)`
    - `static DateOnly BandDate(DateTime displayLocal, int fromHour, int toHour)`
    - `static double? ValueOf(HourlyReading hour, WeatherSignal signal)`
    - `static HourVerdict Judge(HourlyReading hour, WeatherThresholds thresholds)`

**The three verdicts, exactly:**

| verdict | rule |
|---|---|
| **blocked** | at least one signal with a non-null threshold has a value `>=` that threshold. `Failed` lists every such signal |
| **unjudgeable** | nothing failed, but a signal with a non-null threshold has a `null` value. Not good; not counted as blocked, because no signal rejected it |
| **good** | nothing failed and nothing needed was missing |

A signal whose threshold is `null` (unselected or off) is never consulted, so a missing value for it does **not** make the hour unjudgeable. A rejection beats a missing value: an hour where rain fails and feels-like is `null` is **blocked** by rain.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/WeatherHourJudgeTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class WeatherHourJudgeTests
{
    private static readonly WeatherSignal[] All = { WeatherSignal.Rain, WeatherSignal.Heat, WeatherSignal.Sun };

    private static HourlyReading Hour(int? rain = 10, double? feels = 30, int? uv = 2, int hour = 10, int day = 20)
        => new(new DateTime(2026, 9, day, hour, 0, 0), true, 28, feels, "CLEAR", null, rain, uv);

    [Theory]
    [InlineData(8, true)]
    [InlineData(15, true)]
    [InlineData(16, false)] // toHour is exclusive
    [InlineData(7, false)]
    public void InBand_without_wrap_is_fromHour_inclusive_to_toHour_exclusive(int hour, bool expected)
        => WeatherHourJudge.InBand(hour, 8, 16).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(23)]
    public void The_default_band_0_to_24_holds_every_hour(int hour)
        => WeatherHourJudge.InBand(hour, 0, 24).Should().BeTrue();

    [Theory]
    [InlineData(18, true)]
    [InlineData(23, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(17, false)]
    public void InBand_wraps_past_midnight_when_toHour_is_not_after_fromHour(int hour, bool expected)
        => WeatherHourJudge.InBand(hour, 18, 2).Should().Be(expected);

    [Fact]
    public void The_wrapped_tail_belongs_to_the_band_opened_on_the_earlier_date()
    {
        WeatherHourJudge.BandDate(new DateTime(2026, 9, 21, 1, 0, 0), 18, 2)
            .Should().Be(new DateOnly(2026, 9, 20));
        WeatherHourJudge.BandDate(new DateTime(2026, 9, 20, 19, 0, 0), 18, 2)
            .Should().Be(new DateOnly(2026, 9, 20));
    }

    [Fact]
    public void Without_wrap_the_band_date_is_the_hours_own_date()
        => WeatherHourJudge.BandDate(new DateTime(2026, 9, 21, 1, 0, 0), 0, 24)
            .Should().Be(new DateOnly(2026, 9, 21));

    [Fact]
    public void An_hour_below_every_threshold_is_good()
    {
        var v = WeatherHourJudge.Judge(Hour(rain: 59, feels: 39.9, uv: 5), new WeatherThresholds(All, 60, 40, 6));

        v.IsGood.Should().BeTrue();
        v.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void A_value_exactly_at_the_threshold_blocks_matching_the_web_apps_badge_rule()
    {
        var v = WeatherHourJudge.Judge(Hour(rain: 60, feels: 40, uv: 6), new WeatherThresholds(All, 60, 40, 6));

        v.IsGood.Should().BeFalse();
        v.Failed.Should().Equal(WeatherSignal.Rain, WeatherSignal.Heat, WeatherSignal.Sun);
    }

    [Fact]
    public void A_missing_value_for_a_gating_signal_makes_the_hour_unjudgeable_not_blocked()
    {
        var v = WeatherHourJudge.Judge(Hour(rain: null), new WeatherThresholds(new[] { WeatherSignal.Rain }, 60, null, null));

        v.IsGood.Should().BeFalse();
        v.IsBlocked.Should().BeFalse();
        v.Failed.Should().BeEmpty();
    }

    [Fact]
    public void A_signal_with_no_threshold_is_never_consulted_even_when_its_value_is_missing()
    {
        // Heat is selected but switched off (threshold null); its missing value must not matter.
        var v = WeatherHourJudge.Judge(Hour(feels: null), new WeatherThresholds(new[] { WeatherSignal.Heat }, null, null, null));

        v.IsGood.Should().BeTrue();
    }

    [Fact]
    public void A_rejection_beats_a_missing_value()
    {
        var v = WeatherHourJudge.Judge(Hour(rain: 80, feels: null), new WeatherThresholds(All, 60, 40, 6));

        v.IsBlocked.Should().BeTrue();
        v.Failed.Should().Equal(WeatherSignal.Rain);
    }

    [Fact]
    public void ValueOf_reads_the_matching_field()
    {
        var h = Hour(rain: 55, feels: 33.5, uv: 7);

        WeatherHourJudge.ValueOf(h, WeatherSignal.Rain).Should().Be(55);
        WeatherHourJudge.ValueOf(h, WeatherSignal.Heat).Should().Be(33.5);
        WeatherHourJudge.ValueOf(h, WeatherSignal.Sun).Should().Be(7);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherHourJudgeTests"`
Expected: FAIL — build error, `WeatherHourJudge` does not exist.

- [ ] **Step 3: Write the judge**

Create `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherHourJudge.cs`:

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>The verdict on one forecast hour. Neither good nor blocked = unjudgeable: a signal that
/// gates the hour had no value, and no signal rejected it.</summary>
public sealed record HourVerdict(bool IsGood, IReadOnlyList<WeatherSignal> Failed)
{
    public bool IsBlocked => Failed.Count > 0;
}

public static class WeatherHourJudge
{
    /// <summary>[fromHour, toHour). When toHour &lt;= fromHour the band wraps past midnight
    /// (e.g. 18→2 for a night market).</summary>
    public static bool InBand(int hour, int fromHour, int toHour)
        => toHour > fromHour
            ? hour >= fromHour && hour < toHour
            : hour >= fromHour || hour < toHour;

    /// <summary>The date whose band this hour belongs to. On a wrapping band the after-midnight tail
    /// belongs to the band opened the evening before (spec §5; ADR-118 crosses midnight the same way).</summary>
    public static DateOnly BandDate(DateTime displayLocal, int fromHour, int toHour)
    {
        var date = DateOnly.FromDateTime(displayLocal);
        return toHour <= fromHour && displayLocal.Hour < toHour ? date.AddDays(-1) : date;
    }

    public static double? ValueOf(HourlyReading hour, WeatherSignal signal) => signal switch
    {
        WeatherSignal.Rain => hour.RainPct,
        WeatherSignal.Heat => hour.FeelsLikeC,
        WeatherSignal.Sun => hour.UvIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };

    /// <summary>Blocked when any gating signal is AT OR ABOVE its threshold. Only signals with a
    /// non-null threshold gate the hour.</summary>
    public static HourVerdict Judge(HourlyReading hour, WeatherThresholds thresholds)
    {
        var failed = new List<WeatherSignal>(3);
        var missing = false;
        foreach (var signal in Enum.GetValues<WeatherSignal>())
        {
            if (thresholds.Of(signal) is not { } limit) continue;
            if (ValueOf(hour, signal) is not { } value) { missing = true; continue; }
            if (value >= limit) failed.Add(signal);
        }
        return new HourVerdict(IsGood: failed.Count == 0 && !missing, Failed: failed);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherHourJudgeTests"`
Expected: PASS, 21 test cases (13 theory rows + 8 facts).

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test backend --configuration Release`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherHourJudge.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/WeatherHourJudgeTests.cs
git commit -m "feat(weather): judge one forecast hour against the selected signals (#153)"
```

---

### Task 3: Cut the hourly series into Weather windows (pure)

The DTOs and the miss-reason enum land here, because this is the task that builds them.

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/WeatherWindowMissReason.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (append at the end of the file)
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowSelection.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowSelectionTests.cs`

**Interfaces:**
- Consumes: `WeatherSignal`, `WeatherThresholds` (Task 1); `WeatherHourJudge`, `HourVerdict` (Task 2); `HourlyReading`.
- Produces:
  - `enum WeatherWindowMissReason { NoWeatherData, AllHoursBlocked, NoWindowLongEnough }` in `MenuNest.Domain.Enums`
  - in `MenuNest.Application.UseCases.Trips` (TripDtos.cs):
    - `sealed record WeatherWindowDto(DateOnly Date, DateTime StartLocal, DateTime EndLocalExclusive, int Hours, int? WorstRainPct, double? WorstFeelsLikeC, int? WorstUvIndex)`
    - `sealed record WeatherWindowMissDto(WeatherWindowMissReason Reason, WeatherSignal? BlockingSignal, double? ClosestValue, double? Threshold, int HoursExamined, int HoursBlocked, int LongestRunHours)`
    - `sealed record WeatherWindowResultDto(IReadOnlyList<WeatherWindowDto> Windows, WeatherWindowMissDto? Miss, bool HorizonTruncated, DateOnly SearchedFrom, DateOnly SearchedThrough)`
  - `sealed record WeatherWindowCut(IReadOnlyList<WeatherWindowDto> Windows, WeatherWindowMissDto? Miss, DateOnly? FirstExaminedDate, DateOnly? LastExaminedDate)`
  - `static WeatherWindowCut WeatherWindowSelection.Cut(IReadOnlyList<HourlyReading> hours, WeatherThresholds thresholds, DateOnly? fromDate, DateOnly? toDate, int fromHour, int toHour, int minWindowHours)`

**Rules this task implements (spec §5, §7):**

1. **Examined hours** = hours inside the band whose **band date** is inside `[fromDate, toDate]` (either bound may be null = open).
2. **A run continues** only when the hour is good **and** it is exactly one hour after the previous hour **and** it has the same band date. So a gap in the buckets breaks a run, the band's edges break runs, and with the default 0–24 band every midnight breaks a run — one window per band-run, never one giant window.
3. Runs shorter than `minWindowHours` are dropped.
4. Windows are ordered by `StartLocal`. `Date` = band date of the first hour. `Worst*` = the maximum over the window, **only for selected signals**; an unselected signal's field is `null`.
5. With no window left, `Miss` is:
   - `NoWeatherData` when nothing was examined, **or** when no hour was good and none was blocked (every hour unjudgeable) — no cause may be invented.
   - `NoWindowLongEnough` when at least one hour was good.
   - `AllHoursBlocked` otherwise.
6. When any hour was blocked, the miss also names the **blocking signal** — the signal that failed the most hours, ties broken in declared order `Rain`, `Heat`, `Sun` — its **threshold**, and **`ClosestValue`**: the lowest value of that signal **among the hours that signal itself blocked**. Because blocking is `>=`, a threshold must be set **above** `ClosestValue` to unblock that hour; the tool description (Task 7) says so.
7. `Miss` is null exactly when `Windows` is non-empty.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowSelectionTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class WeatherWindowSelectionTests
{
    private static readonly DateOnly Day0 = new(2026, 9, 20);
    private static readonly WeatherThresholds RainOnly = new(new[] { WeatherSignal.Rain }, 60, null, null);
    private static readonly WeatherThresholds RainAndHeat =
        new(new[] { WeatherSignal.Rain, WeatherSignal.Heat }, 60, 40, null);

    private static HourlyReading H(int day, int hour, int? rain = 10, double? feels = 30, int? uv = 2)
        => new(Day0.AddDays(day).ToDateTime(new TimeOnly(hour, 0)), true, 28, feels, "CLEAR", null, rain, uv);

    private static IEnumerable<HourlyReading> Run(int day, int fromHour, int count)
        => Enumerable.Range(fromHour, count).Select(h => H(day, h));

    private static WeatherWindowCut Cut(
        IEnumerable<HourlyReading> hours, WeatherThresholds? t = null,
        DateOnly? from = null, DateOnly? to = null, int fromHour = 0, int toHour = 24, int min = 1)
        => WeatherWindowSelection.Cut(hours.ToList(), t ?? RainOnly, from, to, fromHour, toHour, min);

    // ── windows ───────────────────────────────────────────────────────────────

    [Fact]
    public void Consecutive_good_hours_form_one_window()
    {
        var cut = Cut(Run(0, 6, 3));

        cut.Miss.Should().BeNull();
        cut.Windows.Should().ContainSingle();
        var w = cut.Windows[0];
        w.Date.Should().Be(Day0);
        w.StartLocal.Should().Be(Day0.ToDateTime(new TimeOnly(6, 0)));
        w.EndLocalExclusive.Should().Be(Day0.ToDateTime(new TimeOnly(9, 0)));
        w.Hours.Should().Be(3);
    }

    [Fact]
    public void A_blocked_hour_splits_a_run()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7), H(0, 8, rain: 70), H(0, 9), H(0, 10) });

        cut.Windows.Select(w => w.Hours).Should().Equal(2, 2);
    }

    [Fact]
    public void A_missing_bucket_splits_a_run()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7), H(0, 9), H(0, 10) });

        cut.Windows.Select(w => w.StartLocal.Hour).Should().Equal(6, 9);
    }

    [Fact]
    public void An_unjudgeable_hour_splits_a_run()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7, rain: null), H(0, 8) });

        cut.Windows.Select(w => w.StartLocal.Hour).Should().Equal(6, 8);
    }

    [Fact]
    public void Runs_shorter_than_the_minimum_are_dropped()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7, rain: 90), H(0, 8), H(0, 9) }, min: 2);

        cut.Windows.Should().ContainSingle().Which.StartLocal.Hour.Should().Be(8);
    }

    [Fact]
    public void The_band_edges_split_windows_per_day()
    {
        var cut = Cut(Run(0, 0, 24).Concat(Run(1, 0, 24)), fromHour: 8, toHour: 16);

        cut.Windows.Should().HaveCount(2);
        cut.Windows.Should().OnlyContain(w => w.Hours == 8);
        cut.Windows.Select(w => w.Date).Should().Equal(Day0, Day0.AddDays(1));
    }

    [Fact]
    public void With_the_default_band_midnight_still_splits_windows()
    {
        var cut = Cut(new[] { H(0, 22), H(0, 23), H(1, 0), H(1, 1) });

        cut.Windows.Select(w => w.Hours).Should().Equal(2, 2);
    }

    [Fact]
    public void A_wrapping_band_joins_across_midnight_and_dates_the_window_by_its_first_hour()
    {
        var cut = Cut(Run(0, 18, 6).Concat(Run(1, 0, 2)), fromHour: 18, toHour: 2);

        var w = cut.Windows.Should().ContainSingle().Subject;
        w.Hours.Should().Be(8);
        w.Date.Should().Be(Day0);
        w.EndLocalExclusive.Should().Be(Day0.AddDays(1).ToDateTime(new TimeOnly(2, 0)));
    }

    [Fact]
    public void The_date_range_filters_by_band_date()
    {
        var cut = Cut(Run(0, 6, 3).Concat(Run(1, 6, 3)), from: Day0.AddDays(1), to: Day0.AddDays(1));

        cut.Windows.Should().ContainSingle().Which.Date.Should().Be(Day0.AddDays(1));
        cut.FirstExaminedDate.Should().Be(Day0.AddDays(1));
        cut.LastExaminedDate.Should().Be(Day0.AddDays(1));
    }

    [Fact]
    public void Worst_values_are_reported_only_for_selected_signals()
    {
        var cut = Cut(new[] { H(0, 6, rain: 10, feels: 35, uv: 9), H(0, 7, rain: 40, feels: 36, uv: 3) });

        var w = cut.Windows.Single();
        w.WorstRainPct.Should().Be(40);
        w.WorstFeelsLikeC.Should().BeNull("Heat was not selected — an unjudged value must not be reported");
        w.WorstUvIndex.Should().BeNull();
    }

    // ── the miss ──────────────────────────────────────────────────────────────

    [Fact]
    public void No_hours_at_all_is_NoWeatherData()
    {
        var cut = Cut(Array.Empty<HourlyReading>());

        cut.Windows.Should().BeEmpty();
        cut.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
        cut.Miss.HoursExamined.Should().Be(0);
        cut.FirstExaminedDate.Should().BeNull();
    }

    [Fact]
    public void Hours_that_all_fall_outside_the_band_are_NoWeatherData()
    {
        var cut = Cut(new[] { H(0, 20), H(0, 21) }, fromHour: 8, toHour: 10);

        cut.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
    }

    [Fact]
    public void Every_hour_blocked_names_the_signal_the_closest_value_and_the_threshold()
    {
        var cut = Cut(new[] { H(0, 6, rain: 70), H(0, 7, rain: 65), H(0, 8, rain: 80) });

        cut.Windows.Should().BeEmpty();
        var m = cut.Miss!;
        m.Reason.Should().Be(WeatherWindowMissReason.AllHoursBlocked);
        m.BlockingSignal.Should().Be(WeatherSignal.Rain);
        m.ClosestValue.Should().Be(65);
        m.Threshold.Should().Be(60);
        m.HoursExamined.Should().Be(3);
        m.HoursBlocked.Should().Be(3);
        m.LongestRunHours.Should().Be(0);
    }

    [Fact]
    public void The_blocking_signal_is_the_one_that_failed_the_most_hours()
    {
        var cut = Cut(new[] { H(0, 6, feels: 45), H(0, 7, feels: 44), H(0, 8, rain: 70) }, RainAndHeat);

        cut.Miss!.BlockingSignal.Should().Be(WeatherSignal.Heat);
        cut.Miss.ClosestValue.Should().Be(44);
        cut.Miss.Threshold.Should().Be(40);
    }

    [Fact]
    public void A_tie_is_broken_in_declared_order_and_closest_ignores_hours_this_signal_passed()
    {
        // Hour 6 is rejected by heat only; its rain of 10 says nothing about how far to relax rain.
        var cut = Cut(new[] { H(0, 6, rain: 10, feels: 45), H(0, 7, rain: 70, feels: 30) }, RainAndHeat);

        cut.Miss!.BlockingSignal.Should().Be(WeatherSignal.Rain);
        cut.Miss.ClosestValue.Should().Be(70, "not 10 — quoting a passing hour's value would relax nothing");
    }

    [Fact]
    public void Some_good_hours_but_no_run_long_enough_is_NoWindowLongEnough()
    {
        var cut = Cut(new[] { H(0, 6), H(0, 7, rain: 70), H(0, 8) }, min: 2);

        var m = cut.Miss!;
        m.Reason.Should().Be(WeatherWindowMissReason.NoWindowLongEnough);
        m.LongestRunHours.Should().Be(1);
        m.BlockingSignal.Should().Be(WeatherSignal.Rain);
        m.HoursBlocked.Should().Be(1);
    }

    [Fact]
    public void Every_hour_unjudgeable_is_NoWeatherData_with_no_invented_cause()
    {
        var cut = Cut(new[] { H(0, 6, rain: null), H(0, 7, rain: null) });

        var m = cut.Miss!;
        m.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
        m.BlockingSignal.Should().BeNull();
        m.ClosestValue.Should().BeNull();
        m.HoursExamined.Should().Be(2);
        m.HoursBlocked.Should().Be(0);
    }

    [Fact]
    public void Miss_is_null_exactly_when_windows_exist()
    {
        Cut(Run(0, 6, 2)).Miss.Should().BeNull();
        Cut(new[] { H(0, 6, rain: 99) }).Miss.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherWindowSelectionTests"`
Expected: FAIL — build error, `WeatherWindowSelection` / `WeatherWindowMissReason` do not exist.

- [ ] **Step 3: Write the miss-reason enum**

Create `backend/src/MenuNest.Domain/Enums/WeatherWindowMissReason.cs`:

```csharp
namespace MenuNest.Domain.Enums;

/// <summary>Why a Weather window query returned no window (menunest-225). NoWeatherData is never
/// conflated with AllHoursBlocked: "no forecast" is not "bad weather" (ADR-030/031).</summary>
public enum WeatherWindowMissReason
{
    NoWeatherData,
    AllHoursBlocked,
    NoWindowLongEnough,
}
```

- [ ] **Step 4: Append the DTOs**

Append to the end of `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (the file already has `using MenuNest.Domain.Enums;`):

```csharp

/// <summary>One Weather window: a dated, contiguous run of forecast hours in which every selected
/// signal stayed below its threshold (menunest-218). Worst* are null for unselected signals.</summary>
public sealed record WeatherWindowDto(
    DateOnly Date, DateTime StartLocal, DateTime EndLocalExclusive, int Hours,
    int? WorstRainPct, double? WorstFeelsLikeC, int? WorstUvIndex);

/// <summary>Why no window came back (menunest-225). ClosestValue is the lowest value of the blocking
/// signal among the hours it blocked; blocking is &gt;=, so only a threshold ABOVE it unblocks one.</summary>
public sealed record WeatherWindowMissDto(
    WeatherWindowMissReason Reason, WeatherSignal? BlockingSignal,
    double? ClosestValue, double? Threshold,
    int HoursExamined, int HoursBlocked, int LongestRunHours);

/// <summary>Miss is null exactly when Windows is non-empty. SearchedFrom/SearchedThrough name the
/// band dates actually examined; HorizonTruncated says ToDate ran past the forecast.</summary>
public sealed record WeatherWindowResultDto(
    IReadOnlyList<WeatherWindowDto> Windows, WeatherWindowMissDto? Miss,
    bool HorizonTruncated, DateOnly SearchedFrom, DateOnly SearchedThrough);
```

- [ ] **Step 5: Write the selection**

Create `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowSelection.cs`:

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>The windows found, or why there are none, plus the band dates actually examined.</summary>
public sealed record WeatherWindowCut(
    IReadOnlyList<WeatherWindowDto> Windows, WeatherWindowMissDto? Miss,
    DateOnly? FirstExaminedDate, DateOnly? LastExaminedDate);

/// <summary>Cuts an hourly forecast into Weather windows. Pure — no I/O — in the same spirit as
/// WeatherHourSelection.CoolestHour, so the whole judging rule is unit-testable.</summary>
public static class WeatherWindowSelection
{
    private sealed record Examined(HourlyReading Hour, DateOnly BandDate, HourVerdict Verdict);

    public static WeatherWindowCut Cut(
        IReadOnlyList<HourlyReading> hours, WeatherThresholds thresholds,
        DateOnly? fromDate, DateOnly? toDate, int fromHour, int toHour, int minWindowHours)
    {
        var examined = hours
            .Where(h => WeatherHourJudge.InBand(h.DisplayLocal.Hour, fromHour, toHour))
            .Select(h => new Examined(h, WeatherHourJudge.BandDate(h.DisplayLocal, fromHour, toHour),
                                      WeatherHourJudge.Judge(h, thresholds)))
            .Where(x => (fromDate is null || x.BandDate >= fromDate) && (toDate is null || x.BandDate <= toDate))
            .OrderBy(x => x.Hour.DisplayLocal)
            .ToList();

        if (examined.Count == 0)
            return new WeatherWindowCut(
                Array.Empty<WeatherWindowDto>(),
                new WeatherWindowMissDto(WeatherWindowMissReason.NoWeatherData, null, null, null, 0, 0, 0),
                null, null);

        var firstDate = examined.Min(x => x.BandDate);
        var lastDate = examined.Max(x => x.BandDate);

        var runs = new List<List<Examined>>();
        List<Examined>? current = null;
        foreach (var x in examined)
        {
            if (!x.Verdict.IsGood) { current = null; continue; }
            var joins = current is not null
                && current[^1].BandDate == x.BandDate
                && current[^1].Hour.DisplayLocal.AddHours(1) == x.Hour.DisplayLocal;
            if (!joins)
            {
                current = new List<Examined>();
                runs.Add(current);
            }
            current!.Add(x);
        }

        var windows = runs
            .Where(r => r.Count >= minWindowHours)
            .Select(r => ToWindow(r, thresholds))
            .ToList();
        if (windows.Count > 0)
            return new WeatherWindowCut(windows, null, firstDate, lastDate);

        var blocked = examined.Where(x => x.Verdict.IsBlocked).ToList();
        var reason = runs.Count > 0 ? WeatherWindowMissReason.NoWindowLongEnough
            : blocked.Count > 0 ? WeatherWindowMissReason.AllHoursBlocked
            : WeatherWindowMissReason.NoWeatherData;

        WeatherSignal? blocking = null;
        double? closest = null;
        double? threshold = null;
        if (blocked.Count > 0)
        {
            // OrderByDescending is stable, so ties keep the enum's declared order: Rain, Heat, Sun.
            var signal = Enum.GetValues<WeatherSignal>()
                .OrderByDescending(s => blocked.Count(x => x.Verdict.Failed.Contains(s)))
                .First();
            blocking = signal;
            // Only hours THIS signal blocked: an hour it passed says nothing about how far to relax it.
            closest = blocked
                .Where(x => x.Verdict.Failed.Contains(signal))
                .Min(x => WeatherHourJudge.ValueOf(x.Hour, signal));
            threshold = thresholds.Of(signal);
        }

        var miss = new WeatherWindowMissDto(
            reason, blocking, closest, threshold,
            HoursExamined: examined.Count,
            HoursBlocked: blocked.Count,
            LongestRunHours: runs.Count == 0 ? 0 : runs.Max(r => r.Count));
        return new WeatherWindowCut(Array.Empty<WeatherWindowDto>(), miss, firstDate, lastDate);
    }

    private static WeatherWindowDto ToWindow(List<Examined> run, WeatherThresholds t)
    {
        bool Selected(WeatherSignal s) => t.Selected.Contains(s);
        return new WeatherWindowDto(
            Date: run[0].BandDate,
            StartLocal: run[0].Hour.DisplayLocal,
            EndLocalExclusive: run[^1].Hour.DisplayLocal.AddHours(1),
            Hours: run.Count,
            WorstRainPct: Selected(WeatherSignal.Rain) ? run.Max(x => x.Hour.RainPct) : null,
            WorstFeelsLikeC: Selected(WeatherSignal.Heat) ? run.Max(x => x.Hour.FeelsLikeC) : null,
            WorstUvIndex: Selected(WeatherSignal.Sun) ? run.Max(x => x.Hour.UvIndex) : null);
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherWindowSelectionTests"`
Expected: PASS, 18 tests.

- [ ] **Step 7: Run the whole backend suite**

Run: `dotnet test backend --configuration Release`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums/WeatherWindowMissReason.cs \
        backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
        backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowSelection.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowSelectionTests.cs
git commit -m "feat(weather): cut an hourly forecast into Weather windows (#153)"
```

---

### Task 4: The query and its validator

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsValidator.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsValidatorTests.cs`

**Interfaces:**
- Consumes: `WeatherSignal` (Task 1); `WeatherWindowResultDto` (Task 3).
- Produces:
  - `sealed record FindWeatherWindowsQuery(double Lat, double Lng, IReadOnlyList<WeatherSignal> Signals, DateOnly? FromDate = null, DateOnly? ToDate = null, int? FromHour = null, int? ToHour = null, int? MaxRainPct = null, double? MaxFeelsLikeC = null, int? MaxUvIndex = null, int? MinWindowHours = null) : IQuery<WeatherWindowResultDto>`
  - `sealed class FindWeatherWindowsValidator : AbstractValidator<FindWeatherWindowsQuery>` — picked up automatically by `AddValidatorsFromAssembly` in `MenuNest.Application/DependencyInjection.cs`.

**Rules:**

| field | rule | why |
|---|---|---|
| `Lat` / `Lng` | `[-90, 90]` / `[-180, 180]` | same as `GetHourlyForecastValidator` |
| `Signals` | not null, not empty, every value a defined enum member | empty is not "everything is good" (spec §4.1) |
| `FromHour` | `0–23` when given | |
| `ToHour` | `1–24` when given | `ToHour <= FromHour` is legal: the band wraps |
| `FromDate` / `ToDate` | `FromDate <= ToDate` when both given | |
| `MaxRainPct` | `1–100` when given | |
| `MaxFeelsLikeC` | `1–60` when given | |
| `MaxUvIndex` | `1–20` when given | |
| each `Max*` | must be null unless its signal is in `Signals` | menunest-220: silently ignoring it would hide a misunderstanding |
| `MinWindowHours` | `1–24` when given | |

**Why the thresholds start at 1, not 0 (a refinement of spec §4.1's `0–100`):** blocking is `>=`, so an explicit `0` would block every hour — a query that can only ever answer "no". The stored `0` means **off**, but a per-call value is never "off": a caller who wants no gate on a signal leaves that signal out of `Signals`. Rejecting `0` stops the assistant confusing the two.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsValidatorTests.cs`:

```csharp
using FluentValidation.TestHelper;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class FindWeatherWindowsValidatorTests
{
    private readonly FindWeatherWindowsValidator _v = new();

    private static FindWeatherWindowsQuery Q(params WeatherSignal[] signals)
        => new(19.36, 98.44, signals.Length == 0 ? Array.Empty<WeatherSignal>() : signals);

    [Fact]
    public void Accepts_the_minimal_query()
        => _v.TestValidate(Q(WeatherSignal.Rain)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Accepts_a_band_that_wraps_past_midnight()
        => _v.TestValidate(Q(WeatherSignal.Rain) with { FromHour = 18, ToHour = 2 })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Accepts_thresholds_for_selected_signals()
        => _v.TestValidate(Q(WeatherSignal.Rain, WeatherSignal.Heat, WeatherSignal.Sun)
                with { MaxRainPct = 70, MaxFeelsLikeC = 38.5, MaxUvIndex = 8 })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Rejects_an_empty_signal_list()
        => _v.TestValidate(Q()).ShouldHaveValidationErrorFor(x => x.Signals);

    [Fact]
    public void Rejects_a_null_signal_list()
        => _v.TestValidate(Q(WeatherSignal.Rain) with { Signals = null! })
            .ShouldHaveValidationErrorFor(x => x.Signals);

    [Fact]
    public void Rejects_an_undefined_signal()
        => _v.TestValidate(Q((WeatherSignal)99)).ShouldHaveAnyValidationError();

    [Fact]
    public void Rejects_a_rain_threshold_when_rain_is_not_selected()
        => _v.TestValidate(Q(WeatherSignal.Heat) with { MaxRainPct = 70 })
            .ShouldHaveValidationErrorFor(x => x.MaxRainPct);

    [Fact]
    public void Rejects_a_feels_like_threshold_when_heat_is_not_selected()
        => _v.TestValidate(Q(WeatherSignal.Sun) with { MaxFeelsLikeC = 38 })
            .ShouldHaveValidationErrorFor(x => x.MaxFeelsLikeC);

    [Fact]
    public void Rejects_a_uv_threshold_when_sun_is_not_selected()
        => _v.TestValidate(Q(WeatherSignal.Heat) with { MaxUvIndex = 8 })
            .ShouldHaveValidationErrorFor(x => x.MaxUvIndex);

    [Fact]
    public void Rejects_an_explicit_threshold_of_zero()
        => _v.TestValidate(Q(WeatherSignal.Rain) with { MaxRainPct = 0 })
            .ShouldHaveValidationErrorFor(x => x.MaxRainPct);

    [Fact]
    public void Rejects_a_from_date_after_the_to_date()
        => _v.TestValidate(Q(WeatherSignal.Rain) with
            { FromDate = new DateOnly(2026, 9, 22), ToDate = new DateOnly(2026, 9, 21) })
            .ShouldHaveAnyValidationError();

    [Theory]
    [InlineData(24, null)]
    [InlineData(-1, null)]
    [InlineData(null, 0)]
    [InlineData(null, 25)]
    public void Rejects_hours_outside_the_band_ranges(int? fromHour, int? toHour)
        => _v.TestValidate(Q(WeatherSignal.Rain) with { FromHour = fromHour, ToHour = toHour })
            .ShouldHaveAnyValidationError();

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Rejects_a_minimum_window_length_outside_1_to_24(int min)
        => _v.TestValidate(Q(WeatherSignal.Rain) with { MinWindowHours = min })
            .ShouldHaveValidationErrorFor(x => x.MinWindowHours);

    [Fact]
    public void Rejects_an_out_of_range_latitude()
        => _v.TestValidate(Q(WeatherSignal.Rain) with { Lat = 91 })
            .ShouldHaveValidationErrorFor(x => x.Lat);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~FindWeatherWindowsValidatorTests"`
Expected: FAIL — build error, `FindWeatherWindowsQuery` / `FindWeatherWindowsValidator` do not exist.

- [ ] **Step 3: Write the query**

Create `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsQuery.cs`:

```csharp
using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>Find the Weather windows at a bare location (issue #153, menunest-217). Every parameter
/// after Signals is optional; the defaults live in the handler and in WeatherWindowThresholds.</summary>
public sealed record FindWeatherWindowsQuery(
    double Lat,
    double Lng,
    IReadOnlyList<WeatherSignal> Signals,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int? FromHour = null,
    int? ToHour = null,
    int? MaxRainPct = null,
    double? MaxFeelsLikeC = null,
    int? MaxUvIndex = null,
    int? MinWindowHours = null) : IQuery<WeatherWindowResultDto>;
```

- [ ] **Step 4: Write the validator**

Create `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsValidator.cs`:

```csharp
using FluentValidation;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

public sealed class FindWeatherWindowsValidator : AbstractValidator<FindWeatherWindowsQuery>
{
    public FindWeatherWindowsValidator()
    {
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180);

        RuleFor(x => x.Signals)
            .NotEmpty()
            .WithMessage("Select at least one signal: Rain, Heat or Sun. For raw hours use the hourly forecast.");
        RuleForEach(x => x.Signals).IsInEnum();

        RuleFor(x => x.FromHour!.Value).InclusiveBetween(0, 23)
            .When(x => x.FromHour is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.FromHour));
        RuleFor(x => x.ToHour!.Value).InclusiveBetween(1, 24)
            .When(x => x.ToHour is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.ToHour));

        RuleFor(x => x)
            .Must(x => x.FromDate is null || x.ToDate is null || x.FromDate <= x.ToDate)
            .WithMessage("fromDate must not be after toDate.");

        // A per-call threshold is never "off" (that is the stored 0); to drop a gate, leave the signal out.
        RuleFor(x => x.MaxRainPct!.Value).InclusiveBetween(1, 100)
            .When(x => x.MaxRainPct is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.MaxRainPct));
        RuleFor(x => x.MaxFeelsLikeC!.Value).InclusiveBetween(1, 60)
            .When(x => x.MaxFeelsLikeC is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.MaxFeelsLikeC));
        RuleFor(x => x.MaxUvIndex!.Value).InclusiveBetween(1, 20)
            .When(x => x.MaxUvIndex is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.MaxUvIndex));

        // menunest-220: a threshold for an unselected signal means the caller misunderstands the tool.
        RuleFor(x => x.MaxRainPct).Null()
            .When(x => x.Signals is not null && !x.Signals.Contains(WeatherSignal.Rain))
            .WithMessage("maxRainPct was given but Rain is not in signals.");
        RuleFor(x => x.MaxFeelsLikeC).Null()
            .When(x => x.Signals is not null && !x.Signals.Contains(WeatherSignal.Heat))
            .WithMessage("maxFeelsLikeC was given but Heat is not in signals.");
        RuleFor(x => x.MaxUvIndex).Null()
            .When(x => x.Signals is not null && !x.Signals.Contains(WeatherSignal.Sun))
            .WithMessage("maxUvIndex was given but Sun is not in signals.");

        RuleFor(x => x.MinWindowHours!.Value).InclusiveBetween(1, 24)
            .When(x => x.MinWindowHours is not null).OverridePropertyName(nameof(FindWeatherWindowsQuery.MinWindowHours));
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~FindWeatherWindowsValidatorTests"`
Expected: PASS, 18 test cases (12 facts + 6 theory rows). If a `ShouldHaveValidationErrorFor(x => x.MaxRainPct)` fails while the rule clearly fired, the property name did not match — check the `OverridePropertyName` on that rule, never loosen the test to `ShouldHaveAnyValidationError`.

- [ ] **Step 6: Run the whole backend suite**

Run: `dotnet test backend --configuration Release`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsQuery.cs \
        backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsValidator.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsValidatorTests.cs
git commit -m "feat(weather): FindWeatherWindows query and validator (#153)"
```

---

### Task 5: The handler

Orchestrates: validate, read `UserSettings`, resolve thresholds, size the fetch, cut, report what was searched and whether the horizon cut it short.

**Why the fetch is padded.** `GetHourlyAsync` takes a count of hours **from now**; `ToDate` is a **local** date at the point, and the handler does not know the point's UTC offset. A local date is at most one day either side of the UTC date, so the handler asks for `(daysAhead + 2) × 24` hours — `+ 3` when the band wraps, because the last band's tail runs into the next morning — clamped to `[24, 240]`. A multiple of 24 also keeps `GetHourlyAsync`'s `hours`-keyed cache from fragmenting across callers (spec §9).

**Why truncation is measured against the band, not the date.** The forecast ends at *now + 240 h*, which usually lands in the middle of a day. Comparing only dates would call a half-covered last day "covered". So `HorizonTruncated` is true when the provider's last bucket is earlier than the **last band hour of `ToDate`** — `ToDate` at `ToHour − 1`, or the next day at `ToHour − 1` for a wrapping band. A morning-only band on a half-covered last day is correctly *not* truncated.

**What was searched.** `SearchedFrom` / `SearchedThrough` are the first and last band dates actually examined (`WeatherWindowCut.FirstExaminedDate` / `LastExaminedDate`). When nothing was examined, both fall back to `FromDate ?? today`; `Miss.HoursExamined == 0` tells the caller that nothing was searched.

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsHandlerTests.cs`

**Interfaces:**
- Consumes: `FindWeatherWindowsQuery`, `FindWeatherWindowsValidator` (Task 4); `WeatherWindowResultDto` (Task 3); `WeatherWindowThresholds.Resolve` (Task 1); `WeatherWindowSelection.Cut`, `WeatherWindowCut` (Task 3); `IWeatherService.GetHourlyAsync(WeatherPoint, int, CancellationToken)`; `WeatherPoint(string StopId, double Lat, double Lng, DateTime? ArrivalLocal)`; `IApplicationDbContext.UserSettings`; `IUserProvisioner.GetOrProvisionCurrentAsync(CancellationToken)`; `IClock.UtcNow`.
- Produces: `sealed class FindWeatherWindowsHandler : IQueryHandler<FindWeatherWindowsQuery, WeatherWindowResultDto>` with constructor `(IWeatherService weather, IApplicationDbContext db, IUserProvisioner users, IClock clock, IValidator<FindWeatherWindowsQuery> validator)`, and `public const int DefaultMinWindowHours = 2`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsHandlerTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class FindWeatherWindowsHandlerTests : IDisposable
{
    private sealed class StubWeather : IWeatherService
    {
        public IReadOnlyList<HourlyReading> Hours = Array.Empty<HourlyReading>();
        public int RequestedHours;

        public Task<IReadOnlyList<WeatherReading>> GetReadingsAsync(
            IReadOnlyList<WeatherPoint> points, WeatherReadingKind kind, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WeatherReading>>(Array.Empty<WeatherReading>());

        public Task<IReadOnlyList<HourlyReading>> GetHourlyAsync(WeatherPoint point, int hours, CancellationToken ct)
        {
            RequestedHours = hours;
            return Task.FromResult(Hours);
        }
    }

    // A fixed clock makes every date below absolute and every test deterministic.
    private static readonly DateOnly Day0 = new(2026, 9, 20);
    private readonly FixedClock _clock = new(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));

    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users;
    private readonly StubWeather _weather = new();

    public FindWeatherWindowsHandlerTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
        _users = new Mock<IUserProvisioner>();
        _users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    /// Writes the User's Weather-alert threshold. The "missing row" tests deliberately never call
    /// this: the row is created lazily on first write, so most Users have none.
    private void SeedThresholds(int? uv, int? feels)
    {
        var s = UserSettings.Create(_user.Id);
        s.SetWeatherAlerts(uv, feels);
        _db.UserSettings.Add(s);
        _db.SaveChanges();
    }

    private FindWeatherWindowsHandler Handler()
        => new(_weather, _db, _users.Object, _clock, new FindWeatherWindowsValidator());

    private static HourlyReading H(int day, int hour, int? rain = 10, double? feels = 30, int? uv = 2)
        => new(Day0.AddDays(day).ToDateTime(new TimeOnly(hour, 0)), true, 28, feels, "CLEAR", null, rain, uv);

    /// <summary>`count` consecutive hours starting at day/hour.</summary>
    private static HourlyReading[] Hours(int day, int hour, int count)
        => Enumerable.Range(0, count)
            .Select(i => Day0.AddDays(day).ToDateTime(new TimeOnly(hour, 0)).AddHours(i))
            .Select(t => new HourlyReading(t, true, 28, 30, "CLEAR", null, 10, 2))
            .ToArray();

    private static FindWeatherWindowsQuery Q(params WeatherSignal[] signals)
        => new(19.36, 98.44, signals.Length == 0 ? new[] { WeatherSignal.Rain } : signals);

    private Task<WeatherWindowResultDto> Run(FindWeatherWindowsQuery q)
        => Handler().Handle(q, CancellationToken.None).AsTask();

    // ── thresholds (menunest-219) ─────────────────────────────────────────────

    [Fact]
    public async Task Returns_windows_and_no_miss_when_hours_pass()
    {
        _weather.Hours = new[] { H(0, 6), H(0, 7), H(0, 8) };

        var result = await Run(Q());

        result.Miss.Should().BeNull();
        result.Windows.Should().ContainSingle().Which.Hours.Should().Be(3);
    }

    [Fact]
    public async Task With_no_UserSettings_row_the_built_in_default_passes_39C()
    {
        _weather.Hours = new[] { H(0, 6, feels: 39), H(0, 7, feels: 39) };

        var result = await Run(Q(WeatherSignal.Heat));

        result.Windows.Should().ContainSingle();
    }

    [Fact]
    public async Task With_no_UserSettings_row_the_built_in_default_blocks_40C_exactly()
    {
        _weather.Hours = new[] { H(0, 6, feels: 40), H(0, 7, feels: 41) };

        var result = await Run(Q(WeatherSignal.Heat));

        result.Windows.Should().BeEmpty();
        result.Miss!.Reason.Should().Be(WeatherWindowMissReason.AllHoursBlocked);
        result.Miss.Threshold.Should().Be(40);
        result.Miss.ClosestValue.Should().Be(40);
    }

    [Fact]
    public async Task Honours_the_stored_Weather_alert_threshold()
    {
        SeedThresholds(uv: null, feels: 38);
        _weather.Hours = new[] { H(0, 6, feels: 39), H(0, 7, feels: 39) };

        var result = await Run(Q(WeatherSignal.Heat));

        result.Miss!.Reason.Should().Be(WeatherWindowMissReason.AllHoursBlocked);
        result.Miss.Threshold.Should().Be(38);
    }

    [Fact]
    public async Task A_stored_zero_turns_the_signal_off()
    {
        SeedThresholds(uv: null, feels: 0);
        _weather.Hours = new[] { H(0, 6, feels: 44), H(0, 7, feels: 45) };

        var result = await Run(Q(WeatherSignal.Heat));

        result.Windows.Should().ContainSingle();
        result.Windows[0].WorstFeelsLikeC.Should().Be(45, "an off signal is still selected, so its worst value is reported");
    }

    [Fact]
    public async Task An_explicit_value_beats_the_stored_threshold()
    {
        SeedThresholds(uv: null, feels: 38);
        _weather.Hours = new[] { H(0, 6, feels: 39), H(0, 7, feels: 39) };

        var result = await Run(Q(WeatherSignal.Heat) with { MaxFeelsLikeC = 41 });

        result.Windows.Should().ContainSingle();
    }

    [Fact]
    public async Task Applies_the_default_minimum_window_length_of_two_hours()
    {
        _weather.Hours = new[] { H(0, 6), H(0, 7, rain: 90), H(0, 8), H(0, 9) };

        var result = await Run(Q());

        result.Windows.Should().ContainSingle().Which.StartLocal.Hour.Should().Be(8);
    }

    // ── provider failure ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_empty_provider_result_is_NoWeatherData_and_not_truncation()
    {
        _weather.Hours = Array.Empty<HourlyReading>();

        var result = await Run(Q() with { ToDate = Day0.AddDays(3) });

        result.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
        result.HorizonTruncated.Should().BeFalse("a failed provider call is not a horizon limit");
        result.SearchedFrom.Should().Be(Day0);
        result.SearchedThrough.Should().Be(Day0);
    }

    // ── what was searched, and truncation ─────────────────────────────────────

    [Fact]
    public async Task Flags_truncation_when_the_forecast_ends_before_ToDate()
    {
        _weather.Hours = Hours(0, 6, 24);

        var result = await Run(Q() with { ToDate = Day0.AddDays(9) });

        result.HorizonTruncated.Should().BeTrue();
        result.SearchedThrough.Should().Be(Day0.AddDays(1));
    }

    [Fact]
    public async Task A_half_covered_last_day_is_truncated_for_the_whole_day_band()
    {
        // Forecast ends at day1 12:00; the default band's last hour on day1 is 23:00.
        _weather.Hours = Hours(0, 0, 37);

        var result = await Run(Q() with { ToDate = Day0.AddDays(1) });

        result.HorizonTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task A_half_covered_last_day_is_not_truncated_for_a_morning_band_it_covers()
    {
        // Same forecast; the band 08-13 ends at 12:00 on day1, which the forecast reaches.
        _weather.Hours = Hours(0, 0, 37);

        var result = await Run(Q() with { ToDate = Day0.AddDays(1), FromHour = 8, ToHour = 13 });

        result.HorizonTruncated.Should().BeFalse();
        result.SearchedThrough.Should().Be(Day0.AddDays(1));
    }

    [Fact]
    public async Task No_ToDate_means_the_whole_horizon_and_is_never_truncated()
    {
        _weather.Hours = Hours(0, 0, 30);

        var result = await Run(Q());

        result.HorizonTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task SearchedFrom_reports_the_first_date_actually_examined()
    {
        _weather.Hours = Hours(0, 0, 48);

        var result = await Run(Q() with { FromDate = Day0.AddDays(1) });

        result.SearchedFrom.Should().Be(Day0.AddDays(1));
    }

    // ── fetch sizing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Requests_the_whole_horizon_when_no_ToDate_is_given()
    {
        await Run(Q());

        _weather.RequestedHours.Should().Be(240);
    }

    [Fact]
    public async Task Requests_whole_days_with_one_day_of_timezone_padding_each_side()
    {
        await Run(Q() with { ToDate = Day0.AddDays(1) });

        _weather.RequestedHours.Should().Be(72); // (1 day ahead + 2) * 24
    }

    [Fact]
    public async Task Requests_one_more_day_when_the_band_wraps_past_midnight()
    {
        await Run(Q() with { ToDate = Day0.AddDays(1), FromHour = 18, ToHour = 2 });

        _weather.RequestedHours.Should().Be(96); // (1 + 3) * 24
    }

    [Fact]
    public async Task Never_requests_more_than_the_horizon()
    {
        await Run(Q() with { ToDate = Day0.AddDays(30) });

        _weather.RequestedHours.Should().Be(240);
    }

    // ── validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_an_empty_signal_list_before_calling_the_provider()
    {
        var act = () => Run(new FindWeatherWindowsQuery(19.36, 98.44, Array.Empty<WeatherSignal>()));

        await act.Should().ThrowAsync<ValidationException>();
        _weather.RequestedHours.Should().Be(0);
    }
}
```

**If a call above does not compile,** check the real signature in the repo and fix the *test*, never the production code. The ones that matter: `User.CreateFromExternalLogin(externalId, email, displayName, AuthProvider)`, `UserSettings.Create(Guid userId)`, `SetWeatherAlerts(int? uv, int? feels)`, `FixedClock(DateTime utcNow)`. The SQLite setup is copied from `Trips/AttachChecklistItemRelationalTests.cs`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~FindWeatherWindowsHandlerTests"`
Expected: FAIL — build error, `FindWeatherWindowsHandler` does not exist.

- [ ] **Step 3: Write the handler**

Create `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsHandler.cs`:

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>Find the Weather windows at a bare location (issue #153). The first backend reader of
/// the User's Weather-alert threshold (menunest-219); reuses the existing forecast/hours walk, so no
/// new billing SKU (ADR-119); persists nothing (ADR-033).</summary>
public sealed class FindWeatherWindowsHandler
    : IQueryHandler<FindWeatherWindowsQuery, WeatherWindowResultDto>
{
    public const int DefaultMinWindowHours = 2; // menunest-224
    private const int HorizonHours = 240;       // ADR-031
    private const int DefaultFromHour = 0;
    private const int DefaultToHour = 24;

    private readonly IWeatherService _weather;
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IClock _clock;
    private readonly IValidator<FindWeatherWindowsQuery> _validator;

    public FindWeatherWindowsHandler(
        IWeatherService weather, IApplicationDbContext db, IUserProvisioner users,
        IClock clock, IValidator<FindWeatherWindowsQuery> validator)
    { _weather = weather; _db = db; _users = users; _clock = clock; _validator = validator; }

    public async ValueTask<WeatherWindowResultDto> Handle(FindWeatherWindowsQuery q, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(q, ct);

        var user = await _users.GetOrProvisionCurrentAsync(ct);
        // Created lazily on first write: a User who never opened /settings has NO row, and that
        // must resolve exactly like null — the built-in defaults.
        var settings = await _db.UserSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == user.Id, ct);

        var thresholds = WeatherWindowThresholds.Resolve(
            q.Signals, q.MaxRainPct, q.MaxFeelsLikeC, q.MaxUvIndex,
            settings?.UvWarnThreshold, settings?.FeelsLikeWarnThreshold);

        var fromHour = q.FromHour ?? DefaultFromHour;
        var toHour = q.ToHour ?? DefaultToHour;
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        var hours = await _weather.GetHourlyAsync(
            new WeatherPoint("", q.Lat, q.Lng, null), HoursToFetch(q.ToDate, today, fromHour, toHour), ct);

        var cut = WeatherWindowSelection.Cut(
            hours, thresholds, q.FromDate, q.ToDate, fromHour, toHour,
            q.MinWindowHours ?? DefaultMinWindowHours);

        var searchedFrom = cut.FirstExaminedDate ?? q.FromDate ?? today;
        return new WeatherWindowResultDto(
            cut.Windows,
            cut.Miss,
            HorizonTruncated: q.ToDate is { } to && hours.Count > 0
                && hours.Max(h => h.DisplayLocal) < LastBandHour(to, fromHour, toHour),
            SearchedFrom: searchedFrom,
            SearchedThrough: cut.LastExaminedDate ?? searchedFrom);
    }

    /// <summary>ToDate is a LOCAL date at the point and its UTC offset is unknown, so pad one day each
    /// side (plus one for a wrapping band's after-midnight tail). Whole days keep GetHourlyAsync's
    /// hours-keyed cache shared between callers (spec §9).</summary>
    private static int HoursToFetch(DateOnly? toDate, DateOnly today, int fromHour, int toHour)
    {
        if (toDate is not { } to) return HorizonHours;
        var padding = toHour > fromHour ? 2 : 3;
        return Math.Clamp((to.DayNumber - today.DayNumber + padding) * 24, 24, HorizonHours);
    }

    /// <summary>The last hour the band covers for ToDate — the next morning when the band wraps.</summary>
    private static DateTime LastBandHour(DateOnly toDate, int fromHour, int toHour)
    {
        var date = toHour > fromHour ? toDate : toDate.AddDays(1);
        return date.ToDateTime(new TimeOnly(toHour - 1, 0));
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~FindWeatherWindowsHandlerTests"`
Expected: PASS, 18 tests.

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test backend --configuration Release`
Expected: PASS. `IClock` is already registered as a singleton in `MenuNest.Infrastructure/DependencyInjection.cs`, and Mediator's source generator picks up the new handler, so no DI change is needed. If the build reports the handler unregistered, stop and report — do not hand-register it.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsHandlerTests.cs
git commit -m "feat(weather): FindWeatherWindows handler reads the User's threshold (#153)"
```

---

### Task 6: The HTTP endpoint

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` — add a `using` and one action beside `HourlyWeather` (currently at lines 144–146)
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Controllers/TripsControllerWeatherWindowsTests.cs`

**Interfaces:**
- Consumes: `FindWeatherWindowsQuery` (Task 4), `WeatherWindowResultDto` (Task 3).
- Produces: `public async Task<ActionResult<WeatherWindowResultDto>> WeatherWindows([FromBody] FindWeatherWindowsQuery q, CancellationToken ct)` on `TripsController`, routed `POST api/trips/weather/windows`.

Authenticated by the global fallback policy; no resource authorization — the same as its two siblings `api/trips/weather` and `api/trips/weather/hourly`.

**Why a JSON binding test.** ADR-156: System.Text.Json silently drops unknown members at model binding, so a test that only builds the query in C# proves nothing about what a real request body becomes. The test below deserialises a real body with the same options `Program.cs` uses (web defaults + `JsonStringEnumConverter`), so a renamed parameter or a string enum that fails to bind turns the test red.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Controllers/TripsControllerWeatherWindowsTests.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using MenuNest.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Controllers;

public sealed class TripsControllerWeatherWindowsTests
{
    // The same shape Program.cs configures for MVC: web defaults + string enums.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void The_action_is_routed_POST_api_trips_weather_windows()
    {
        var method = typeof(TripsController).GetMethod(nameof(TripsController.WeatherWindows))!;

        method.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("api/trips/weather/windows");
    }

    [Fact]
    public void A_minimal_body_binds_with_string_signals_and_every_optional_left_null()
    {
        var q = JsonSerializer.Deserialize<FindWeatherWindowsQuery>(
            """{ "lat": 19.36, "lng": 98.44, "signals": ["Heat", "Sun"] }""", Json)!;

        q.Lat.Should().Be(19.36);
        q.Lng.Should().Be(98.44);
        q.Signals.Should().Equal(WeatherSignal.Heat, WeatherSignal.Sun);
        q.FromDate.Should().BeNull();
        q.ToDate.Should().BeNull();
        q.FromHour.Should().BeNull();
        q.MaxFeelsLikeC.Should().BeNull();
        q.MinWindowHours.Should().BeNull();
    }

    [Fact]
    public void A_full_body_binds_every_member()
    {
        var q = JsonSerializer.Deserialize<FindWeatherWindowsQuery>("""
            { "lat": 19.36, "lng": 98.44, "signals": ["Rain"],
              "fromDate": "2026-09-26", "toDate": "2026-09-27",
              "fromHour": 8, "toHour": 12, "maxRainPct": 70, "minWindowHours": 3 }
            """, Json)!;

        q.FromDate.Should().Be(new DateOnly(2026, 9, 26));
        q.ToDate.Should().Be(new DateOnly(2026, 9, 27));
        q.FromHour.Should().Be(8);
        q.ToHour.Should().Be(12);
        q.MaxRainPct.Should().Be(70);
        q.MinWindowHours.Should().Be(3);
    }

    [Fact]
    public async Task WeatherWindows_sends_the_body_unchanged_and_returns_the_result()
    {
        var mediator = new Mock<IMediator>();
        var q = new FindWeatherWindowsQuery(19.36, 98.44, new[] { WeatherSignal.Rain }, MaxRainPct: 70);
        var dto = new WeatherWindowResultDto(
            Array.Empty<WeatherWindowDto>(), null, false, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 29));
        mediator
            .Setup(m => m.Send(It.Is<FindWeatherWindowsQuery>(x => x == q), It.IsAny<CancellationToken>()))
            .Returns<FindWeatherWindowsQuery, CancellationToken>((_, _) => new ValueTask<WeatherWindowResultDto>(dto));

        var result = await new TripsController(mediator.Object).WeatherWindows(q, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(dto);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter "FullyQualifiedName~TripsControllerWeatherWindowsTests"`
Expected: FAIL — build error, `TripsController.WeatherWindows` does not exist.

- [ ] **Step 3: Add the action**

In `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`, add this `using` beside the other `UseCases.Trips.*` usings (after `using MenuNest.Application.UseCases.Trips.DeleteTripPlace;`):

```csharp
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
```

Then add this action directly after the `HourlyWeather` action:

```csharp
    /// <summary>Weather windows at a bare location (issue #153). Authenticated by the fallback policy,
    /// no resource check — the same as the two weather reads above it.</summary>
    [HttpPost("api/trips/weather/windows")]
    public async Task<ActionResult<WeatherWindowResultDto>> WeatherWindows([FromBody] FindWeatherWindowsQuery q, CancellationToken ct)
        => Ok(await _mediator.Send(q, ct));
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter "FullyQualifiedName~TripsControllerWeatherWindowsTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test backend --configuration Release`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
        backend/tests/MenuNest.WebApi.UnitTests/Controllers/TripsControllerWeatherWindowsTests.cs
git commit -m "feat(weather): POST api/trips/weather/windows (#153)"
```

---

### Task 7: The MCP tool

The tool **description is the behaviour.** It is the only place an MCP client learns to map "วันไหนแดดไม่ร้อน" onto `[Heat, Sun]` (menunest-220), that blocking is `>=`, and that an empty result is not bad weather. So it is pinned by test, as `BudgetToolsTests` already pins `list_budget_accounts`.

**Optional parameters must be optional in the schema.** The live MenuNest MCP schema shows that a nullable C# parameter **without** a default is listed in `required` (`create_trip` requires `destination` and `isDaily`). With eight optional parameters, that would force the assistant to send eight explicit nulls on every call. Every optional parameter therefore gets `= null`, and `CancellationToken ct = default` (C# requires it once optionals precede it). A schema test proves `required` is exactly `lat`, `lng`, `signals`.

**Files:**
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` — add a `using` and one method directly after `get_stop_hourly_forecast`
- Modify: `backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsTests.cs` — add tests at the end of the class

**Interfaces:**
- Consumes: `FindWeatherWindowsQuery` (Task 4), `WeatherWindowResultDto` (Task 3), `WeatherSignal` (Task 1).
- Produces: `public async Task<WeatherWindowResultDto> find_weather_windows(double lat, double lng, WeatherSignal[] signals, DateOnly? fromDate = null, DateOnly? toDate = null, int? fromHour = null, int? toHour = null, int? maxRainPct = null, double? maxFeelsLikeC = null, int? maxUvIndex = null, int? minWindowHours = null, CancellationToken ct = default)` on `TripTools`.

- [ ] **Step 1: Write the failing test**

Add these usings to the top of `backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsTests.cs` (the test project already has global usings for xUnit, `System.ComponentModel` is needed for `DescriptionAttribute`):

```csharp
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using ModelContextProtocol.Server;
```

Then add these tests at the end of the `TripToolsTests` class:

```csharp
    // ── find_weather_windows (#153) ───────────────────────────────────────────

    [Fact]
    public async Task find_weather_windows_sends_the_query_with_every_argument()
    {
        var dto = new WeatherWindowResultDto(
            Array.Empty<WeatherWindowDto>(), null, false, new DateOnly(2026, 9, 26), new DateOnly(2026, 9, 27));
        _mediator
            .Setup(m => m.Send(It.IsAny<FindWeatherWindowsQuery>(), It.IsAny<CancellationToken>()))
            .Returns<FindWeatherWindowsQuery, CancellationToken>((_, _) => new ValueTask<WeatherWindowResultDto>(dto));

        var result = await _sut.find_weather_windows(
            19.36, 98.44, new[] { WeatherSignal.Heat, WeatherSignal.Sun },
            fromDate: new DateOnly(2026, 9, 26), toDate: new DateOnly(2026, 9, 27),
            fromHour: 8, toHour: 12, maxFeelsLikeC: 38, maxUvIndex: 7, minWindowHours: 3,
            ct: CancellationToken.None);

        result.Should().BeSameAs(dto);
        _mediator.Verify(m => m.Send(It.Is<FindWeatherWindowsQuery>(q =>
            q.Lat == 19.36 && q.Lng == 98.44
            && q.Signals.SequenceEqual(new[] { WeatherSignal.Heat, WeatherSignal.Sun })
            && q.FromDate == new DateOnly(2026, 9, 26) && q.ToDate == new DateOnly(2026, 9, 27)
            && q.FromHour == 8 && q.ToHour == 12
            && q.MaxRainPct == null && q.MaxFeelsLikeC == 38 && q.MaxUvIndex == 7
            && q.MinWindowHours == 3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void find_weather_windows_requires_only_lat_lng_and_signals()
    {
        var method = typeof(TripTools).GetMethod(nameof(TripTools.find_weather_windows))!;
        var tool = McpServerTool.Create(method, new TripTools(new Mock<IMediator>().Object), null);
        var schema = tool.ProtocolTool.InputSchema;

        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "lat", "lng", "signals" });
        schema.GetProperty("properties").GetProperty("signals").GetRawText()
            .Should().Contain("\"Rain\"").And.Contain("\"Heat\"").And.Contain("\"Sun\"");
    }

    // The description is the ONLY place the assistant learns which signals a Thai question means,
    // that blocking is >=, and that an empty result is not bad weather. A silent edit is a silent
    // behaviour change, so it is pinned.
    [Fact]
    public void find_weather_windows_description_teaches_signal_choice_and_how_to_read_a_miss()
    {
        var description = typeof(TripTools)
            .GetMethod(nameof(TripTools.find_weather_windows))!
            .GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.Should().Contain("resolve_place", "the location must be resolved upstream (menunest-217)");
        description.Should().Contain("แดดไม่ร้อน").And.Contain("[Heat, Sun]");
        description.Should().Contain("ฝนไม่ตก").And.Contain("[Rain]");
        description.Should().Contain("AT OR ABOVE");
        description.Should().Contain("ABOVE closestValue");
        description.Should().Contain("NoWeatherData");
        description.Should().Contain("horizonTruncated");
        description.Should().NotContain("best time", "the term is Weather window, never 'best time' (menunest-223)");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests --filter "FullyQualifiedName~TripToolsTests"`
Expected: FAIL — build error, `TripTools.find_weather_windows` does not exist.

If the **existing** `using` lines in `TripToolsTests.cs` already include one of the usings above, do not add it twice.

- [ ] **Step 3: Add the tool**

In `backend/src/MenuNest.McpServer/Tools/TripTools.cs`, add beside the other `UseCases.Trips.*` usings (after `using MenuNest.Application.UseCases.Trips.GetStopHourlyForecast;`):

```csharp
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
```

Then add this method directly after `get_stop_hourly_forecast`:

```csharp
    [McpServerTool, Description(
        "Find Weather windows at a location: dated runs of consecutive forecast hours in which EVERY selected signal " +
        "stays below its threshold, within the 10-day forecast horizon. Use it when the user asks which day or time at " +
        "a place is not rainy / not hot / not harsh sun — typically BEFORE a trip exists. Get lat/lng from resolve_place first. " +
        "Choose signals from the user's words and add nothing they did not ask about: " +
        "'ฝนไม่ตก' / not raining -> [Rain]; 'ไม่ร้อน' / not hot -> [Heat]; 'แดดไม่แรง' / low UV -> [Sun]; " +
        "'แดดไม่ร้อน' -> [Heat, Sun]; 'ฝนไม่ตกและไม่ร้อน' -> [Rain, Heat]. " +
        "An hour is BLOCKED when a selected value is AT OR ABOVE its threshold. Thresholds default to the user's own " +
        "weather-alert settings (feels-like 40 C and UV 6 unless they changed them; a signal they switched off never blocks) " +
        "and rain 60%. Pass maxRainPct / maxFeelsLikeC / maxUvIndex only for a selected signal and only when the user asks. " +
        "Use fromHour/toHour for a time of day (toHour is exclusive; if toHour <= fromHour the band wraps past midnight, e.g. 18-2). " +
        "If windows is empty, read miss: AllHoursBlocked names blockingSignal, threshold and closestValue — the lowest value " +
        "that blocked an hour. To relax, a threshold must be ABOVE closestValue; offer that to the user, do not do it silently. " +
        "NoWindowLongEnough means good hours exist but in runs shorter than minWindowHours (default 2) — offer a shorter minimum. " +
        "NoWeatherData means there is no forecast for that range — never tell the user the weather is bad. " +
        "horizonTruncated=true means toDate was past the forecast; say which dates were searched (searchedFrom..searchedThrough).")]
    public async Task<WeatherWindowResultDto> find_weather_windows(
        [Description("Latitude, from resolve_place")] double lat,
        [Description("Longitude, from resolve_place")] double lng,
        [Description("Signals that judge an hour: Rain, Heat (feels-like), Sun (UV). At least one.")] WeatherSignal[] signals,
        [Description("First local date to search, YYYY-MM-DD. Default: today.")] DateOnly? fromDate = null,
        [Description("Last local date to search, YYYY-MM-DD. Default: the end of the 10-day forecast.")] DateOnly? toDate = null,
        [Description("Start hour of each day, 0-23 (default 0).")] int? fromHour = null,
        [Description("End hour of each day, 1-24, exclusive (default 24). If <= fromHour the band wraps past midnight.")] int? toHour = null,
        [Description("Rain % that blocks an hour (1-100, default 60). Only with Rain selected.")] int? maxRainPct = null,
        [Description("Feels-like C that blocks an hour (1-60, default the user's setting). Only with Heat selected.")] double? maxFeelsLikeC = null,
        [Description("UV index that blocks an hour (1-20, default the user's setting). Only with Sun selected.")] int? maxUvIndex = null,
        [Description("Shortest window worth returning, in hours (1-24, default 2).")] int? minWindowHours = null,
        CancellationToken ct = default)
        => await mediator.Send(new FindWeatherWindowsQuery(
            lat, lng, signals, fromDate, toDate, fromHour, toHour,
            maxRainPct, maxFeelsLikeC, maxUvIndex, minWindowHours), ct);
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests --filter "FullyQualifiedName~TripToolsTests"`
Expected: PASS — the three existing tests plus the three new ones.

If `find_weather_windows_requires_only_lat_lng_and_signals` fails because `required` still lists the optional parameters, **stop and report**: it means ModelContextProtocol 1.0.0 does not honour C# defaults, and the fix is a design decision (e.g. a single `options` object parameter), not a test edit.

- [ ] **Step 5: Run the whole suite, backend and frontend**

Run: `dotnet test backend --configuration Release`
Expected: PASS.

Run: `cd frontend && npx tsc --noEmit && npm run build && cd ..`
Expected: PASS — nothing in the frontend changed, but the pre-commit hook runs these too, so the last commit proves them.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsTests.cs
git commit -m "feat(weather): find_weather_windows MCP tool (closes #153)"
```

---

## Verification after the last task

- [ ] `dotnet test backend --configuration Release` — PASS.
- [ ] `git log --oneline origin/main..HEAD` shows the seven feature commits, each ending `(#153)` and the last `(closes #153)`.
- [ ] `git diff --stat origin/main...HEAD -- backend` touches **no** file under `Persistence/`, `Migrations/`, or any `*DbContext*.cs` — the "never persist weather" constraint.
- [ ] **After deploy, not before:** from Claude, call `resolve_place` on a Pai map link, then `find_weather_windows` with `signals: ["Rain"]` and no other argument. Expect windows or a `miss` — never an error, never eight nulls in the call. Then repeat with `maxRainPct: 0` and expect a validation error that names `maxRainPct`. **Pushing to `main` deploys to prod; ask before pushing.**

---

## What changed from the first plan, and why

The first plan (`2026-09-19-weather-window-query.md`) is careful and mostly right; this version keeps its seven-task shape and fixes six things found by checking it against the code and the live MCP schema.

| # | First plan | This plan | Why |
|---|---|---|---|
| 1 | `HorizonTruncated` compared **dates** only | compares the provider's last bucket with the **last band hour** of `ToDate` | the forecast ends at *now + 240 h*, mid-day. A half-covered last day read as covered, so the assistant would have said "no evening window on the 29th" about hours it never searched |
| 2 | `SearchedFrom`/`SearchedThrough` from the first/last **provider** bucket | from the first/last **examined** band date | a bucket at 05:00 outside a 08–16 band made the result claim a day whose band had no data |
| 3 | handler read `DateTime.UtcNow`; tests used dates relative to today | handler reads `IClock`; tests use `FixedClock` with absolute dates | the repo already has `IClock`/`FixedClock` for exactly this; relative dates made the fetch-sizing tests depend on the hour they run |
| 4 | optional MCP parameters had no C# defaults | `= null` on every optional, plus a schema test on `required` | the live MCP schema lists nullable-without-default parameters as **required**, so the assistant would have had to send eight nulls per call |
| 5 | fetch sizing ignored the wrapping band | `+3` days instead of `+2` when the band wraps | an 18→02 band on `ToDate` needs the next morning's hours |
| 6 | explicit thresholds accepted `0` (spec §4.1: `0–100`) | explicit thresholds start at `1` | blocking is `>=`, so `0` blocks every hour; the stored `0` means *off*, and the assistant must not confuse the two. **This refines the spec** — update §4.1 if accepted |

Also made explicit, where the first plan left them implicit: the three-state hour verdict (good / blocked / unjudgeable) and that a rejection beats a missing value; that `ClosestValue` is only relaxed by a threshold **above** it; that an off-but-selected signal still reports its `Worst*`; and that `NoWindowLongEnough` also names its blocking signal.
