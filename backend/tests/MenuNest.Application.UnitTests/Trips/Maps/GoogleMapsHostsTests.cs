using FluentAssertions;
using MenuNest.Application.Abstractions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class GoogleMapsHostsTests
{
    [Theory]
    // Already allowed before this change — these must not regress.
    [InlineData("maps.app.goo.gl")]
    [InlineData("goo.gl")]
    [InlineData("g.co")]
    [InlineData("google.com")]
    [InlineData("www.google.com")]
    [InlineData("maps.google.com")]
    // R6.2: the ccTLD forms a Thai share sheet actually produces.
    [InlineData("google.co.th")]
    [InlineData("maps.google.co.th")]
    [InlineData("www.google.co.th")]
    [InlineData("google.de")]
    [InlineData("google.com.au")]
    [InlineData("maps.google.co.uk")]
    public void Allows(string host) => GoogleMapsHosts.IsAllowedHost(host).Should().BeTrue();

    [Theory]
    // A widened allowlist widens the SSRF surface, so the look-alikes matter more
    // than the happy path. Each of these must stay rejected.
    [InlineData("evilgoogle.com")]
    [InlineData("google.co.th.evil.com")]
    [InlineData("googlexcom")]
    [InlineData("notgoogle.de")]
    [InlineData("google.evil")]
    [InlineData("localhost")]
    [InlineData("169.254.169.254")]
    public void Rejects(string host) => GoogleMapsHosts.IsAllowedHost(host).Should().BeFalse();

    [Fact]
    public void RejectsNonHttpSchemes() =>
        GoogleMapsHosts.IsAllowedUrl("file:///etc/passwd").Should().BeFalse();

    [Fact]
    public void AllowsACcTldUrlEndToEnd() =>
        GoogleMapsHosts.IsAllowedUrl("https://maps.google.co.th/maps/place/Wat+Phra+Kaew/").Should().BeTrue();
}
