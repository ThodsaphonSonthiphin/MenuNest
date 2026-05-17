using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.FollowUps.RetroCloseEpisode;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.FollowUps;

public class RetroCloseEpisodeHandlerTests
{
    private static readonly DateTime FixedNow =
        new(2026, 05, 17, 12, 00, 00, DateTimeKind.Utc);

    private static RetroCloseEpisodeHandler Build(HandlerTestFixture fx, FixedClock clock)
        => new(fx.Db, fx.UserProvisioner.Object, new RetroCloseEpisodeValidator(), clock);

    private static async Task<SymptomEpisode> SeedActiveEpisode(HandlerTestFixture fx)
    {
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();
        return episode;
    }

    [Fact]
    public async Task RetroResolved_sets_endedAt_retroClosed_and_estimatedDuration()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var episode = await SeedActiveEpisode(fx);

        await Build(fx, clock).Handle(
            new RetroCloseEpisodeCommand(
                EpisodeId: episode.Id,
                EstimatedDuration: "1_to_3h",
                Outcome: PingResponse.RetroResolved),
            CancellationToken.None);

        var persisted = fx.Db.SymptomEpisodes.Single(e => e.Id == episode.Id);
        persisted.EndedAt.Should().Be(FixedNow);
        persisted.RetroClosed.Should().BeTrue();
        persisted.RetroEstimatedDuration.Should().Be("1_to_3h");
    }

    [Fact]
    public async Task RetroUnknown_also_closes_episode()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var episode = await SeedActiveEpisode(fx);

        await Build(fx, clock).Handle(
            new RetroCloseEpisodeCommand(
                EpisodeId: episode.Id,
                EstimatedDuration: "not_sure",
                Outcome: PingResponse.RetroUnknown),
            CancellationToken.None);

        var persisted = fx.Db.SymptomEpisodes.Single(e => e.Id == episode.Id);
        persisted.EndedAt.Should().Be(FixedNow);
        persisted.RetroClosed.Should().BeTrue();
        persisted.RetroEstimatedDuration.Should().Be("not_sure");
    }

    [Fact]
    public async Task Cancels_pending_pings_on_the_episode()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var episode = await SeedActiveEpisode(fx);

        var ping1 = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-10));
        var ping2 = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(20));
        fx.Db.FollowUpPings.AddRange(ping1, ping2);
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new RetroCloseEpisodeCommand(
                EpisodeId: episode.Id,
                EstimatedDuration: "within_1h",
                Outcome: PingResponse.RetroResolved),
            CancellationToken.None);

        var pings = fx.Db.FollowUpPings.Where(p => p.SymptomEpisodeId == episode.Id).ToList();
        pings.Should().AllSatisfy(p => p.Status.Should().Be(PingStatus.Missed));
    }

    [Fact]
    public async Task Does_not_touch_already_answered_pings()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var episode = await SeedActiveEpisode(fx);

        var answered = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-30));
        answered.MarkAsked();
        answered.RecordResponse(PingResponse.Same, severityAtCheck: 6);
        fx.Db.FollowUpPings.Add(answered);
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new RetroCloseEpisodeCommand(
                EpisodeId: episode.Id,
                EstimatedDuration: "hours",
                Outcome: PingResponse.RetroUnknown),
            CancellationToken.None);

        fx.Db.FollowUpPings.Single(p => p.Id == answered.Id).Status
            .Should().Be(PingStatus.Answered);
    }

    [Fact]
    public async Task Rejects_already_closed_episode()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var episode = await SeedActiveEpisode(fx);

        episode.Resolve(endedAt: FixedNow.AddMinutes(-60), severityAfter: 0);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx, clock).Handle(
            new RetroCloseEpisodeCommand(
                EpisodeId: episode.Id,
                EstimatedDuration: "within_1h",
                Outcome: PingResponse.RetroResolved),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Episode is already closed.");
    }

    [Fact]
    public async Task Rejects_non_retro_outcome_Resolved()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var episode = await SeedActiveEpisode(fx);

        var act = async () => await Build(fx, clock).Handle(
            new RetroCloseEpisodeCommand(
                EpisodeId: episode.Id,
                EstimatedDuration: "within_1h",
                Outcome: PingResponse.Resolved),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Outcome must be RetroResolved or RetroUnknown.");
    }

    [Fact]
    public async Task Rejects_non_retro_outcomes_Improved_Same_Worse()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var episode = await SeedActiveEpisode(fx);

        foreach (var bad in new[] { PingResponse.Improved, PingResponse.Same, PingResponse.Worse })
        {
            var act = async () => await Build(fx, clock).Handle(
                new RetroCloseEpisodeCommand(
                    EpisodeId: episode.Id,
                    EstimatedDuration: "within_1h",
                    Outcome: bad),
                CancellationToken.None);
            await act.Should().ThrowAsync<DomainException>()
                .WithMessage("Outcome must be RetroResolved or RetroUnknown.");
        }
    }

    [Fact]
    public async Task Throws_when_episode_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var otherEpisode = SymptomEpisode.Start(
            userId: Guid.NewGuid(), symptomId: symptom.Id, severity: 5);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(otherEpisode);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx, clock).Handle(
            new RetroCloseEpisodeCommand(
                EpisodeId: otherEpisode.Id,
                EstimatedDuration: "within_1h",
                Outcome: PingResponse.RetroResolved),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Episode not found.");
    }
}
