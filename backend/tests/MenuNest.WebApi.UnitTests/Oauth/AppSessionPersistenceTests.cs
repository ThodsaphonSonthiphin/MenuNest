using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class AppSessionPersistenceTests
{
    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task An_app_session_row_survives_a_new_dbcontext()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        using (var db = NewDb(conn))
        {
            db.AppSessions.Add(new AppSession
            {
                RefreshCode = "code-1",
                Subject = "oid-1",
                ExpiresAt = DateTime.UtcNow.AddDays(365),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var db2 = NewDb(conn);
        var row = await db2.AppSessions.SingleAsync(s => s.RefreshCode == "code-1");
        row.Subject.Should().Be("oid-1");
        row.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(364));
    }

    [Fact]
    public void The_inmemory_context_can_build_its_model_with_app_sessions()
    {
        // InMemoryAppDbContext hand-rolls OnModelCreating, so a non-Id key must be
        // declared explicitly there or model validation throws on first access.
        using var db = new InMemoryAppDbContext(
            new DbContextOptionsBuilder<InMemoryAppDbContext>()
                .UseInMemoryDatabase($"appsession-{Guid.NewGuid()}").Options);

        db.Invoking(d => d.AppSessions.Any()).Should().NotThrow();
    }
}
