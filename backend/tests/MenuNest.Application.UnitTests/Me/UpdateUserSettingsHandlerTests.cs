using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Me.UpdateUserSettings;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Me;

public sealed class UpdateUserSettingsHandlerTests
{
    private static SqliteAppDbContext NewContext(SqliteConnection conn)
    {
        var ctx = new SqliteAppDbContext(
            new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static UpdateUserSettingsHandler NewHandler(SqliteAppDbContext ctx, User user)
    {
        var provisioner = new Mock<IUserProvisioner>();
        provisioner.Setup(p => p.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return new UpdateUserSettingsHandler(ctx, provisioner.Object, new UpdateUserSettingsValidator());
    }

    [Fact]
    public async Task Creates_the_row_on_first_write_then_updates_it()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        var user = User.CreateFromExternalLogin("ext-1", "a@b.com", "A", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = NewHandler(ctx, user);

        var first = await handler.Handle(new UpdateUserSettingsCommand("/pomodoro"), CancellationToken.None);
        first.HomePath.Should().Be("/pomodoro");
        (await ctx.UserSettings.CountAsync()).Should().Be(1);

        var second = await handler.Handle(new UpdateUserSettingsCommand("/trips"), CancellationToken.None);
        second.HomePath.Should().Be("/trips");
        (await ctx.UserSettings.CountAsync()).Should().Be(1); // still one row (updated, not inserted)
    }

    [Fact]
    public async Task Null_clears_the_HomePath()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        var user = User.CreateFromExternalLogin("ext-2", "b@b.com", "B", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var handler = NewHandler(ctx, user);

        await handler.Handle(new UpdateUserSettingsCommand("/trips"), CancellationToken.None);
        var cleared = await handler.Handle(new UpdateUserSettingsCommand(null), CancellationToken.None);

        cleared.HomePath.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_a_HomePath_longer_than_100_chars()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        var user = User.CreateFromExternalLogin("ext-3", "c@b.com", "C", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var handler = NewHandler(ctx, user);

        var act = async () => await handler.Handle(
            new UpdateUserSettingsCommand(new string('x', 101)), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

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
}
