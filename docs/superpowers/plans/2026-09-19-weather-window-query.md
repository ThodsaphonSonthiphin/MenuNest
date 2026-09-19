# Weather Window Query Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use sp-subagent-driven-development (recommended) or sp-executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the assistant answer "at this location, which days/hours are not rainy / not too hot?" over MCP, so a **User** can pick a date and plan a **Trip**.

**Architecture:** A new read-only CQRS use-case in the Trips module. It reuses `IWeatherService.GetHourlyAsync` (the existing `forecast/hours:lookup` walk — no new provider, no new billing SKU), reads the **User**'s stored **Weather-alert threshold** from `UserSettings`, and cuts the hourly series into **Weather window**s with two pure, fully-unit-tested classes. Exposed as an MCP tool and a matching HTTP endpoint. Nothing is persisted.

**Tech Stack:** .NET / C# · Mediator (source-generated `IQuery`/`IQueryHandler`) · FluentValidation · EF Core (read-only, existing `DbSet<UserSettings>`) · xUnit + Moq + FluentAssertions · ModelContextProtocol (`[McpServerTool]`)

**Spec:** `docs/superpowers/specs/2026-09-19-weather-window-query-design.md`

## Global Constraints

- **Never persist weather.** No entity, no `DbSet`, no migration, no change to any of the four `IApplicationDbContext` implementers (ADR-033). `UserSettings` is **read** through the existing `DbSet`.
- **No new Google call shape.** Only `IWeatherService.GetHourlyAsync`. No new provider, no new billing SKU (ADR-093, menunest-119).
- **Degrade, never throw, on provider failure.** `GetHourlyAsync` already returns an empty list on failure (ADR-030). An empty list means `WeatherWindowMissReason.NoWeatherData`.
- **Mocking is Moq, never NSubstitute.** `new Mock<IUserProvisioner>()`; `Substitute.For<>` does not compile in this repo.
- **Every commit must leave the WHOLE suite green.** `frontend/.husky/pre-commit` runs backend `dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` + `npm run build` on every commit. Never `--no-verify`.
- **Stage narrowly.** Always `git add <explicit paths>`. Never `git add -A` / `git add .` — `daily-state.md` and `AGENTS.md` must never enter a feature commit.
- **Every commit references the ticket.** Subject ends `(#153)`; the final commit of the feature may use `(closes #153)`.
- **Built-in threshold defaults, exact values:** **Feels-like** `40` °C, **UV index** `6`, rain `60` %. Stored `null` = use the default; stored `0` = **off**; **a missing `UserSettings` row behaves exactly like `null`**.
- **Horizon:** 240 hours / 10 days (ADR-031). `GetHourlyAsync` clamps `hours` to `[1, 240]` itself.
- **No frontend work in this plan at all.**

---

## File Structure

| File | Responsibility |
|---|---|
| `backend/src/MenuNest.Domain/Enums/WeatherSignal.cs` (new) | the three **selected signal**s: `Rain`, `Heat`, `Sun` |
| `backend/src/MenuNest.Domain/Enums/WeatherWindowMissReason.cs` (new) | why zero windows came back |
| `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowThresholds.cs` (new) | **pure.** Resolve effective thresholds from selected signals + overrides + stored settings. The server twin of `effectiveThreshold` in `frontend/src/pages/trips/lib/weather.ts` |
| `.../FindWeatherWindows/WeatherHourJudge.cs` (new) | **pure.** Band membership (incl. midnight wrap), band-date attribution, per-hour good/blocked verdict |
| `.../FindWeatherWindows/WeatherWindowSelection.cs` (new) | **pure.** Group good hours into runs, apply `MinWindowHours`, build the windows and the miss record |
| `.../FindWeatherWindows/FindWeatherWindowsQuery.cs` (new) | the query record |
| `.../FindWeatherWindows/FindWeatherWindowsValidator.cs` (new) | FluentValidation rules |
| `.../FindWeatherWindows/FindWeatherWindowsHandler.cs` (new) | fetch sizing, `UserSettings` read, orchestration, horizon truncation |
| `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (modify, append) | `WeatherWindowDto`, `WeatherWindowMissDto`, `WeatherWindowResultDto` |
| `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` (modify) | `POST api/trips/weather/windows` |
| `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (modify) | `find_weather_windows` |

Tests mirror this: one test file per pure class, plus handler, validator and tool-description tests.

---

### Task 1: Threshold resolution (pure)

This is the **first** server-side use of the **Weather-alert threshold**. `UserSettings.UvWarnThreshold` and `UserSettings.FeelsLikeWarnThreshold` are `int?` with the encoding `null` = default, `0` = off, `N` = that value. Until now only the web app decoded it.

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/WeatherSignal.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowThresholds.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowThresholdsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum WeatherSignal { Rain, Heat, Sun }`; `sealed record WeatherThresholds(double? MaxRainPct, double? MaxFeelsLikeC, double? MaxUvIndex)` where **`null` on a field means that signal never blocks an hour**; `static WeatherThresholds WeatherWindowThresholds.Resolve(IReadOnlyList<WeatherSignal> signals, int? explicitRainPct, double? explicitFeelsLikeC, int? explicitUvIndex, int? storedUvWarn, int? storedFeelsWarn)`.

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
    private static readonly IReadOnlyList<WeatherSignal> All =
        new[] { WeatherSignal.Rain, WeatherSignal.Heat, WeatherSignal.Sun };

    [Fact]
    public void Unselected_signals_never_block()
    {
        var t = WeatherWindowThresholds.Resolve(
            new[] { WeatherSignal.Rain }, null, null, null, storedUvWarn: 6, storedFeelsWarn: 38);

        t.MaxRainPct.Should().Be(60);
        t.MaxFeelsLikeC.Should().BeNull();
        t.MaxUvIndex.Should().BeNull();
    }

    [Fact]
    public void A_missing_UserSettings_row_behaves_exactly_like_null()
    {
        var missing = WeatherWindowThresholds.Resolve(All, null, null, null, null, null);

        missing.MaxRainPct.Should().Be(60);
        missing.MaxFeelsLikeC.Should().Be(40);
        missing.MaxUvIndex.Should().Be(6);
    }

    [Fact]
    public void Stored_value_beats_the_built_in_default()
    {
        var t = WeatherWindowThresholds.Resolve(All, null, null, null, storedUvWarn: 8, storedFeelsWarn: 38);

        t.MaxFeelsLikeC.Should().Be(38);
        t.MaxUvIndex.Should().Be(8);
    }

    [Fact]
    public void Stored_zero_means_off_so_that_signal_never_blocks()
    {
        var t = WeatherWindowThresholds.Resolve(All, null, null, null, storedUvWarn: 0, storedFeelsWarn: 0);

        t.MaxFeelsLikeC.Should().BeNull();
        t.MaxUvIndex.Should().BeNull();
        t.MaxRainPct.Should().Be(60);
    }

    [Fact]
    public void An_explicit_override_beats_the_stored_value_and_the_default()
    {
        var t = WeatherWindowThresholds.Resolve(
            All, explicitRainPct: 70, explicitFeelsLikeC: 41, explicitUvIndex: 9,
            storedUvWarn: 0, storedFeelsWarn: 0);

        t.MaxRainPct.Should().Be(70);
        t.MaxFeelsLikeC.Should().Be(41);
        t.MaxUvIndex.Should().Be(9);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run from the repo root:
`dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherWindowThresholdsTests"`
Expected: FAIL — build error, `WeatherSignal` and `WeatherWindowThresholds` do not exist.

- [ ] **Step 3: Write the enum**

Create `backend/src/MenuNest.Domain/Enums/WeatherSignal.cs`:

