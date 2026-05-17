using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Episodes.UpdateEpisode;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Episodes;

public class UpdateEpisodeHandlerTests
{
    [Fact]
    public async Task Only_supplied_fields_are_updated()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var triggerIds = new[] { Guid.NewGuid() };
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5,
            triggerIds: triggerIds, notes: "Original note");
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new UpdateEpisodeValidator());

        var result = await sut.Handle(
            new UpdateEpisodeCommand(Id: episode.Id, Severity: 8),
            CancellationToken.None);

        result.Severity.Should().Be(8);
        result.Notes.Should().Be("Original note");
        result.TriggerIds.Should().BeEquivalentTo(triggerIds);

        var persisted = fx.Db.SymptomEpisodes.Single(e => e.Id == episode.Id);
        persisted.Severity.Should().Be(8);
        persisted.Notes.Should().Be("Original note");
    }

    [Fact]
    public async Task Updates_notes_period_and_triggers_when_supplied()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var newTriggers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var sut = new UpdateEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new UpdateEpisodeValidator());

        var result = await sut.Handle(
            new UpdateEpisodeCommand(
                Id: episode.Id,
                Notes: "Updated note",
                IsOnPeriod: true,
                TriggerIds: newTriggers),
            CancellationToken.None);

        result.Notes.Should().Be("Updated note");
        result.IsOnPeriod.Should().BeTrue();
        result.TriggerIds.Should().BeEquivalentTo(newTriggers);
    }

    [Fact]
    public async Task Applies_migraine_attributes_when_provided_flag_set()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ไมเกรน");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new UpdateEpisodeValidator());

        var result = await sut.Handle(
            new UpdateEpisodeCommand(
                Id: episode.Id,
                HasAura: true,
                AuraTypes: new[] { AuraType.Visual },
                Location: SymptomLocation.Right,
                Quality: SymptomQuality.Throbbing,
                MigraineAttributesProvided: true),
            CancellationToken.None);

        result.HasAura.Should().BeTrue();
        result.AuraTypes.Should().ContainSingle().Which.Should().Be(AuraType.Visual);
        result.Location.Should().Be(SymptomLocation.Right);
        result.Quality.Should().Be(SymptomQuality.Throbbing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Throws_ValidationException_when_severity_out_of_range(int severity)
    {
        using var fx = new HandlerTestFixture();
        var sut = new UpdateEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new UpdateEpisodeValidator());

        var act = async () => await sut.Handle(
            new UpdateEpisodeCommand(Id: Guid.NewGuid(), Severity: severity),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Throws_when_episode_not_found()
    {
        using var fx = new HandlerTestFixture();
        var sut = new UpdateEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new UpdateEpisodeValidator());

        var act = async () => await sut.Handle(
            new UpdateEpisodeCommand(Id: Guid.NewGuid(), Severity: 5),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }

    [Fact]
    public async Task Throws_when_episode_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var otherUserId = Guid.NewGuid();
        var episode = SymptomEpisode.Start(otherUserId, symptom.Id, severity: 4);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new UpdateEpisodeValidator());

        var act = async () => await sut.Handle(
            new UpdateEpisodeCommand(Id: episode.Id, Severity: 5),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }
}
