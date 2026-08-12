using System.Text.Json;
using FluentAssertions;
using MenuNest.WebApi.Oauth;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Oauth;

public sealed class AppSessionEndpointContractTests
{
    [Fact]
    public void The_refresh_request_binds_the_snake_case_field_the_spa_sends()
    {
        var body = JsonSerializer.Deserialize<AppSessionEndpoints.RefreshRequest>(
            """{"refresh_token":"abc123"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        body!.refresh_token.Should().Be("abc123");
    }

    [Fact]
    public void The_token_response_serialises_the_fields_the_spa_reads()
    {
        var json = JsonSerializer.Serialize(
            new AppSessionTokens("access-jwt", 3600, "refresh-code"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("accessToken").GetString().Should().Be("access-jwt");
        doc.RootElement.GetProperty("expiresIn").GetInt32().Should().Be(3600);
        doc.RootElement.GetProperty("refreshToken").GetString().Should().Be("refresh-code");
    }
}
