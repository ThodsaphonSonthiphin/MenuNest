using FluentAssertions;
using MenuNest.WebApi;
using Xunit;

public class BuildVersionTests
{
    [Fact]
    public void Parse_splits_version_and_commit_on_plus()
    {
        var v = BuildVersion.Parse("0.1.0+a1b2c3d", "2026-07-21T04:12:00Z");
        v.Version.Should().Be("0.1.0+a1b2c3d");
        v.Commit.Should().Be("a1b2c3d");
        v.BuildTime.Should().Be("2026-07-21T04:12:00Z");
    }

    [Fact]
    public void Parse_no_plus_yields_local_commit()
    {
        var v = BuildVersion.Parse("0.1.0", null);
        v.Version.Should().Be("0.1.0");
        v.Commit.Should().Be("local");
        v.BuildTime.Should().BeNull();
    }

    [Fact]
    public void Parse_null_or_empty_informational_defaults_to_zero()
    {
        BuildVersion.Parse(null, "").Version.Should().Be("0.0.0");
        BuildVersion.Parse("", null).Commit.Should().Be("local");
        BuildVersion.Parse(null, "").BuildTime.Should().BeNull();
    }
}
