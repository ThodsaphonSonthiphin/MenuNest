using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Persistence;

public sealed class WritingEntryConfigurationTests
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
    public async Task Round_trips_a_writing_entry_through_sqlite()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewContext(conn);

        var user = User.CreateFromExternalLogin(
            externalId: "wp-test-oid",
            email: "wp@example.com",
            displayName: "WP Test",
            authProvider: AuthProvider.Microsoft);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = WritingEntry.Create(
            user.Id,
            new DateOnly(2026, 8, 16),
            "<p>my daughter play with her toy</p>",
            elapsedSeconds: 420);
        db.WritingEntries.Add(entry);
        await db.SaveChangesAsync();

        var reloaded = await db.WritingEntries.SingleAsync(w => w.Id == entry.Id);
        reloaded.UserId.Should().Be(user.Id);
        reloaded.Date.Should().Be(new DateOnly(2026, 8, 16));
        reloaded.ElapsedSeconds.Should().Be(420);
        reloaded.CorrectedAt.Should().BeNull();
    }

    [Fact]
    public async Task Soft_deleted_entry_keeps_its_row_and_records_DeletedAt()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewContext(conn);

        var user = User.CreateFromExternalLogin(
            externalId: "wp-test-oid-2",
            email: "wp2@example.com",
            displayName: "WP Test 2",
            authProvider: AuthProvider.Microsoft);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = WritingEntry.Create(
            user.Id, new DateOnly(2026, 8, 16), "<p>a night to soft delete</p>", 420);
        db.WritingEntries.Add(entry);
        await db.SaveChangesAsync();

        entry.SoftDelete();
        await db.SaveChangesAsync();

        var reloaded = await db.WritingEntries.SingleAsync(w => w.Id == entry.Id);
        reloaded.DeletedAt.Should().NotBeNull();
    }
}
