using System.Security.Claims;
using FluentAssertions;
using MenuNest.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using Moq;

namespace MenuNest.Infrastructure.IntegrationTests.Authentication;

public class CurrentUserServiceTests
{
    private const string Oid = "00000000-0000-0000-0000-000000000abc";

    [Fact]
    public void IsAuthenticated_is_false_when_no_HttpContext()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var sut = new CurrentUserService(accessor.Object);

        sut.IsAuthenticated.Should().BeFalse();
        sut.ExternalId.Should().BeNull();
    }

    [Fact]
    public void IsAuthenticated_is_false_for_anonymous_principal()
    {
        var context = BuildHttpContextWithPrincipal(new ClaimsPrincipal(new ClaimsIdentity()));

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(context);

        var sut = new CurrentUserService(accessor.Object);

        sut.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Reads_oid_from_long_claim_uri()
    {
        var principal = BuildPrincipal(
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", Oid),
            ("name", "Somsak Lee"),
            ("email", "somsak@example.com"));

        var sut = BuildSut(principal);

        sut.IsAuthenticated.Should().BeTrue();
        sut.ExternalId.Should().Be(Oid);
        sut.Email.Should().Be("somsak@example.com");
        sut.DisplayName.Should().Be("Somsak Lee");
    }

    [Fact]
    public void Reads_oid_from_short_claim_name()
    {
        var principal = BuildPrincipal(("oid", Oid));

        var sut = BuildSut(principal);

        sut.ExternalId.Should().Be(Oid);
    }

    [Fact]
    public void Email_falls_back_to_preferred_username()
    {
        var principal = BuildPrincipal(
            ("oid", Oid),
            ("preferred_username", "somsak@contoso.onmicrosoft.com"));

        var sut = BuildSut(principal);

        sut.Email.Should().Be("somsak@contoso.onmicrosoft.com");
    }

    [Fact]
    public void RequireExternalId_throws_when_anonymous()
    {
        var sut = BuildSut(new ClaimsPrincipal(new ClaimsIdentity()));

        var act = () => sut.RequireExternalId();

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void RequireExternalId_returns_value_when_authenticated()
    {
        var principal = BuildPrincipal(("oid", Oid));

        var sut = BuildSut(principal);

        sut.RequireExternalId().Should().Be(Oid);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static CurrentUserService BuildSut(ClaimsPrincipal principal)
    {
        var context = BuildHttpContextWithPrincipal(principal);
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(context);
        return new CurrentUserService(accessor.Object);
    }

    private static ClaimsPrincipal BuildPrincipal(params (string type, string value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.type, c.value)),
            authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static HttpContext BuildHttpContextWithPrincipal(ClaimsPrincipal principal) =>
        new DefaultHttpContext { User = principal };
}
