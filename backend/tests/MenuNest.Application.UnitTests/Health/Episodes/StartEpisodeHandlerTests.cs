using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Episodes.StartEpisode;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Health.Episodes;

public class StartEpisodeHandlerTests
{
    [Fact]
    public async Task Creates_episode_with_required_severity_scoped_to_current_user()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        fx.Db.Symptoms.Add(symptom);
        await fx.Db.SaveChangesAsync();

        var sut = new StartEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new StartEpisodeValidator());

        var result = await sut.Handle(
            new StartEpisodeCommand(SymptomId: symptom.Id, Severity: 5),
            CancellationToken.None);

        result.Severity.Should().Be(5);
        result.SymptomId.Should().Be(symptom.Id);
        result.SymptomName.Should().Be("ปวดหัว");
        result.EndedAt.Should().BeNull();
        result.IntakeCount.Should().Be(0);
        result.FirstDrugName.Should().BeNull();

        var persisted = fx.Db.SymptomEpisodes.Single(e => e.Id == result.Id);
        persisted.UserId.Should().Be(fx.User.Id);
    }

    [Fact]
    public async Task Persists_migraine_attributes_when_supplied()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ไมเกรน");
        fx.Db.Symptoms.Add(symptom);
        await fx.Db.SaveChangesAsync();

        var sut = new StartEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new StartEpisodeValidator());

        var triggerIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var auraTypes = new[] { AuraType.Visual, AuraType.Sensory };
        var associated = new[] { AssociatedSymptom.Nausea, AssociatedSymptom.Photophobia };

        var result = await sut.Handle(
            new StartEpisodeCommand(
                SymptomId: symptom.Id,
                Severity: 7,
                IsOnPeriod: true,
                TriggerIds: triggerIds,
                Notes: "Stressful day",
                HasAura: true,
                AuraTypes: auraTypes,
                AuraDurationMin: 20,
                Location: SymptomLocation.Right,
                Quality: SymptomQuality.Throbbing,
                AssociatedSymptoms: associated,
                WorsenedByActivity: true,
                FunctionalImpact: FunctionalImpact.Moderate),
            CancellationToken.None);

        var persisted = fx.Db.SymptomEpisodes.Single(e => e.Id == result.Id);
        persisted.IsOnPeriod.Should().BeTrue();
        persisted.HasAura.Should().BeTrue();
        persisted.AuraDurationMin.Should().Be(20);
        persisted.Location.Should().Be(SymptomLocation.Right);
        persisted.Quality.Should().Be(SymptomQuality.Throbbing);
        persisted.WorsenedByActivity.Should().BeTrue();
        persisted.FunctionalImpact.Should().Be(FunctionalImpact.Moderate);
        persisted.AuraTypes.Should().BeEquivalentTo(auraTypes);
        persisted.AssociatedSymptoms.Should().BeEquivalentTo(associated);
        persisted.TriggerIds.Should().BeEquivalentTo(triggerIds);
        persisted.Notes.Should().Be("Stressful day");
    }

    [Fact]
    public async Task Skips_migraine_attribute_call_when_none_supplied()
    {
        // Verifies the handler doesn't clobber defaults when the caller is
        // a non-migraine "Quick Log" path that only supplies severity.
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ไข้");
        fx.Db.Symptoms.Add(symptom);
        await fx.Db.SaveChangesAsync();

        var sut = new StartEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new StartEpisodeValidator());

        var result = await sut.Handle(
            new StartEpisodeCommand(SymptomId: symptom.Id, Severity: 3),
            CancellationToken.None);

        var persisted = fx.Db.SymptomEpisodes.Single(e => e.Id == result.Id);
        persisted.HasAura.Should().BeNull();
        persisted.Location.Should().BeNull();
        persisted.Quality.Should().BeNull();
        persisted.AuraTypes.Should().BeEmpty();
        persisted.AssociatedSymptoms.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task Throws_ValidationException_when_severity_out_of_range(int severity)
    {
        using var fx = new HandlerTestFixture();
        var sut = new StartEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new StartEpisodeValidator());

        var act = async () => await sut.Handle(
            new StartEpisodeCommand(SymptomId: Guid.NewGuid(), Severity: severity),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Throws_ValidationException_when_symptom_id_empty()
    {
        using var fx = new HandlerTestFixture();
        var sut = new StartEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new StartEpisodeValidator());

        var act = async () => await sut.Handle(
            new StartEpisodeCommand(SymptomId: Guid.Empty, Severity: 5),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Uses_provided_started_at_when_set()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดท้อง");
        fx.Db.Symptoms.Add(symptom);
        await fx.Db.SaveChangesAsync();

        var sut = new StartEpisodeHandler(fx.Db, fx.UserProvisioner.Object, new StartEpisodeValidator());
        var backdated = DateTime.UtcNow.AddHours(-3);

        var result = await sut.Handle(
            new StartEpisodeCommand(SymptomId: symptom.Id, Severity: 4, StartedAt: backdated),
            CancellationToken.None);

        result.StartedAt.Should().BeCloseTo(backdated, TimeSpan.FromSeconds(1));
    }
}
