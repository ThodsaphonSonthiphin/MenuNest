using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Episodes.GetActiveEpisodes;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Health.Episodes;

public class GetActiveEpisodesHandlerTests
{
    [Fact]
    public async Task Returns_only_episodes_with_null_ended_at_for_current_user()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");

        var active1 = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5,
            startedAt: DateTime.UtcNow.AddMinutes(-10));
        var active2 = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 7,
            startedAt: DateTime.UtcNow.AddMinutes(-30));
        var resolved = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6,
            startedAt: DateTime.UtcNow.AddHours(-3));
        resolved.Resolve(severityAfter: 0);

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.AddRange(active1, active2, resolved);
        await fx.Db.SaveChangesAsync();

        var sut = new GetActiveEpisodesHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new GetActiveEpisodesQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().NotContain(e => e.Id == resolved.Id);
        // Ordered StartedAt DESC: most-recent first.
        result[0].Id.Should().Be(active1.Id);
        result[1].Id.Should().Be(active2.Id);
    }

    [Fact]
    public async Task Does_not_leak_other_users_episodes()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var otherUserId = Guid.NewGuid();
        var mine = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        var theirs = SymptomEpisode.Start(otherUserId, symptom.Id, severity: 5);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.AddRange(mine, theirs);
        await fx.Db.SaveChangesAsync();

        var sut = new GetActiveEpisodesHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new GetActiveEpisodesQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result.Single().Id.Should().Be(mine.Id);
    }

    [Fact]
    public async Task Populates_symptom_name_intake_count_and_first_drug_name()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ไมเกรน");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6);
        var drug = Drug.Create(fx.User.Id, "Sumatriptan", DrugType.Triptan, "50mg", 4, 4, 2);
        var intake1 = Intake.Create(fx.User.Id, drug.Id, doseAmount: 1,
            symptomEpisodeId: episode.Id,
            takenAt: DateTime.UtcNow.AddMinutes(-10));
        var intake2 = Intake.Create(fx.User.Id, drug.Id, doseAmount: 1,
            symptomEpisodeId: episode.Id,
            takenAt: DateTime.UtcNow.AddMinutes(-5));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.Add(drug);
        fx.Db.Intakes.AddRange(intake1, intake2);
        await fx.Db.SaveChangesAsync();

        var sut = new GetActiveEpisodesHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new GetActiveEpisodesQuery(), CancellationToken.None);

        var dto = result.Single();
        dto.SymptomName.Should().Be("ไมเกรน");
        dto.IntakeCount.Should().Be(2);
        dto.FirstDrugName.Should().Be("Sumatriptan");
    }

    [Fact]
    public async Task Returns_empty_when_no_active_episodes()
    {
        using var fx = new HandlerTestFixture();
        var sut = new GetActiveEpisodesHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(new GetActiveEpisodesQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
