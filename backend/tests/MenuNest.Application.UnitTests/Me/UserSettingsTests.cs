using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Me;

public class UserSettingsTests
{
    [Fact]
    public void ActiveTargetRule_is_null_until_set()
    {
        var settings = UserSettings.Create(Guid.NewGuid());

        settings.ActiveTargetRule.Should().BeNull();
    }

    [Fact]
    public void SetActiveTargetRule_stores_the_trimmed_rule()
    {
        var settings = UserSettings.Create(Guid.NewGuid());

        settings.SetActiveTargetRule("  third-person singular -s  ");

        settings.ActiveTargetRule.Should().Be("third-person singular -s");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetActiveTargetRule_clears_to_null_for_blank_input(string? blank)
    {
        var settings = UserSettings.Create(Guid.NewGuid());
        settings.SetActiveTargetRule("articles (a/an/the)");

        settings.SetActiveTargetRule(blank);

        settings.ActiveTargetRule.Should().BeNull();
    }

    [Fact]
    public void SetActiveTargetRule_accepts_exactly_200_characters()
    {
        var settings = UserSettings.Create(Guid.NewGuid());

        settings.SetActiveTargetRule(new string('x', 200));

        settings.ActiveTargetRule!.Length.Should().Be(200);
    }

    [Fact]
    public void SetActiveTargetRule_rejects_201_characters()
    {
        var settings = UserSettings.Create(Guid.NewGuid());

        var act = () => settings.SetActiveTargetRule(new string('x', 201));

        act.Should().Throw<DomainException>()
            .WithMessage("ActiveTargetRule must be 200 characters or less.");
    }

    [Fact]
    public void SetActiveTargetRule_does_not_disturb_the_other_settings()
    {
        var settings = UserSettings.Create(Guid.NewGuid());
        settings.SetHomePath("/writing");
        settings.SetWeatherAlerts(8, 41);

        settings.SetActiveTargetRule("plural -s");

        settings.HomePath.Should().Be("/writing");
        settings.UvWarnThreshold.Should().Be(8);
        settings.FeelsLikeWarnThreshold.Should().Be(41);
    }
}
