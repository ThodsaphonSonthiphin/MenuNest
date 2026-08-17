using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Me.UpdateUserSettings;
using MenuNest.Application.UseCases.Writing.GetActiveTargetRule;
using MenuNest.Application.UseCases.Writing.SetActiveTargetRule;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Writing;

public class ActiveTargetRuleHandlerTests
{
    private static SetActiveTargetRuleHandler SetHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new SetActiveTargetRuleValidator());

    private static GetActiveTargetRuleHandler GetHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    [Fact]
    public async Task Get_returns_null_when_no_settings_row_exists_at_all()
    {
        using var fx = new HandlerTestFixture();

        var rule = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);

        rule.Should().BeNull();
    }

    [Fact]
    public async Task Set_creates_the_settings_row_lazily_then_get_reads_it_back()
    {
        using var fx = new HandlerTestFixture();

        var written = await SetHandler(fx).Handle(
            new SetActiveTargetRuleCommand("third-person singular -s"), CancellationToken.None);
        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);

        written.Should().Be("third-person singular -s");
        read.Should().Be("third-person singular -s");
    }

    [Fact]
    public async Task Set_overwrites_a_previous_rule()
    {
        using var fx = new HandlerTestFixture();
        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("articles (a/an/the)"), CancellationToken.None);

        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("past simple -ed"), CancellationToken.None);

        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);
        read.Should().Be("past simple -ed");
    }

    [Fact]
    public async Task Set_with_blank_clears_the_rule()
    {
        using var fx = new HandlerTestFixture();
        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("plural -s"), CancellationToken.None);

        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("   "), CancellationToken.None);

        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);
        read.Should().BeNull();
    }

    [Fact]
    public async Task Set_rejects_a_rule_over_200_characters()
    {
        using var fx = new HandlerTestFixture();

        var act = async () => await SetHandler(fx).Handle(
            new SetActiveTargetRuleCommand(new string('x', 201)), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Set_does_not_clear_the_home_path_or_weather_thresholds()
    {
        using var fx = new HandlerTestFixture();
        var settingsHandler = new UpdateUserSettingsHandler(
            fx.Db, fx.UserProvisioner.Object, new UpdateUserSettingsValidator());
        await settingsHandler.Handle(new UpdateUserSettingsCommand("/writing", 8, 41), CancellationToken.None);

        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("plural -s"), CancellationToken.None);

        var settings = fx.Db.UserSettings.Single(s => s.UserId == fx.User.Id);
        settings.HomePath.Should().Be("/writing");
        settings.UvWarnThreshold.Should().Be(8);
        settings.FeelsLikeWarnThreshold.Should().Be(41);
        settings.ActiveTargetRule.Should().Be("plural -s");
    }

    [Fact]
    public async Task A_settings_save_does_not_clear_an_existing_rule()
    {
        // UpdateUserSettings is a full-snapshot PUT (ADR-091). The rule is
        // deliberately NOT part of that snapshot, so saving Home/weather from
        // the settings screen must leave the rule alone.
        using var fx = new HandlerTestFixture();
        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("plural -s"), CancellationToken.None);
        var settingsHandler = new UpdateUserSettingsHandler(
            fx.Db, fx.UserProvisioner.Object, new UpdateUserSettingsValidator());

        await settingsHandler.Handle(new UpdateUserSettingsCommand("/budget", 6, 40), CancellationToken.None);

        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);
        read.Should().Be("plural -s");
    }

    [Fact]
    public async Task Get_is_scoped_to_the_calling_user()
    {
        using var fx = new HandlerTestFixture();
        var otherSettings = UserSettings.Create(Guid.NewGuid());
        otherSettings.SetActiveTargetRule("someone elses rule");
        fx.Db.UserSettings.Add(otherSettings);
        await fx.Db.SaveChangesAsync();

        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);

        read.Should().BeNull();
    }
}
