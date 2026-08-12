using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.WebApi.Oauth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class AppSessionServiceTests
{
    private const string ServerUrl = "https://menunest.azurewebsites.net/mcp";

    private static OAuthJwt Jwt() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "test-signing-key-please-change-in-prod",
            ["MCP:ServerUrl"] = ServerUrl,
        }).Build());

    private static SqliteAppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;
        var db = new SqliteAppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task An_issued_access_token_carries_oid_so_it_maps_to_the_same_user()
    {
        // CurrentUserService resolves ExternalId as objectidentifier ?? oid ?? sub.
        // If this claim were missing the session would provision a DUPLICATE user.
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var sut = new AppSessionService(new AppSessionStore(db), Jwt());

        var tokens = await sut.IssueAsync("oid-123", "Pon", "pon@x.io");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        jwt.Claims.Should().Contain(c => c.Type == "oid" && c.Value == "oid-123");
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == "oid-123");
        jwt.Issuer.Should().Be(ServerUrl);
        tokens.ExpiresIn.Should().Be(3600);
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Refreshing_rotates_the_code_and_keeps_the_subject()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var sut = new AppSessionService(new AppSessionStore(db), Jwt());

        var first = await sut.IssueAsync("oid-123", "Pon", "pon@x.io");
        var second = await sut.RefreshAsync(first.RefreshToken);

        second.Should().NotBeNull();
        second!.RefreshToken.Should().NotBe(first.RefreshToken, "rotation is single-use");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(second.AccessToken);
        jwt.Claims.Should().Contain(c => c.Type == "oid" && c.Value == "oid-123");

        (await sut.RefreshAsync(first.RefreshToken)).Should()
            .BeNull("the old code must not be reusable");
    }

    [Fact]
    public async Task Refreshing_an_unknown_code_returns_null()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var sut = new AppSessionService(new AppSessionStore(db), Jwt());

        (await sut.RefreshAsync("never-issued")).Should().BeNull();
    }
}
