using FluentAssertions;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Domain;

public class ReviewLinkTests
{
    [Fact]
    public void Create_keeps_a_valid_https_url_and_trimmed_label()
    {
        var link = ReviewLink.Create("  https://www.tiktok.com/@u/video/1  ", "  @foodie  ");
        link.Url.Should().Be("https://www.tiktok.com/@u/video/1");
        link.Label.Should().Be("@foodie");
    }

    [Fact]
    public void Create_nulls_a_blank_label()
    {
        ReviewLink.Create("https://x.com/v", "   ").Label.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://x.com/v")]
    [InlineData("/relative/path")]
    public void Create_rejects_non_http_urls(string url)
    {
        var act = () => ReviewLink.Create(url, null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_over_length_url()
    {
        var act = () => ReviewLink.Create("https://x.com/" + new string('a', 500), null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_over_length_label()
    {
        var act = () => ReviewLink.Create("https://x.com/v", new string('a', 81));
        act.Should().Throw<DomainException>();
    }
}