```csharp
namespace MenuNest.Domain.Enums;

/// <summary>One of the three per-hour cues a Weather window may be judged on, chosen per call by
/// the caller from the question the User asked (menunest-220). Rain = precipitation probability;
/// Heat = Feels-like temperature; Sun = UV index. Heat and Sun stay separate because the
/// Weather-alert threshold already holds two independent numbers (menunest-221, ADR-091).
/// Declaration order is also the tie-break order for the blocking signal.</summary>
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

/// <summary>The resolved upper bound for each signal. A null field means that signal NEVER blocks
/// an hour — either it was not selected, or its threshold is turned off (stored 0).</summary>
public sealed record WeatherThresholds(double? MaxRainPct, double? MaxFeelsLikeC, double? MaxUvIndex);

/// <summary>Server twin of <c>effectiveThreshold</c> in frontend <c>pages/trips/lib/weather.ts</c>
/// (menunest-219). Keep the two in step: this is the third copy of the null/0/N encoding, after
/// weather.ts and pages/settings/weatherAlertControl.ts.</summary>
public static class WeatherWindowThresholds
{
    /// <summary>Built-in defaults, matching FEELS_WARN_DEFAULT / UV_WARN_DEFAULT / RAIN_TINT_THRESHOLD.</summary>
    public const double DefaultMaxFeelsLikeC = 40;
    public const int DefaultMaxUvIndex = 6;
    public const int DefaultMaxRainPct = 60;

    public static WeatherThresholds Resolve(
        IReadOnlyList<WeatherSignal> signals,
        int? explicitRainPct,
        double? explicitFeelsLikeC,
        int? explicitUvIndex,
        int? storedUvWarn,
        int? storedFeelsWarn)
    {
        return new WeatherThresholds(
            MaxRainPct: signals.Contains(WeatherSignal.Rain)
                ? explicitRainPct ?? DefaultMaxRainPct
                : null,
            MaxFeelsLikeC: signals.Contains(WeatherSignal.Heat)
                ? explicitFeelsLikeC ?? Stored(storedFeelsWarn, DefaultMaxFeelsLikeC)
                : null,
            MaxUvIndex: signals.Contains(WeatherSignal.Sun)
                ? explicitUvIndex ?? Stored(storedUvWarn, DefaultMaxUvIndex)
                : null);
    }

    // null (or a missing UserSettings row) => the built-in default; 0 => off; N => N.
    private static double? Stored(int? stored, double dflt) => stored switch
    {
        null => dflt,
        0 => null,
        _ => stored.Value,
    };
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherWindowThresholdsTests"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums/WeatherSignal.cs \
        backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowThresholds.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowThresholdsTests.cs
git commit -m "feat(weather): resolve Weather window thresholds from the User's setting (#153)"
```

---

### Task 2: Band membership and the per-hour verdict (pure)

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherHourJudge.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/WeatherHourJudgeTests.cs`

**Interfaces:**
- Consumes: `WeatherThresholds(double? MaxRainPct, double? MaxFeelsLikeC, double? MaxUvIndex)` and `enum WeatherSignal { Rain, Heat, Sun }` from Task 1; `HourlyReading(DateTime DisplayLocal, bool IsDaytime, double? TempC, double? FeelsLikeC, string? ConditionType, string? IconBaseUri, int? RainPct, int? UvIndex)` from `MenuNest.Application.Abstractions`.
- Produces: `static bool WeatherHourJudge.InBand(int hour, int fromHour, int toHour)`; `static DateOnly WeatherHourJudge.BandDate(DateTime displayLocal, int fromHour, int toHour)`; `static WeatherSignal? WeatherHourJudge.BlockedBy(HourlyReading h, WeatherThresholds t)` returning the first signal that rejects the hour, and `null` when the hour is good; `static bool WeatherHourJudge.IsJudgeable(HourlyReading h, WeatherThresholds t)`.

**The three-way verdict.** An hour is exactly one of: **good** (`IsJudgeable` true and `BlockedBy` null), **blocked** (`BlockedBy` non-null), or **unjudgeable** (`IsJudgeable` false — a selected signal's value is missing from the bucket). An unjudgeable hour is not good, so it breaks a run, but it was not rejected by any signal, so it must not be counted as blocked (spec §7).

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
    private static HourlyReading H(int day, int hour, int? rain = 10, double? feels = 30, int? uv = 2)
        => new(new DateTime(2026, 9, day, hour, 0, 0), true, 28, feels, "CLEAR", null, rain, uv);

    [Fact]
    public void A_normal_band_is_inclusive_of_from_and_exclusive_of_to()
    {
        WeatherHourJudge.InBand(7, 8, 16).Should().BeFalse();
        WeatherHourJudge.InBand(8, 8, 16).Should().BeTrue();
        WeatherHourJudge.InBand(15, 8, 16).Should().BeTrue();
        WeatherHourJudge.InBand(16, 8, 16).Should().BeFalse();
    }

    [Fact]
    public void A_full_day_band_admits_every_hour()
    {
        for (var h = 0; h < 24; h++) WeatherHourJudge.InBand(h, 0, 24).Should().BeTrue();
    }

    [Fact]
    public void A_wrapping_band_admits_both_sides_of_midnight()
    {
        WeatherHourJudge.InBand(17, 18, 2).Should().BeFalse();
        WeatherHourJudge.InBand(18, 18, 2).Should().BeTrue();
        WeatherHourJudge.InBand(23, 18, 2).Should().BeTrue();
        WeatherHourJudge.InBand(0, 18, 2).Should().BeTrue();
        WeatherHourJudge.InBand(1, 18, 2).Should().BeTrue();
        WeatherHourJudge.InBand(2, 18, 2).Should().BeFalse();
    }

    [Fact]
    public void A_wrapped_tail_hour_belongs_to_the_date_the_band_opened_on()
    {
        WeatherHourJudge.BandDate(new DateTime(2026, 9, 22, 20, 0, 0), 18, 2)
            .Should().Be(new DateOnly(2026, 9, 22));
        WeatherHourJudge.BandDate(new DateTime(2026, 9, 23, 1, 0, 0), 18, 2)
            .Should().Be(new DateOnly(2026, 9, 22));
    }

    [Fact]
    public void A_non_wrapping_band_never_moves_the_date()
    {
        WeatherHourJudge.BandDate(new DateTime(2026, 9, 23, 1, 0, 0), 8, 16)
            .Should().Be(new DateOnly(2026, 9, 23));
    }

    [Fact]
    public void An_hour_inside_every_selected_threshold_is_good()
    {
        var t = new WeatherThresholds(60, 40, 6);
        WeatherHourJudge.BlockedBy(H(22, 8, rain: 30, feels: 33, uv: 4), t).Should().BeNull();
    }

    [Fact]
    public void A_value_exactly_at_the_threshold_blocks_the_hour()
    {
        var t = new WeatherThresholds(60, 40, 6);
        WeatherHourJudge.BlockedBy(H(22, 8, rain: 60), t).Should().Be(WeatherSignal.Rain);
        WeatherHourJudge.BlockedBy(H(22, 8, feels: 40), t).Should().Be(WeatherSignal.Heat);
        WeatherHourJudge.BlockedBy(H(22, 8, uv: 6), t).Should().Be(WeatherSignal.Sun);
    }

    [Fact]
    public void A_null_threshold_means_that_signal_never_blocks()
    {
        var rainOnly = new WeatherThresholds(60, null, null);
        WeatherHourJudge.BlockedBy(H(22, 13, rain: 10, feels: 44, uv: 11), rainOnly).Should().BeNull();
    }

    [Fact]
    public void Rain_wins_the_tie_when_several_signals_reject_the_same_hour()
    {
        var t = new WeatherThresholds(60, 40, 6);
        WeatherHourJudge.BlockedBy(H(22, 13, rain: 90, feels: 44, uv: 11), t).Should().Be(WeatherSignal.Rain);
    }

    [Fact]
    public void An_hour_missing_a_selected_signals_value_is_unjudgeable_not_blocked()
    {
        var t = new WeatherThresholds(60, 40, 6);
        var h = H(22, 8, rain: null);

        WeatherHourJudge.IsJudgeable(h, t).Should().BeFalse();
        WeatherHourJudge.BlockedBy(h, t).Should().BeNull();
    }

    [Fact]
    public void A_missing_value_for_an_unselected_signal_is_still_judgeable()
    {
        var rainOnly = new WeatherThresholds(60, null, null);
        WeatherHourJudge.IsJudgeable(H(22, 8, feels: null, uv: null), rainOnly).Should().BeTrue();
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

/// <summary>Pure per-hour rules for a Weather window (menunest-220..222): does this forecast hour
/// fall inside the caller's hours-of-day band, which calendar date does the band attribute it to,
/// and does every selected signal accept it.</summary>
public static class WeatherHourJudge
{
    /// <summary>fromHour inclusive, toHour exclusive. When toHour &lt;= fromHour the band wraps past
    /// midnight (e.g. 18-02 for a night market) and both sides are admitted.</summary>
    public static bool InBand(int hour, int fromHour, int toHour)
        => toHour > fromHour
            ? hour >= fromHour && hour < toHour
            : hour >= fromHour || hour < toHour;

    /// <summary>The date the band that contains this hour opened on. For a wrapping band the tail
    /// past midnight belongs to the PREVIOUS date, so "คืนวันที่ 22" stays one window on 22 Sep.
    /// menunest-222; this is a caller preference, never a claim about daylight (menunest-117).</summary>
    public static DateOnly BandDate(DateTime displayLocal, int fromHour, int toHour)
        => toHour <= fromHour && displayLocal.Hour < toHour
            ? DateOnly.FromDateTime(displayLocal.AddDays(-1))
            : DateOnly.FromDateTime(displayLocal);

    /// <summary>True when every SELECTED signal has a value on this bucket. A selected signal with a
    /// missing value makes the hour unjudgeable: it is not good (so it breaks a run), but no signal
    /// rejected it, so it is not blocked either.</summary>
    public static bool IsJudgeable(HourlyReading h, WeatherThresholds t)
        => (t.MaxRainPct is null || h.RainPct is not null)
        && (t.MaxFeelsLikeC is null || h.FeelsLikeC is not null)
        && (t.MaxUvIndex is null || h.UvIndex is not null);

    /// <summary>The first selected signal that rejects this hour, in WeatherSignal declaration order
    /// (Rain, Heat, Sun) — the documented tie-break. Null when the hour is good OR unjudgeable;
    /// callers separate those two with <see cref="IsJudgeable"/>. A value AT the threshold is
    /// rejected, matching the web app's weatherAlertBadges (>= threshold warns).</summary>
    public static WeatherSignal? BlockedBy(HourlyReading h, WeatherThresholds t)
    {
        if (!IsJudgeable(h, t)) return null;
        if (t.MaxRainPct is { } r && h.RainPct >= r) return WeatherSignal.Rain;
        if (t.MaxFeelsLikeC is { } f && h.FeelsLikeC >= f) return WeatherSignal.Heat;
        if (t.MaxUvIndex is { } u && h.UvIndex >= u) return WeatherSignal.Sun;
        return null;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherHourJudgeTests"`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherHourJudge.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/WeatherHourJudgeTests.cs
