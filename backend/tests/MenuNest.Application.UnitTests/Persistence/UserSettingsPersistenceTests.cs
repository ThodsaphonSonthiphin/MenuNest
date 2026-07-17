using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Persistence;

public sealed class UserSettingsPersistenceTests
{
    private static SqliteAppDbContext NewContext(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new SqliteAppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task HomePath_round_trips_and_UserId_is_unique()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);

        var user = User.CreateFromExternalLogin("ext-1", "a@b.com", "A", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var settings = UserSettings.Create(user.Id);
        settings.SetHomePath("/pomodoro");
        ctx.UserSettings.Add(settings);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.UserSettings.SingleAsync(s => s.UserId == user.Id);
        loaded.HomePath.Should().Be("/pomodoro");

        // Second row for the same user must violate the unique index. Clear the
        // change tracker first: with the first UserSettings still tracked, EF's
        // required-1:1-relationship fixup would sever/cascade-delete it in memory
        // (an UPDATE-like "replace") instead of ever reaching the DB, masking the
        // real unique-constraint violation this test means to exercise.
        ctx.ChangeTracker.Clear();
        ctx.UserSettings.Add(UserSettings.Create(user.Id));
        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public void Create_rejects_an_empty_UserId()
    {
        var act = () => UserSettings.Create(Guid.Empty);
        act.Should().Throw<MenuNest.Domain.Exceptions.DomainException>();
    }
}
