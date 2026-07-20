using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Persistence;

public sealed class OAuthEntityPersistenceTests
{
    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task OAuthClient_and_RefreshToken_round_trip()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);

        db.OAuthClients.Add(new OAuthClient
        {
            ClientId = "cid1", ClientName = "claude", RedirectUrisJson = "[\"https://x/cb\"]",
            Scope = "openid", ExpiresAt = DateTime.UtcNow.AddDays(365),
        });
        db.OAuthRefreshTokens.Add(new OAuthRefreshToken
        {
            RefreshCode = "rc1", EntraRefreshToken = "entra-rt", Subject = "oid-1",
            ExpiresAt = DateTime.UtcNow.AddDays(365), CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var client = await db.OAuthClients.FindAsync("cid1");
        var rt = await db.OAuthRefreshTokens.FindAsync("rc1");
        Assert.NotNull(client);
        Assert.Equal("claude", client!.ClientName);
        Assert.NotNull(rt);
        Assert.Equal("entra-rt", rt!.EntraRefreshToken);
    }
}