git commit -m "feat(weather): band membership and the per-hour Weather window verdict (#153)"
```

---

### Task 3: Cut the hourly series into Weather windows (pure)

The heart of the feature. Given the hourly series and the resolved thresholds, produce the ordered **Weather window** list — or, when it is empty, the miss record that names the blocking signal (menunest-225).

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/WeatherWindowMissReason.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowSelection.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowSelectionTests.cs`

**Interfaces:**
- Consumes: `WeatherThresholds`, `WeatherSignal` (Task 1); `WeatherHourJudge.InBand`, `.BandDate`, `.BlockedBy`, `.IsJudgeable` (Task 2); `HourlyReading` from `MenuNest.Application.Abstractions`.
- Produces:
  - `enum WeatherWindowMissReason { NoWeatherData, AllHoursBlocked, NoWindowLongEnough }`
  - `sealed record WeatherWindow(DateOnly Date, DateTime StartLocal, DateTime EndLocalExclusive, int Hours, int? WorstRainPct, double? WorstFeelsLikeC, int? WorstUvIndex)`
  - `sealed record WeatherWindowMiss(WeatherWindowMissReason Reason, WeatherSignal? BlockingSignal, double? ClosestValue, double? Threshold, int HoursExamined, int HoursBlocked, int LongestRunHours)`
  - `sealed record WeatherWindowCut(IReadOnlyList<WeatherWindow> Windows, WeatherWindowMiss? Miss)`
  - `static WeatherWindowCut WeatherWindowSelection.Cut(IReadOnlyList<HourlyReading> hours, WeatherThresholds thresholds, DateOnly? fromDate, DateOnly? toDate, int fromHour, int toHour, int minWindowHours)`

