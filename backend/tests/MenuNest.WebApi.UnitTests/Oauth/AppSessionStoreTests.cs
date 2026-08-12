using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.WebApi.Oauth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class AppSessionStoreTests
{
    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task An_issued_session_survives_a_restart_and_returns_its_subject()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        string code;
        using (var db = NewDb(conn))
            code = await new AppSessionStore(db).IssueAsync("oid-1");

        using var db2 = NewDb(conn); // fresh context = App Service restart
        (await new AppSessionStore(db2).TakeAsync(code)).Should().Be("oid-1");
    }

    [Fact]
    public async Task A_refresh_code_is_single_use()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var store = new AppSessionStore(db);

        var code = await store.IssueAsync("oid-1");
        (await store.TakeAsync(code)).Should().Be("oid-1");
        (await store.TakeAsync(code)).Should().BeNull("the row is consumed on first use");
    }

    [Fact]
    public async Task An_expired_session_is_refused_and_removed()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);

        db.AppSessions.Add(new AppSession
        {
            RefreshCode = "stale",
            Subject = "oid-1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-400),
        });
        await db.SaveChangesAsync();

        (await new AppSessionStore(db).TakeAsync("stale")).Should().BeNull();
        (await db.AppSessions.AnyAsync(s => s.RefreshCode == "stale")).Should().BeFalse();
    }

    [Fact]
    public async Task Revoking_one_device_leaves_the_other_signed_in()
    {
        // ADR-159: logout revokes only the device that pressed it.
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var store = new AppSessionStore(db);

        var phone = await store.IssueAsync("oid-1");
        var laptop = await store.IssueAsync("oid-1");

        (await store.RevokeAsync(laptop)).Should().BeTrue();
        (await store.TakeAsync(phone)).Should().Be("oid-1", "the other device is untouched");
    }

    [Fact]
    public async Task Issuing_reclaims_only_this_subjects_expired_rows()
    {
        // Nothing else reclaims AppSessions rows, so issue-time is the sweep.
        // It must stay surgical: a live row for the same subject is another
        // signed-in device (ADR-159), and another subject's rows are not ours.
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);

        db.AppSessions.AddRange(
            new AppSession
            {
                RefreshCode = "mine-expired",
                Subject = "oid-1",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow.AddDays(-400),
            },
            new AppSession
            {
                RefreshCode = "mine-live",
                Subject = "oid-1",
                ExpiresAt = DateTime.UtcNow.AddDays(10),
                CreatedAt = DateTime.UtcNow,
            },
            new AppSession
            {
                RefreshCode = "theirs-expired",
                Subject = "oid-2",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow.AddDays(-400),
            });
        await db.SaveChangesAsync();

        var fresh = await new AppSessionStore(db).IssueAsync("oid-1");

        var remaining = await db.AppSessions.Select(s => s.RefreshCode).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { "mine-live", "theirs-expired", fresh });
    }

    [Fact]
    public async Task Revoking_an_unknown_code_is_a_no_op()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);

        (await new AppSessionStore(db).RevokeAsync("never-issued")).Should().BeFalse();
    }
}
