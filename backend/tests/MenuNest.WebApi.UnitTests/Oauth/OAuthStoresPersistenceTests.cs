using MenuNest.Application.UnitTests.Support;
using MenuNest.WebApi.Oauth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class OAuthStoresPersistenceTests
{
    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Client_registration_survives_a_new_dbcontext()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        string clientId;
        using (var db = NewDb(conn))
            clientId = await new ClientStore(db).RegisterAsync("claude", new[] {"https://x/cb"}, "openid");

        using var db2 = NewDb(conn); // simulate an App Service restart: fresh context, same store
        var reg = await new ClientStore(db2).GetAsync(clientId);
        Assert.NotNull(reg);
        Assert.Contains("https://x/cb", reg!.RedirectUris);
    }

    [Fact]
    public async Task Refresh_code_is_single_use_and_survives_restart()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

        string code;
        using (var db = NewDb(conn))
            code = await new TokenStore(cache, db).SaveRefreshAsync("entra-rt", "oid-1");

        // survives a "restart" (fresh context) and returns the stored RT once
        using (var db2 = NewDb(conn))
        {
            var rt = await new TokenStore(cache, db2).TakeRefreshAsync(code);
            Assert.Equal("entra-rt", rt);
        }
        // second use of the same code fails (single-use rotation)
        using (var db3 = NewDb(conn))
        {
            var again = await new TokenStore(cache, db3).TakeRefreshAsync(code);
            Assert.Null(again);
        }
    }
}