**Invariant:** `Miss` is null exactly when `Windows` is non-empty. Never both, never neither.

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
    private static readonly WeatherThresholds RainOnly = new(60, null, null);

    // One bucket on 22 Sep 2026 at the given hour.
    private static HourlyReading H(int hour, int? rain = 10, double? feels = 30, int? uv = 2, int day = 22)
        => new(new DateTime(2026, 9, day, hour, 0, 0), true, 28, feels, "CLEAR", null, rain, uv);

    private static WeatherWindowCut Cut(
        IReadOnlyList<HourlyReading> hours, WeatherThresholds? t = null,
        int fromHour = 0, int toHour = 24, int min = 2,
        DateOnly? from = null, DateOnly? to = null)
        => WeatherWindowSelection.Cut(hours, t ?? RainOnly, from, to, fromHour, toHour, min);

    [Fact]
    public void Consecutive_good_hours_become_one_window()
    {
        var cut = Cut(new[] { H(6), H(7), H(8), H(9) });

        cut.Miss.Should().BeNull();
        cut.Windows.Should().HaveCount(1);
        cut.Windows[0].StartLocal.Should().Be(new DateTime(2026, 9, 22, 6, 0, 0));
        cut.Windows[0].EndLocalExclusive.Should().Be(new DateTime(2026, 9, 22, 10, 0, 0));
        cut.Windows[0].Hours.Should().Be(4);
        cut.Windows[0].Date.Should().Be(new DateOnly(2026, 9, 22));
    }

    [Fact]
    public void One_blocked_hour_splits_a_run_in_two()
    {
        // 06 07 08 [09 blocked] 10 11 -> 06-09 and 10-12
        var cut = Cut(new[] { H(6), H(7), H(8), H(9, rain: 62), H(10), H(11) });

        cut.Windows.Should().HaveCount(2);
        cut.Windows[0].Hours.Should().Be(3);
        cut.Windows[1].StartLocal.Hour.Should().Be(10);
        cut.Windows[1].Hours.Should().Be(2);
    }

    [Fact]
    public void A_run_shorter_than_minWindowHours_is_dropped()
    {
        // The 10-11 fragment is 2h; with min 3 only the 3h run survives.
        var cut = Cut(new[] { H(6), H(7), H(8), H(9, rain: 62), H(10), H(11) }, min: 3);

        cut.Windows.Should().HaveCount(1);
        cut.Windows[0].Hours.Should().Be(3);
    }

    [Fact]
    public void A_gap_in_the_buckets_breaks_a_run()
    {
        // 06 07 then nothing at 08, then 09 10 -> two runs, not one.
        var cut = Cut(new[] { H(6), H(7), H(9), H(10) });

        cut.Windows.Should().HaveCount(2);
    }

    [Fact]
    public void Hours_outside_the_band_are_never_examined()
    {
        var cut = Cut(new[] { H(3), H(4), H(5), H(8), H(9) }, fromHour: 8, toHour: 16);

        cut.Windows.Should().HaveCount(1);
        cut.Windows[0].StartLocal.Hour.Should().Be(8);
    }

    [Fact]
    public void A_wrapping_band_yields_one_window_across_midnight_dated_to_the_opening_day()
    {
        var hours = new[] { H(22, day: 22), H(23, day: 22), H(0, day: 23), H(1, day: 23) };

        var cut = Cut(hours, fromHour: 18, toHour: 2);

        cut.Windows.Should().HaveCount(1);
        cut.Windows[0].Hours.Should().Be(4);
        cut.Windows[0].Date.Should().Be(new DateOnly(2026, 9, 22));
        cut.Windows[0].EndLocalExclusive.Should().Be(new DateTime(2026, 9, 23, 2, 0, 0));
    }

    [Fact]
    public void Hours_outside_the_date_range_are_never_examined()
    {
        var hours = new[] { H(8, day: 22), H(9, day: 22), H(8, day: 23), H(9, day: 23) };

        var cut = Cut(hours, from: new DateOnly(2026, 9, 23), to: new DateOnly(2026, 9, 23));

        cut.Windows.Should().HaveCount(1);
        cut.Windows[0].Date.Should().Be(new DateOnly(2026, 9, 23));
    }

    [Fact]
    public void Worst_values_are_reported_for_selected_signals_only()
    {
        var t = new WeatherThresholds(60, 40, null);
        var hours = new[] { H(6, rain: 10, feels: 31, uv: 3), H(7, rain: 45, feels: 36, uv: 11) };

        var cut = Cut(hours, t);

        cut.Windows[0].WorstRainPct.Should().Be(45);
        cut.Windows[0].WorstFeelsLikeC.Should().Be(36);
        cut.Windows[0].WorstUvIndex.Should().BeNull();
    }

    [Fact]
    public void An_empty_hourly_series_is_NoWeatherData()
    {
        var cut = Cut(Array.Empty<HourlyReading>());

        cut.Windows.Should().BeEmpty();
        cut.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
    }

    [Fact]
    public void A_range_that_matches_no_bucket_is_NoWeatherData()
    {
        var cut = Cut(new[] { H(8), H(9) }, from: new DateOnly(2026, 9, 30), to: new DateOnly(2026, 9, 30));

        cut.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
        cut.Miss.HoursExamined.Should().Be(0);
    }

    [Fact]
    public void Every_hour_blocked_names_the_signal_its_closest_value_and_its_threshold()
    {
        var hours = new[] { H(6, rain: 80), H(7, rain: 65), H(8, rain: 90) };

        var cut = Cut(hours);

        cut.Miss!.Reason.Should().Be(WeatherWindowMissReason.AllHoursBlocked);
        cut.Miss.BlockingSignal.Should().Be(WeatherSignal.Rain);
        cut.Miss.ClosestValue.Should().Be(65);
        cut.Miss.Threshold.Should().Be(60);
        cut.Miss.HoursExamined.Should().Be(3);
        cut.Miss.HoursBlocked.Should().Be(3);
    }

    [Fact]
    public void The_blocking_signal_is_the_one_that_rejected_the_most_hours()
    {
        var t = new WeatherThresholds(60, 40, null);
        // one hour blocked by rain, two by heat
        var hours = new[] { H(6, rain: 80, feels: 30), H(7, rain: 10, feels: 44), H(8, rain: 10, feels: 42) };

        var cut = Cut(hours, t);

        cut.Miss!.BlockingSignal.Should().Be(WeatherSignal.Heat);
        cut.Miss.ClosestValue.Should().Be(42);
        cut.Miss.Threshold.Should().Be(40);
    }

    [Fact]
    public void Good_hours_that_never_reach_minWindowHours_is_NoWindowLongEnough()
    {
        var hours = new[] { H(6), H(7, rain: 80), H(8), H(9, rain: 80), H(10) };

        var cut = Cut(hours, min: 2);

        cut.Windows.Should().BeEmpty();
        cut.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWindowLongEnough);
        cut.Miss.LongestRunHours.Should().Be(1);
        cut.Miss.BlockingSignal.Should().BeNull();
    }

    [Fact]
    public void Every_examined_hour_unjudgeable_is_NoWeatherData_not_AllHoursBlocked()
    {
        var hours = new[] { H(6, rain: null), H(7, rain: null) };

        var cut = Cut(hours);

        cut.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
        cut.Miss.BlockingSignal.Should().BeNull();
        cut.Miss.HoursBlocked.Should().Be(0);
        cut.Miss.HoursExamined.Should().Be(2);
    }

    [Fact]
    public void Windows_come_back_ordered_by_start()
    {
        var hours = new[] { H(10, day: 23), H(11, day: 23), H(6, day: 22), H(7, day: 22) };

        var cut = Cut(hours);

        cut.Windows.Should().HaveCount(2);
        cut.Windows[0].StartLocal.Should().BeBefore(cut.Windows[1].StartLocal);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherWindowSelectionTests"`
Expected: FAIL — build error, `WeatherWindowSelection` does not exist.

- [ ] **Step 3: Write the miss-reason enum**

Create `backend/src/MenuNest.Domain/Enums/WeatherWindowMissReason.cs`:

```csharp
namespace MenuNest.Domain.Enums;

/// <summary>Why a Weather window search came back empty (menunest-225). These are deliberately
/// distinct: "Google gave us nothing" and "every hour was too wet" must never read the same to the
/// assistant, and "some hours were fine but none lasted long enough" is a third, actionable answer.</summary>
public enum WeatherWindowMissReason
{
    /// <summary>No forecast hours to judge — a provider failure, beyond the Forecast horizon, or no
    /// bucket fell inside the requested range and band (ADR-030, ADR-031). Also covers the case
    /// where every examined hour was missing a selected signal's value.</summary>
    NoWeatherData,

    /// <summary>Hours were examined and every one was rejected by a selected signal.</summary>
    AllHoursBlocked,

    /// <summary>Some hours passed, but no run of them reached MinWindowHours (menunest-224).</summary>
    NoWindowLongEnough,
}
```

- [ ] **Step 4: Write the selection**

Create `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowSelection.cs`:

```csharp
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>One Weather window: a dated, contiguous run of forecast hours where every selected
/// signal stayed inside its threshold. EndLocalExclusive is the hour AFTER the last good one, so
/// Hours == (End - Start).TotalHours. Worst* is populated only for SELECTED signals — CONTEXT.md
/// says unselected signals are ignored entirely, never merely reported (menunest-220/221).</summary>
public sealed record WeatherWindow(
    DateOnly Date, DateTime StartLocal, DateTime EndLocalExclusive, int Hours,
    int? WorstRainPct, double? WorstFeelsLikeC, int? WorstUvIndex);

/// <summary>Why the search came back empty, and what to relax (menunest-225).</summary>
public sealed record WeatherWindowMiss(
    WeatherWindowMissReason Reason, WeatherSignal? BlockingSignal,
    double? ClosestValue, double? Threshold,
    int HoursExamined, int HoursBlocked, int LongestRunHours);

/// <summary>Windows, or the miss that explains their absence. Exactly one side is populated.</summary>
public sealed record WeatherWindowCut(IReadOnlyList<WeatherWindow> Windows, WeatherWindowMiss? Miss);

/// <summary>Pure: cut an Hourly forecast into Weather windows. No I/O, exactly like
/// WeatherHourSelection.CoolestHour, so the whole rule is unit-testable without a fake
/// IWeatherService.</summary>
public static class WeatherWindowSelection
{
    public static WeatherWindowCut Cut(
        IReadOnlyList<HourlyReading> hours,
        WeatherThresholds thresholds,
        DateOnly? fromDate,
        DateOnly? toDate,
        int fromHour,
        int toHour,
        int minWindowHours)
    {
        var examined = hours
            .Where(h => WeatherHourJudge.InBand(h.DisplayLocal.Hour, fromHour, toHour))
            .Select(h => new
            {
                Reading = h,
                Date = WeatherHourJudge.BandDate(h.DisplayLocal, fromHour, toHour),
            })
            .Where(x => (fromDate is null || x.Date >= fromDate) && (toDate is null || x.Date <= toDate))
            .OrderBy(x => x.Reading.DisplayLocal)
            .ToList();

        if (examined.Count == 0)
            return Empty(WeatherWindowMissReason.NoWeatherData, null, null, null, 0, 0, 0);

        // Walk the examined hours once, cutting runs of good hours. A bucket gap, a blocked hour or
        // an unjudgeable hour all end the current run.
        var runs = new List<List<HourlyReading>>();
        var current = new List<HourlyReading>();
        var blockedCount = new Dictionary<WeatherSignal, int>();
        var closest = new Dictionary<WeatherSignal, double>();
        DateTime? previous = null;

        foreach (var x in examined)
        {
            var h = x.Reading;
            Observe(h, thresholds, closest);

            var blocked = WeatherHourJudge.BlockedBy(h, thresholds);
            var good = blocked is null && WeatherHourJudge.IsJudgeable(h, thresholds);
            if (blocked is { } b) blockedCount[b] = blockedCount.GetValueOrDefault(b) + 1;

            var contiguous = previous is { } p && h.DisplayLocal == p.AddHours(1);
            if (!good || !contiguous)
            {
                if (current.Count > 0) { runs.Add(current); current = new List<HourlyReading>(); }
            }
            if (good) current.Add(h);
            previous = h.DisplayLocal;
        }
        if (current.Count > 0) runs.Add(current);

        var kept = runs.Where(r => r.Count >= minWindowHours)
            .Select(r => ToWindow(r, thresholds, fromHour, toHour))
            .OrderBy(w => w.StartLocal)
            .ToList();

        if (kept.Count > 0) return new WeatherWindowCut(kept, null);

        var hoursBlocked = blockedCount.Values.Sum();
        var longestRun = runs.Count == 0 ? 0 : runs.Max(r => r.Count);

        // Some hours passed but none lasted long enough — no signal is to blame (menunest-224).
        if (longestRun > 0)
            return Empty(WeatherWindowMissReason.NoWindowLongEnough, null, null, null,
                examined.Count, hoursBlocked, longestRun);

        // Nothing passed AND nothing was rejected => every examined hour was unjudgeable. There is no
        // blocking signal to name, and inventing one with a zero count would invent a cause.
        if (hoursBlocked == 0)
            return Empty(WeatherWindowMissReason.NoWeatherData, null, null, null, examined.Count, 0, 0);

        // Most-rejecting signal wins; WeatherSignal declaration order breaks the tie.
        var worst = blockedCount
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => (int)kv.Key)
            .First().Key;

        return Empty(WeatherWindowMissReason.AllHoursBlocked, worst,
            closest.TryGetValue(worst, out var c) ? c : null,
            ThresholdOf(worst, thresholds), examined.Count, hoursBlocked, 0);
    }

    // The LOWEST over-threshold value each selected signal reached — i.e. the smallest threshold that
    // would unblock at least one hour for it. Deliberately NOT the minimum across all examined hours:
    // an hour that already passes this signal (and was rejected by a different one) says nothing about
    // how far to relax THIS number, and quoting it would send the assistant to a threshold that
    // changes nothing. Computed per signal, independently of BlockedBy's first-match short-circuit.
    private static void Observe(HourlyReading h, WeatherThresholds t, Dictionary<WeatherSignal, double> closest)
    {
        if (t.MaxRainPct is { } r && h.RainPct is { } rv && rv >= r) Keep(closest, WeatherSignal.Rain, rv);
        if (t.MaxFeelsLikeC is { } f && h.FeelsLikeC is { } fv && fv >= f) Keep(closest, WeatherSignal.Heat, fv);
        if (t.MaxUvIndex is { } u && h.UvIndex is { } uv && uv >= u) Keep(closest, WeatherSignal.Sun, uv);
    }

    private static void Keep(Dictionary<WeatherSignal, double> closest, WeatherSignal s, double v)
    {
        if (!closest.TryGetValue(s, out var best) || v < best) closest[s] = v;
    }

    private static double? ThresholdOf(WeatherSignal s, WeatherThresholds t) => s switch
    {
        WeatherSignal.Rain => t.MaxRainPct,
        WeatherSignal.Heat => t.MaxFeelsLikeC,
        WeatherSignal.Sun => t.MaxUvIndex,
        _ => null,
    };

    private static WeatherWindow ToWindow(
        List<HourlyReading> run, WeatherThresholds t, int fromHour, int toHour)
    {
        var start = run[0].DisplayLocal;
        var end = run[^1].DisplayLocal.AddHours(1);
        return new WeatherWindow(
            Date: WeatherHourJudge.BandDate(start, fromHour, toHour),
            StartLocal: start,
            EndLocalExclusive: end,
            Hours: run.Count,
            WorstRainPct: t.MaxRainPct is null ? null : run.Max(h => h.RainPct),
            WorstFeelsLikeC: t.MaxFeelsLikeC is null ? null : run.Max(h => h.FeelsLikeC),
            WorstUvIndex: t.MaxUvIndex is null ? null : run.Max(h => h.UvIndex));
    }

    private static WeatherWindowCut Empty(
        WeatherWindowMissReason reason, WeatherSignal? signal, double? closest, double? threshold,
        int examined, int blocked, int longestRun)
        => new(Array.Empty<WeatherWindow>(),
            new WeatherWindowMiss(reason, signal, closest, threshold, examined, blocked, longestRun));
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~WeatherWindowSelectionTests"`
Expected: PASS, 15 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums/WeatherWindowMissReason.cs \
        backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/WeatherWindowSelection.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/WeatherWindowSelectionTests.cs
git commit -m "feat(weather): cut the Hourly forecast into Weather windows (#153)"
```

---

### Task 4: The query, its DTOs and its validator

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsValidator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` (append at end of file, after `RetimeTargetDto`)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsValidatorTests.cs`

**Interfaces:**
- Consumes: `WeatherSignal` (Task 1).
- Produces: `sealed record FindWeatherWindowsQuery(double Lat, double Lng, IReadOnlyList<WeatherSignal> Signals, DateOnly? FromDate, DateOnly? ToDate, int? FromHour, int? ToHour, int? MaxRainPct, double? MaxFeelsLikeC, int? MaxUvIndex, int? MinWindowHours) : IQuery<WeatherWindowResultDto>`; the three DTOs `WeatherWindowDto`, `WeatherWindowMissDto`, `WeatherWindowResultDto`; `sealed class FindWeatherWindowsValidator : AbstractValidator<FindWeatherWindowsQuery>`.

Validators are discovered automatically — `AddValidatorsFromAssembly` in `MenuNest.Application/DependencyInjection.cs` scans the assembly, so **no registration step is needed**.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsValidatorTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class FindWeatherWindowsValidatorTests
{
    private static readonly FindWeatherWindowsValidator Sut = new();

    private static FindWeatherWindowsQuery Q(
        IReadOnlyList<WeatherSignal>? signals = null,
        double lat = 19.3, double lng = 98.4,
        DateOnly? from = null, DateOnly? to = null,
        int? fromHour = null, int? toHour = null,
        int? rain = null, double? feels = null, int? uv = null, int? min = null)
        => new(lat, lng, signals ?? new[] { WeatherSignal.Rain },
               from, to, fromHour, toHour, rain, feels, uv, min);

    [Fact]
    public void A_minimal_query_is_valid()
        => Sut.Validate(Q()).IsValid.Should().BeTrue();

    [Fact]
    public void An_empty_signal_list_is_rejected()
        => Sut.Validate(Q(signals: Array.Empty<WeatherSignal>())).IsValid.Should().BeFalse();

    [Fact]
    public void A_threshold_for_an_unselected_signal_is_rejected()
    {
        Sut.Validate(Q(signals: new[] { WeatherSignal.Rain }, uv: 8)).IsValid.Should().BeFalse();
        Sut.Validate(Q(signals: new[] { WeatherSignal.Rain }, feels: 38)).IsValid.Should().BeFalse();
        Sut.Validate(Q(signals: new[] { WeatherSignal.Sun }, rain: 70)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_threshold_for_a_selected_signal_is_accepted()
        => Sut.Validate(Q(signals: new[] { WeatherSignal.Rain, WeatherSignal.Sun }, rain: 70, uv: 8))
              .IsValid.Should().BeTrue();

    [Fact]
    public void Coordinates_outside_their_ranges_are_rejected()
    {
        Sut.Validate(Q(lat: 91)).IsValid.Should().BeFalse();
        Sut.Validate(Q(lng: -181)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void FromDate_after_ToDate_is_rejected()
        => Sut.Validate(Q(from: new DateOnly(2026, 9, 25), to: new DateOnly(2026, 9, 22)))
              .IsValid.Should().BeFalse();

    [Fact]
    public void An_equal_from_and_to_date_is_a_single_day_and_is_valid()
        => Sut.Validate(Q(from: new DateOnly(2026, 9, 22), to: new DateOnly(2026, 9, 22)))
              .IsValid.Should().BeTrue();

    [Fact]
    public void Hour_bounds_outside_their_ranges_are_rejected()
    {
        Sut.Validate(Q(fromHour: -1)).IsValid.Should().BeFalse();
        Sut.Validate(Q(fromHour: 24)).IsValid.Should().BeFalse();
        Sut.Validate(Q(toHour: 0)).IsValid.Should().BeFalse();
        Sut.Validate(Q(toHour: 25)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_wrapping_band_is_allowed()
        => Sut.Validate(Q(fromHour: 18, toHour: 2)).IsValid.Should().BeTrue();

    [Fact]
    public void MinWindowHours_outside_its_range_is_rejected()
    {
        Sut.Validate(Q(min: 0)).IsValid.Should().BeFalse();
        Sut.Validate(Q(min: 25)).IsValid.Should().BeFalse();
        Sut.Validate(Q(min: 1)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_rain_percentage_outside_0_to_100_is_rejected()
    {
        Sut.Validate(Q(rain: -1)).IsValid.Should().BeFalse();
        Sut.Validate(Q(rain: 101)).IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~FindWeatherWindowsValidatorTests"`
Expected: FAIL — build error, `FindWeatherWindowsQuery` does not exist.

- [ ] **Step 3: Append the DTOs**

Append to the **end** of `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`:

```csharp

/// <summary>Wire shape of one Weather window (menunest-218). EndLocalExclusive is the hour AFTER
/// the last good one. Worst* is null for any signal the caller did not select (menunest-220).</summary>
public sealed record WeatherWindowDto(
    DateOnly Date, DateTime StartLocal, DateTime EndLocalExclusive, int Hours,
    int? WorstRainPct, double? WorstFeelsLikeC, int? WorstUvIndex);

/// <summary>Why zero Weather windows came back, and which number to relax (menunest-225).
/// BlockingSignal / ClosestValue / Threshold are set only for AllHoursBlocked;
/// LongestRunHours only for NoWindowLongEnough.</summary>
public sealed record WeatherWindowMissDto(
    WeatherWindowMissReason Reason, WeatherSignal? BlockingSignal,
    double? ClosestValue, double? Threshold,
    int HoursExamined, int HoursBlocked, int LongestRunHours);

/// <summary>Windows plus, when there are none, the miss that explains it. Miss is null exactly when
/// Windows is non-empty. HorizonTruncated says the requested ToDate ran past the Forecast horizon;
/// SearchedFrom/SearchedThrough are the dates actually covered by forecast data.</summary>
public sealed record WeatherWindowResultDto(
    IReadOnlyList<WeatherWindowDto> Windows, WeatherWindowMissDto? Miss,
    bool HorizonTruncated, DateOnly SearchedFrom, DateOnly SearchedThrough);
```

`TripDtos.cs` already has `using MenuNest.Domain.Enums;` in scope via the file's existing usings — if the build reports `WeatherSignal` or `WeatherWindowMissReason` unresolved, add `using MenuNest.Domain.Enums;` to the top of the file.

- [ ] **Step 4: Write the query**

Create `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsQuery.cs`:

```csharp
using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.FindWeatherWindows;

/// <summary>Find the Weather windows at a bare location (issue #153). Signals is required and
/// non-empty: the caller picks which signals judge an hour from the question the User asked
/// (menunest-220). Everything else is optional; the thresholds fall back to the User's
/// Weather-alert threshold (menunest-219) and the search is bounded by an optional date range and
/// hours-of-day band (menunest-222).</summary>
public sealed record FindWeatherWindowsQuery(
    double Lat,
    double Lng,
    IReadOnlyList<WeatherSignal> Signals,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int? FromHour,
    int? ToHour,
    int? MaxRainPct,
    double? MaxFeelsLikeC,
    int? MaxUvIndex,
    int? MinWindowHours) : IQuery<WeatherWindowResultDto>;
```

- [ ] **Step 5: Write the validator**

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

        // An empty list would make every hour good. A caller that wants raw hours already has
        // get_stop_hourly_forecast / POST api/trips/weather/hourly.
        RuleFor(x => x.Signals).NotEmpty()
            .WithMessage("เลือกอย่างน้อยหนึ่งเกณฑ์ (rain / heat / sun)");

        RuleFor(x => x.FromHour!.Value).InclusiveBetween(0, 23).When(x => x.FromHour is not null);
        RuleFor(x => x.ToHour!.Value).InclusiveBetween(1, 24).When(x => x.ToHour is not null);
        RuleFor(x => x.MinWindowHours!.Value).InclusiveBetween(1, 24).When(x => x.MinWindowHours is not null);
        RuleFor(x => x.MaxRainPct!.Value).InclusiveBetween(0, 100).When(x => x.MaxRainPct is not null);

        RuleFor(x => x).Must(x => x.FromDate is null || x.ToDate is null || x.FromDate <= x.ToDate)
            .WithMessage("fromDate ต้องไม่อยู่หลัง toDate");

        // A threshold for a signal that does not judge is a caller mistake, not a no-op: ignoring it
        // silently would hide a misunderstanding of menunest-220.
        RuleFor(x => x).Must(x => x.MaxRainPct is null || x.Signals.Contains(WeatherSignal.Rain))
            .WithMessage("maxRainPct ใช้ได้เมื่อเลือกเกณฑ์ rain เท่านั้น");
        RuleFor(x => x).Must(x => x.MaxFeelsLikeC is null || x.Signals.Contains(WeatherSignal.Heat))
            .WithMessage("maxFeelsLikeC ใช้ได้เมื่อเลือกเกณฑ์ heat เท่านั้น");
        RuleFor(x => x).Must(x => x.MaxUvIndex is null || x.Signals.Contains(WeatherSignal.Sun))
            .WithMessage("maxUvIndex ใช้ได้เมื่อเลือกเกณฑ์ sun เท่านั้น");
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~FindWeatherWindowsValidatorTests"`
Expected: PASS, 11 tests.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsQuery.cs \
        backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsValidator.cs \
        backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsValidatorTests.cs
git commit -m "feat(weather): FindWeatherWindows query, DTOs and validation (#153)"
```

---

### Task 5: The handler

Orchestrates: size the fetch, read `UserSettings`, resolve thresholds, cut, project, report truncation.

**Why the fetch is over-sized.** `GetHourlyAsync` takes a *count of hours from now*, but `FromDate`/`ToDate` are **local dates at the point**, and the handler does not know the point's timezone. A local date can sit at most one day either side of the UTC date, so the handler asks for `(daysAhead + 2) * 24` hours, clamped to `[24, 240]`. That always covers the requested range, over-fetches by at most 48 h, and — being a multiple of 24 — keeps `GetHourlyAsync`'s `hours`-keyed cache from fragmenting (spec §9).

**Why truncation is measured, not calculated.** `HorizonTruncated` is set by comparing the requested `ToDate` against the last date the provider actually returned. That is exact, where arithmetic against a guessed offset is not.

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsHandlerTests.cs`

**Interfaces:**
- Consumes: `FindWeatherWindowsQuery`, `FindWeatherWindowsValidator`, `WeatherWindowResultDto`, `WeatherWindowDto`, `WeatherWindowMissDto` (Task 4); `WeatherWindowThresholds.Resolve` (Task 1); `WeatherWindowSelection.Cut` (Task 3); `IWeatherService.GetHourlyAsync(WeatherPoint, int, CancellationToken)`, `WeatherPoint(string StopId, double Lat, double Lng, DateTime? ArrivalLocal)`; `IApplicationDbContext.UserSettings`; `IUserProvisioner.GetOrProvisionCurrentAsync(CancellationToken)`.
- Produces: `sealed class FindWeatherWindowsHandler : IQueryHandler<FindWeatherWindowsQuery, WeatherWindowResultDto>` with constructor `(IWeatherService weather, IApplicationDbContext db, IUserProvisioner users, IValidator<FindWeatherWindowsQuery> validator)`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsHandlerTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
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

    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Mock<IUserProvisioner> _users;
    private readonly StubWeather _weather = new();

    // Dates are relative to today so the suite cannot rot: an absolute 2026 date would make the
    // fetch-sizing and truncation assertions start failing once the wall clock passes it.
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

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

    /// Writes the User's Weather-alert threshold. Not called by the "missing row" tests — that is
    /// the whole point of them: the row is created lazily on first write, so most Users have none.
    private void SeedThresholds(int? uv, int? feels)
    {
        var s = UserSettings.Create(_user.Id);
        s.SetWeatherAlerts(uv, feels);
        _db.UserSettings.Add(s);
        _db.SaveChanges();
    }

    private FindWeatherWindowsHandler Handler()
        => new(_weather, _db, _users.Object, new FindWeatherWindowsValidator());

    private static HourlyReading H(int dayOffset, int hour, int? rain = 10, double? feels = 30, int? uv = 2)
        => new(Today.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, 0)), true, 28, feels, "CLEAR", null, rain, uv);

    private static FindWeatherWindowsQuery Q(
        IReadOnlyList<WeatherSignal>? signals = null,
        DateOnly? from = null, DateOnly? to = null,
        int? fromHour = null, int? toHour = null,
        int? rain = null, double? feels = null, int? uv = null, int? min = null)
        => new(19.3, 98.4, signals ?? new[] { WeatherSignal.Rain },
               from, to, fromHour, toHour, rain, feels, uv, min);

    [Fact]
    public async Task Returns_windows_and_no_miss_when_hours_pass()
    {
        _weather.Hours = new[] { H(0, 6), H(0, 7), H(0, 8) };

        var result = await Handler().Handle(Q(), CancellationToken.None);

        result.Miss.Should().BeNull();
        result.Windows.Should().HaveCount(1);
        result.Windows[0].Hours.Should().Be(3);
    }

    [Fact]
    public async Task Uses_the_built_in_default_when_the_UserSettings_row_is_missing()
    {
        // No SeedThresholds call: this User has NO UserSettings row at all, which is the normal
        // state for anyone who never opened /settings. 39 C is under the built-in 40, so it passes.
        _weather.Hours = new[] { H(0, 6, feels: 39), H(0, 7, feels: 39) };

        var result = await Handler().Handle(Q(signals: new[] { WeatherSignal.Heat }), CancellationToken.None);

        result.Windows.Should().HaveCount(1);
    }

    [Fact]
    public async Task Honours_the_stored_Weather_alert_threshold()
    {
        SeedThresholds(uv: null, feels: 38);
        _weather.Hours = new[] { H(0, 6, feels: 39), H(0, 7, feels: 39) };

        var result = await Handler().Handle(Q(signals: new[] { WeatherSignal.Heat }), CancellationToken.None);

        result.Windows.Should().BeEmpty();
        result.Miss!.Reason.Should().Be(WeatherWindowMissReason.AllHoursBlocked);
        result.Miss.Threshold.Should().Be(38);
        result.Miss.ClosestValue.Should().Be(39);
    }

    [Fact]
    public async Task A_stored_zero_turns_the_signal_off_so_nothing_blocks()
    {
        SeedThresholds(uv: null, feels: 0);
        _weather.Hours = new[] { H(0, 6, feels: 44), H(0, 7, feels: 45) };

        var result = await Handler().Handle(Q(signals: new[] { WeatherSignal.Heat }), CancellationToken.None);

        result.Windows.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_explicit_override_beats_the_stored_threshold()
    {
        SeedThresholds(uv: null, feels: 38);
        _weather.Hours = new[] { H(0, 6, feels: 39), H(0, 7, feels: 39) };

        var result = await Handler().Handle(
            Q(signals: new[] { WeatherSignal.Heat }, feels: 41), CancellationToken.None);

        result.Windows.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_empty_provider_result_is_NoWeatherData()
    {
        _weather.Hours = Array.Empty<HourlyReading>();

        var result = await Handler().Handle(Q(), CancellationToken.None);

        result.Windows.Should().BeEmpty();
        result.Miss!.Reason.Should().Be(WeatherWindowMissReason.NoWeatherData);
    }

    [Fact]
    public async Task Reports_the_dates_actually_covered_and_flags_truncation()
    {
        _weather.Hours = new[] { H(0, 6), H(0, 7) };

        var result = await Handler().Handle(Q(to: Today.AddDays(9)), CancellationToken.None);

        result.SearchedFrom.Should().Be(Today);
        result.SearchedThrough.Should().Be(Today);
        result.HorizonTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task Does_not_flag_truncation_when_the_range_is_covered()
    {
        _weather.Hours = new[] { H(0, 6), H(0, 7) };

        var result = await Handler().Handle(Q(to: Today), CancellationToken.None);

        result.HorizonTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task Requests_a_whole_number_of_days_and_never_more_than_the_horizon()
    {
        _weather.Hours = new[] { H(0, 6), H(0, 7) };

        await Handler().Handle(Q(to: Today.AddDays(9)), CancellationToken.None);

        _weather.RequestedHours.Should().Be(240);
    }

    [Fact]
    public async Task Requests_only_the_days_a_short_range_needs_rounded_to_whole_days()
    {
        _weather.Hours = new[] { H(0, 6), H(0, 7) };

        await Handler().Handle(Q(to: Today.AddDays(1)), CancellationToken.None);

        // (1 day ahead + 2 days of timezone padding) * 24 — the point's UTC offset is unknown.
        _weather.RequestedHours.Should().Be(72);
        (_weather.RequestedHours % 24).Should().Be(0);
    }

    [Fact]
    public async Task Rejects_an_empty_signal_list()
    {
        var act = () => Handler().Handle(Q(signals: Array.Empty<WeatherSignal>()), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
```

**If any call above does not compile**, check the real signature in the repo and fix the *test*, never the production code. The three that matter: `User.CreateFromExternalLogin(externalId, email, displayName, AuthProvider)`, `UserSettings.Create(Guid userId)` (it throws `DomainException` on `Guid.Empty`) and `SetWeatherAlerts(int? uv, int? feels)`. The SQLite setup above is copied from `Trips/AttachChecklistItemRelationalTests.cs`.

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

/// <summary>Find the Weather windows at a bare location (issue #153). Reads the User's
/// Weather-alert threshold — the first backend use of it (menunest-219) — reuses the existing
/// forecast/hours walk (no new billing SKU, menunest-119), and persists nothing (ADR-033).</summary>
public sealed class FindWeatherWindowsHandler
    : IQueryHandler<FindWeatherWindowsQuery, WeatherWindowResultDto>
{
    private const int HorizonHours = 240;
    private const int DefaultFromHour = 0;
    private const int DefaultToHour = 24;
    private const int DefaultMinWindowHours = 2;

    private readonly IWeatherService _weather;
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<FindWeatherWindowsQuery> _validator;

    public FindWeatherWindowsHandler(
        IWeatherService weather,
        IApplicationDbContext db,
        IUserProvisioner users,
        IValidator<FindWeatherWindowsQuery> validator)
    { _weather = weather; _db = db; _users = users; _validator = validator; }

    public async ValueTask<WeatherWindowResultDto> Handle(FindWeatherWindowsQuery q, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(q, ct);

        var user = await _users.GetOrProvisionCurrentAsync(ct);
        // The row is created lazily on first write, so a User who never opened /settings has NONE.
        // A missing row must behave exactly like null — the built-in defaults (menunest-219).
        var settings = await _db.UserSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == user.Id, ct);

        var thresholds = WeatherWindowThresholds.Resolve(
            q.Signals, q.MaxRainPct, q.MaxFeelsLikeC, q.MaxUvIndex,
            settings?.UvWarnThreshold, settings?.FeelsLikeWarnThreshold);

        var hours = await _weather.GetHourlyAsync(
            new WeatherPoint("", q.Lat, q.Lng, null), HoursToFetch(q.ToDate), ct);

        var fromHour = q.FromHour ?? DefaultFromHour;
        var toHour = q.ToHour ?? DefaultToHour;
        var cut = WeatherWindowSelection.Cut(
            hours, thresholds, q.FromDate, q.ToDate, fromHour, toHour,
            q.MinWindowHours ?? DefaultMinWindowHours);

        // Measured, not calculated: compare what was asked for against what the provider actually
        // returned, so truncation is exact rather than derived from a guessed UTC offset.
        var firstDate = hours.Count == 0 ? null : (DateOnly?)DateOnly.FromDateTime(hours[0].DisplayLocal);
        var lastDate = hours.Count == 0 ? null : (DateOnly?)DateOnly.FromDateTime(hours[^1].DisplayLocal);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new WeatherWindowResultDto(
            Windows: cut.Windows.Select(w => new WeatherWindowDto(
                w.Date, w.StartLocal, w.EndLocalExclusive, w.Hours,
                w.WorstRainPct, w.WorstFeelsLikeC, w.WorstUvIndex)).ToList(),
            Miss: cut.Miss is null ? null : new WeatherWindowMissDto(
                cut.Miss.Reason, cut.Miss.BlockingSignal, cut.Miss.ClosestValue, cut.Miss.Threshold,
                cut.Miss.HoursExamined, cut.Miss.HoursBlocked, cut.Miss.LongestRunHours),
            HorizonTruncated: q.ToDate is { } wanted && lastDate is { } got && got < wanted,
            SearchedFrom: q.FromDate is { } f && firstDate is { } fd && f > fd ? f : firstDate ?? today,
            SearchedThrough: lastDate ?? firstDate ?? today);
    }

    /// <summary>ToDate is a LOCAL date at the point and the point's offset is unknown, so pad by a
    /// day on each side. A multiple of 24 also keeps GetHourlyAsync's hours-keyed cache from
    /// fragmenting across callers asking slightly different ranges.</summary>
    private static int HoursToFetch(DateOnly? toDate)
    {
        if (toDate is not { } to) return HorizonHours;
        var daysAhead = to.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        var wanted = (daysAhead + 2) * 24;
        return Math.Clamp(wanted, 24, HorizonHours);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~FindWeatherWindowsHandlerTests"`
Expected: PASS, 11 tests.

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test backend --configuration Release`
Expected: PASS — the pre-commit hook runs this, so a red suite here blocks the commit.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/FindWeatherWindows/FindWeatherWindowsHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/FindWeatherWindowsHandlerTests.cs
git commit -m "feat(weather): FindWeatherWindows handler reads the User's threshold (#153)"
```

---

### Task 6: The HTTP endpoint

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` — add a `using` beside the other `MenuNest.Application.UseCases.Trips.*` imports, and an action directly after the existing `HourlyWeather` action
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Controllers/TripsControllerWeatherWindowsTests.cs`

**Interfaces:**
- Consumes: `FindWeatherWindowsQuery`, `WeatherWindowResultDto` (Task 4).
- Produces: `POST api/trips/weather/windows`, body-bound to `FindWeatherWindowsQuery`, returning `WeatherWindowResultDto`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Controllers/TripsControllerWeatherWindowsTests.cs`:

```csharp
using System.Reflection;
using FluentAssertions;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Controllers;

public class TripsControllerWeatherWindowsTests
{
    private static MethodInfo Action =>
        typeof(TripsController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.GetCustomAttribute<HttpPostAttribute>()?.Template == "api/trips/weather/windows");

    [Fact]
    public void Is_mapped_to_the_documented_route()
        => Action.Should().NotBeNull();

    [Fact]
    public void Binds_the_query_from_the_request_body()
    {
        var p = Action.GetParameters().First();
        p.ParameterType.Should().Be<FindWeatherWindowsQuery>();
        p.GetCustomAttribute<FromBodyAttribute>().Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter "FullyQualifiedName~TripsControllerWeatherWindowsTests"`
Expected: FAIL — `Single()` throws because no action carries that route template.

- [ ] **Step 3: Add the using**

In `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`, add this line to the `using` block, keeping the existing alphabetical order (directly after the `...DeleteTripPlace;` line and before `...GetHourlyForecast;`):

```csharp
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
```

- [ ] **Step 4: Add the action**

In the same file, directly **after** the existing `HourlyWeather` action and **before** the `Retime` action, insert:

```csharp
    [HttpPost("api/trips/weather/windows")]
    public async Task<ActionResult<WeatherWindowResultDto>> WeatherWindows([FromBody] FindWeatherWindowsQuery q, CancellationToken ct)
        => Ok(await _mediator.Send(q, ct));
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.WebApi.UnitTests --filter "FullyQualifiedName~TripsControllerWeatherWindowsTests"`
Expected: PASS, 2 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
        backend/tests/MenuNest.WebApi.UnitTests/Controllers/TripsControllerWeatherWindowsTests.cs
git commit -m "feat(weather): POST api/trips/weather/windows (#153)"
```

---

### Task 7: The MCP tool

The tool `Description` is the **only** place an MCP client learns to map "วันไหนแดดไม่ร้อน" onto `[heat, sun]` (menunest-220). A silent edit to it is a silent behaviour change, so a test pins the parts that carry meaning — the same mechanism `BudgetToolsTests` already uses.

**Files:**
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` — add a `using` beside the other `MenuNest.Application.UseCases.Trips.*` imports, and a tool method directly after `get_stop_hourly_forecast`
- Test: `backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsWeatherWindowsTests.cs`

**Interfaces:**
- Consumes: `FindWeatherWindowsQuery`, `WeatherWindowResultDto` (Task 4); `WeatherSignal` (Task 1).
- Produces: MCP tool `find_weather_windows`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsWeatherWindowsTests.cs`:

```csharp
using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
using MenuNest.Domain.Enums;
using MenuNest.McpServer.Tools;
using Moq;
using Xunit;

namespace MenuNest.McpServer.UnitTests.Tools;

public class TripToolsWeatherWindowsTests
{
    private readonly Mock<IMediator> _mediator = new();

    private static MethodInfo Method =>
        typeof(TripTools).GetMethod("find_weather_windows")!;

    private static string Description =>
        Method.GetCustomAttribute<DescriptionAttribute>()!.Description;

    [Fact]
    public async Task Forwards_every_argument_into_the_query()
    {
        var expected = new WeatherWindowResultDto(
            Array.Empty<WeatherWindowDto>(), null, false,
            new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 22));
        FindWeatherWindowsQuery? seen = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<FindWeatherWindowsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<FindWeatherWindowsQuery, CancellationToken>((q, _) => seen = q)
            .Returns(ValueTask.FromResult(expected));

        var sut = new TripTools(_mediator.Object);
        var signals = new[] { WeatherSignal.Heat, WeatherSignal.Sun };

        var result = await sut.find_weather_windows(
            19.3, 98.4, signals,
            new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 28),
            8, 16, null, 41, 8, 3, CancellationToken.None);

        result.Should().BeSameAs(expected);
        seen!.Lat.Should().Be(19.3);
        seen.Lng.Should().Be(98.4);
        seen.Signals.Should().BeEquivalentTo(signals);
        seen.FromDate.Should().Be(new DateOnly(2026, 9, 27));
        seen.ToDate.Should().Be(new DateOnly(2026, 9, 28));
        seen.FromHour.Should().Be(8);
        seen.ToHour.Should().Be(16);
        seen.MaxRainPct.Should().BeNull();
        seen.MaxFeelsLikeC.Should().Be(41);
        seen.MaxUvIndex.Should().Be(8);
        seen.MinWindowHours.Should().Be(3);
    }

    // The description is the only place an MCP client learns which signals a Thai question maps to.
    // Pin the load-bearing parts so an edit that drops them fails here, not silently in production.
    [Theory]
    [InlineData("rain")]
    [InlineData("heat")]
    [InlineData("sun")]
    [InlineData("ฝนไม่ตก")]
    [InlineData("แดดไม่ร้อน")]
    [InlineData("resolve_place")]
    public void Description_carries_the_signal_mapping_guidance(string fragment)
        => Description.Should().Contain(fragment);

    [Fact]
    public void Every_parameter_is_described_for_the_client()
        => Method.GetParameters()
            .Where(p => p.ParameterType != typeof(CancellationToken))
            .Should().OnlyContain(p => p.GetCustomAttribute<DescriptionAttribute>() != null);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests --filter "FullyQualifiedName~TripToolsWeatherWindowsTests"`
Expected: FAIL — build error, `find_weather_windows` does not exist on `TripTools`.

- [ ] **Step 3: Add the using**

In `backend/src/MenuNest.McpServer/Tools/TripTools.cs`, add to the `using` block beside the other `MenuNest.Application.UseCases.Trips.*` imports:

```csharp
using MenuNest.Application.UseCases.Trips.FindWeatherWindows;
```

- [ ] **Step 4: Add the tool**

In the same file, directly **after** the `get_stop_hourly_forecast` method and **before** `retime_stop_to_weather`, insert:

```csharp
    [McpServerTool, Description("Find the good-weather windows at ANY location — use this to answer 'ที่นี่วันไหนฝนไม่ตก' / 'วันไหนแดดไม่ร้อน' before planning a trip. Resolve the place with resolve_place first to get lat/lng; this tool does NOT need a trip or a stop. Pick `signals` from what the user actually asked: 'ฝนไม่ตก' -> [rain]; 'แดดไม่ร้อน' -> [heat, sun]; 'ร้อนไหม' -> [heat]; 'แดดแรงไหม' -> [sun]; 'อากาศดี' -> all three. Send ONLY the signals asked for — a rain filter would wrongly drop the cool overcast hours that answer a 'แดดไม่ร้อน' question. Thresholds default to the user's own weather-alert settings (feels-like 40C, UV 6) and rain to 60%; pass one only to relax it, and only for a signal you selected. Bound the search with fromDate/toDate (a trip weekend) and fromHour/toHour (e.g. 8-16 for a day out, 18-2 for a night market — it may wrap past midnight). Returns windows ordered by start; when none pass, `miss` names the blocking signal and its closest value so you can offer to relax that number.")]
    public async Task<WeatherWindowResultDto> find_weather_windows(
        [Description("Latitude of the place (from resolve_place)")] double lat,
        [Description("Longitude of the place (from resolve_place)")] double lng,
        [Description("Which signals judge an hour: rain (chance of rain), heat (feels-like), sun (UV index). At least one; send only what was asked.")] WeatherSignal[] signals,
        [Description("First local date to search (inclusive). Omit to start at the earliest forecast hour.")] DateOnly? fromDate,
        [Description("Last local date to search (inclusive). Omit for the whole 10-day forecast horizon; a later date is cut to the horizon and horizonTruncated is set.")] DateOnly? toDate,
        [Description("First hour of each day to consider, 0-23 inclusive (default 0)")] int? fromHour,
        [Description("End hour of each day, 1-24 exclusive (default 24). If <= fromHour the band wraps past midnight, e.g. 18-2.")] int? toHour,
        [Description("Rain % at or above which an hour is rejected (default 60). Only with the rain signal.")] int? maxRainPct,
        [Description("Feels-like °C at or above which an hour is rejected (default: the user's setting, else 40). Only with the heat signal.")] double? maxFeelsLikeC,
        [Description("UV index at or above which an hour is rejected (default: the user's setting, else 6). Only with the sun signal.")] int? maxUvIndex,
        [Description("Shortest run of good hours worth reporting (default 2). Set 1 to see every passing hour.")] int? minWindowHours,
        CancellationToken ct)
        => await mediator.Send(new FindWeatherWindowsQuery(
            lat, lng, signals, fromDate, toDate, fromHour, toHour,
            maxRainPct, maxFeelsLikeC, maxUvIndex, minWindowHours), ct);
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.McpServer.UnitTests --filter "FullyQualifiedName~TripToolsWeatherWindowsTests"`
Expected: PASS, 8 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test backend --configuration Release`
Expected: PASS, everything green.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.McpServer.UnitTests/Tools/TripToolsWeatherWindowsTests.cs
git commit -m "feat(weather): find_weather_windows MCP tool (closes #153)"
```

---

## Verification after the last task

The spec's §11 names a risk this plan cannot test away: **the assistant may pick the wrong signals**. Before calling the feature done, exercise it end to end through the real MCP surface, not only through unit tests:

1. Ask the assistant, in Thai: "ปาย อาทิตย์หน้า วันไหนฝนไม่ตกบ้าง". Confirm it calls `resolve_place` and then `find_weather_windows` with `signals: [rain]` only.
2. Ask: "แล้ววันไหนแดดไม่ร้อน". Confirm `signals: [heat, sun]` and **no** rain filter.
3. In the rainy season, confirm an empty result comes back with `miss.blockingSignal` and `miss.closestValue` populated, and that the assistant offers to relax that number.

`CLAUDE.md` is explicit that the automated gates are blind to this class of problem. There is no UI here, so there is no mock to diff and no Playwright spec to add — the MCP conversation **is** the surface, and it is the thing to check by hand.
