using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.FollowUps.RecordPingResponse;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.FollowUps;

public class RecordPingResponseHandlerTests
{
    private static readonly DateTime FixedNow =
        new(2026, 05, 17, 12, 00, 00, DateTimeKind.Utc);

    private static RecordPingResponseHandler Build(HandlerTestFixture fx, FixedClock clock)
        => new(fx.Db, fx.UserProvisioner.Object, new RecordPingResponseValidator(), clock);

    private static async Task<(SymptomEpisode Episode, FollowUpPing Ping)> SeedEpisodeWithPing(
        HandlerTestFixture fx, int severity = 6)
    {
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: severity);
        var ping = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-1));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.FollowUpPings.Add(ping);
        await fx.Db.SaveChangesAsync();
        return (episode, ping);
    }

    [Fact]
    public async Task Resolved_closes_episode_and_marks_other_pending_pings_missed()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var (episode, ping) = await SeedEpisodeWithPing(fx);

        // Extra pending ping on same episode — should be marked missed
        // when the user resolves.
        var extra = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(30));
        fx.Db.FollowUpPings.Add(extra);
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new RecordPingResponseCommand(ping.Id, PingResponse.Resolved),
            CancellationToken.None);

        var persistedEpisode = fx.Db.SymptomEpisodes.Single(e => e.Id == episode.Id);
        persistedEpisode.EndedAt.Should().Be(FixedNow);
        persistedEpisode.SeverityAfter.Should().Be(0);

        var answeredPing = fx.Db.FollowUpPings.Single(p => p.Id == ping.Id);
        answeredPing.Status.Should().Be(PingStatus.Answered);
        answeredPing.Response.Should().Be(PingResponse.Resolved);

        var missedPing = fx.Db.FollowUpPings.Single(p => p.Id == extra.Id);
        missedPing.Status.Should().Be(PingStatus.Missed);
    }

    [Fact]
    public async Task RetroResolved_closes_episode_like_Resolved()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var (episode, ping) = await SeedEpisodeWithPing(fx);

        await Build(fx, clock).Handle(
            new RecordPingResponseCommand(ping.Id, PingResponse.RetroResolved),
            CancellationToken.None);

        fx.Db.SymptomEpisodes.Single(e => e.Id == episode.Id).EndedAt.Should().Be(FixedNow);
    }

    [Fact]
    public async Task Improved_reschedules_ping_at_plus_30_minutes_when_under_cap()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var (episode, ping) = await SeedEpisodeWithPing(fx);

        await Build(fx, clock).Handle(
            new RecordPingResponseCommand(ping.Id, PingResponse.Improved, SeverityAtCheck: 4),
            CancellationToken.None);

        var pings = fx.Db.FollowUpPings.Where(p => p.SymptomEpisodeId == episode.Id).ToList();
        pings.Should().HaveCount(2);
        var answered = pings.Single(p => p.Id == ping.Id);
        answered.Status.Should().Be(PingStatus.Answered);
        answered.Response.Should().Be(PingResponse.Improved);
        answered.SeverityAtCheck.Should().Be(4);

        var next = pings.Single(p => p.Id != ping.Id);
        next.ScheduledAt.Should().Be(FixedNow.AddMinutes(30));
        next.Status.Should().Be(PingStatus.Pending);
    }

    [Fact]
    public async Task Same_reschedules_ping_at_plus_30_minutes()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var (episode, ping) = await SeedEpisodeWithPing(fx);

        await Build(fx, clock).Handle(
            new RecordPingResponseCommand(ping.Id, PingResponse.Same),
            CancellationToken.None);

        fx.Db.FollowUpPings.Count(p => p.SymptomEpisodeId == episode.Id).Should().Be(2);
        fx.Db.FollowUpPings
            .Single(p => p.SymptomEpisodeId == episode.Id && p.Status == PingStatus.Pending)
            .ScheduledAt.Should().Be(FixedNow.AddMinutes(30));
    }

    [Fact]
    public async Task Worse_reschedules_ping_at_plus_30_minutes()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var (episode, ping) = await SeedEpisodeWithPing(fx);

        await Build(fx, clock).Handle(
            new RecordPingResponseCommand(ping.Id, PingResponse.Worse),
            CancellationToken.None);

        fx.Db.FollowUpPings.Count(p => p.SymptomEpisodeId == episode.Id).Should().Be(2);
    }

    [Fact]
    public async Task After_three_pings_Improved_no_longer_reschedules()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var (episode, firstPing) = await SeedEpisodeWithPing(fx);

        // Seed two additional pings → total 3 (cap).
        var second = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-2));
        var third = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-3));
        fx.Db.FollowUpPings.AddRange(second, third);
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new RecordPingResponseCommand(firstPing.Id, PingResponse.Improved),
            CancellationToken.None);

        // Still 3 total — no new ping scheduled.
        fx.Db.FollowUpPings.Count(p => p.SymptomEpisodeId == episode.Id).Should().Be(3);
    }

    [Fact]
    public async Task Throws_when_ping_not_found()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var act = async () => await Build(fx, clock).Handle(
            new RecordPingResponseCommand(Guid.NewGuid(), PingResponse.Resolved),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Ping not found.");
    }

    [Fact]
    public async Task Throws_when_ping_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var otherEpisode = SymptomEpisode.Start(
            userId: Guid.NewGuid(), symptomId: symptom.Id, severity: 5);
        var otherPing = FollowUpPing.Schedule(otherEpisode.Id, FixedNow.AddMinutes(-1));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(otherEpisode);
        fx.Db.FollowUpPings.Add(otherPing);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx, clock).Handle(
            new RecordPingResponseCommand(otherPing.Id, PingResponse.Resolved),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Ping not found.");
    }

    [Fact]
    public async Task Marks_ping_as_Asked_then_Answered_when_starting_from_Pending()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var (_, ping) = await SeedEpisodeWithPing(fx);

        ping.Status.Should().Be(PingStatus.Pending);

        await Build(fx, clock).Handle(
            new RecordPingResponseCommand(ping.Id, PingResponse.Same),
            CancellationToken.None);

        var persisted = fx.Db.FollowUpPings.Single(p => p.Id == ping.Id);
        persisted.Status.Should().Be(PingStatus.Answered);
        persisted.AskedAt.Should().NotBeNull();
        persisted.RespondedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Records_response_when_ping_already_marked_Asked_by_dispatcher()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var (_, ping) = await SeedEpisodeWithPing(fx);

        ping.MarkAsked();
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new RecordPingResponseCommand(ping.Id, PingResponse.Same),
            CancellationToken.None);

        var persisted = fx.Db.FollowUpPings.Single(p => p.Id == ping.Id);
        persisted.Status.Should().Be(PingStatus.Answered);
    }

    [Fact]
    public async Task Idempotent_resolve_when_episode_already_closed()
    {
        // Simulates user double-tap or race between dispatcher and in-app.
        // Both Resolved responses should leave the episode closed once.
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var (episode, ping) = await SeedEpisodeWithPing(fx);

        episode.Resolve(endedAt: FixedNow.AddMinutes(-10), severityAfter: 0);
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new RecordPingResponseCommand(ping.Id, PingResponse.Resolved),
            CancellationToken.None);

        var persisted = fx.Db.SymptomEpisodes.Single(e => e.Id == episode.Id);
        persisted.EndedAt.Should().Be(FixedNow.AddMinutes(-10));
    }
}
