using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Episodes.ResolveEpisode;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Episodes;

public class ResolveEpisodeHandlerTests
{
    [Fact]
    public async Task Sets_ended_at_and_severity_after_and_cancels_pending_pings()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 7);
        var pending1 = FollowUpPing.Schedule(episode.Id, DateTime.UtcNow.AddMinutes(5));
        var pending2 = FollowUpPing.Schedule(episode.Id, DateTime.UtcNow.AddMinutes(35));
        var answered = FollowUpPing.Schedule(episode.Id, DateTime.UtcNow.AddMinutes(-30));
        answered.MarkAsked();
        answered.RecordResponse(PingResponse.Improved, severityAtCheck: 5);

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.FollowUpPings.AddRange(pending1, pending2, answered);
        await fx.Db.SaveChangesAsync();

        var sut = new ResolveEpisodeHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new ResolveEpisodeCommand(Id: episode.Id, SeverityAfter: 2),
            CancellationToken.None);

        result.EndedAt.Should().NotBeNull();
        result.SeverityAfter.Should().Be(2);

        var persisted = fx.Db.SymptomEpisodes.Single(e => e.Id == episode.Id);
        persisted.EndedAt.Should().NotBeNull();
        persisted.SeverityAfter.Should().Be(2);

        fx.Db.FollowUpPings.Single(p => p.Id == pending1.Id).Status.Should().Be(PingStatus.Missed);
        fx.Db.FollowUpPings.Single(p => p.Id == pending2.Id).Status.Should().Be(PingStatus.Missed);
        // Answered pings are not flipped — MarkMissed is idempotent for that case.
        fx.Db.FollowUpPings.Single(p => p.Id == answered.Id).Status.Should().Be(PingStatus.Answered);
    }

    [Fact]
    public async Task Honours_explicit_ended_at_when_supplied()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ไมเกรน");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var endedAt = DateTime.UtcNow.AddHours(-1);
        var sut = new ResolveEpisodeHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new ResolveEpisodeCommand(Id: episode.Id, SeverityAfter: 1, EndedAt: endedAt),
            CancellationToken.None);

        result.EndedAt.Should().BeCloseTo(endedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Throws_when_episode_not_found()
    {
        using var fx = new HandlerTestFixture();
        var sut = new ResolveEpisodeHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(
            new ResolveEpisodeCommand(Id: Guid.NewGuid()),
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

        var sut = new ResolveEpisodeHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(
            new ResolveEpisodeCommand(Id: episode.Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }

    [Fact]
    public async Task Throws_when_episode_already_resolved()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        episode.Resolve(severityAfter: 0);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var sut = new ResolveEpisodeHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(
            new ResolveEpisodeCommand(Id: episode.Id, SeverityAfter: 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Episode is already resolved.");
    }
}
