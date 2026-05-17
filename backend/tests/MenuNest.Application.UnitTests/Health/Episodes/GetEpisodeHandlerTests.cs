using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Episodes.GetEpisode;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Episodes;

public class GetEpisodeHandlerTests
{
    [Fact]
    public async Task Returns_detail_with_intakes_followups_and_photos()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ไมเกรน");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 7);
        var drug = Drug.Create(fx.User.Id, "Sumatriptan", DrugType.Triptan, "50mg", 4, 4, 2);
        var intake = Intake.Create(fx.User.Id, drug.Id, doseAmount: 1,
            symptomEpisodeId: episode.Id,
            takenAt: DateTime.UtcNow.AddMinutes(-15));
        var ping = FollowUpPing.Schedule(episode.Id, DateTime.UtcNow.AddMinutes(15));
        var photo = Photo.Create(fx.User.Id, PhotoParentType.SymptomEpisode, episode.Id,
            "https://blob/ep.jpg", "episode-images", 500, "image/jpeg");

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.Add(drug);
        fx.Db.Intakes.Add(intake);
        fx.Db.FollowUpPings.Add(ping);
        fx.Db.Photos.Add(photo);
        await fx.Db.SaveChangesAsync();

        var sut = new GetEpisodeHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new GetEpisodeQuery(episode.Id), CancellationToken.None);

        result.Id.Should().Be(episode.Id);
        result.SymptomName.Should().Be("ไมเกรน");

        result.Intakes.Should().ContainSingle();
        var intakeRow = result.Intakes.Single();
        intakeRow.Id.Should().Be(intake.Id);
        intakeRow.DrugName.Should().Be("Sumatriptan");
        intakeRow.DoseStrength.Should().Be("50mg");
        intakeRow.DoseAmount.Should().Be(1);

        result.FollowUps.Should().ContainSingle();
        result.FollowUps.Single().Status.Should().Be(PingStatus.Pending);

        result.Photos.Should().ContainSingle();
        result.Photos.Single().Url.Should().Be("https://blob/ep.jpg");
    }

    [Fact]
    public async Task Returns_empty_collections_when_no_related_rows()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ไข้");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 4);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var sut = new GetEpisodeHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new GetEpisodeQuery(episode.Id), CancellationToken.None);

        result.Intakes.Should().BeEmpty();
        result.FollowUps.Should().BeEmpty();
        result.Photos.Should().BeEmpty();
    }

    [Fact]
    public async Task Excludes_soft_deleted_photos()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ไมเกรน");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        var alive = Photo.Create(fx.User.Id, PhotoParentType.SymptomEpisode, episode.Id,
            "https://blob/a.jpg", "episode-images", 100, "image/jpeg");
        var gone = Photo.Create(fx.User.Id, PhotoParentType.SymptomEpisode, episode.Id,
            "https://blob/b.jpg", "episode-images", 100, "image/jpeg");
        gone.SoftDelete();

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Photos.AddRange(alive, gone);
        await fx.Db.SaveChangesAsync();

        var sut = new GetEpisodeHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new GetEpisodeQuery(episode.Id), CancellationToken.None);

        result.Photos.Should().ContainSingle();
        result.Photos.Single().Url.Should().Be("https://blob/a.jpg");
    }

    [Fact]
    public async Task Throws_when_not_owned_by_current_user()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var otherUserId = Guid.NewGuid();
        var foreign = SymptomEpisode.Start(otherUserId, symptom.Id, severity: 6);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(foreign);
        await fx.Db.SaveChangesAsync();

        var sut = new GetEpisodeHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new GetEpisodeQuery(foreign.Id), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }

    [Fact]
    public async Task Throws_when_episode_does_not_exist()
    {
        using var fx = new HandlerTestFixture();
        var sut = new GetEpisodeHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new GetEpisodeQuery(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }
}
