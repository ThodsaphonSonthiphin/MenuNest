using System.Data.Common;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips;
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
