using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.FollowUps.GetPendingPings;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Health.FollowUps;

public class GetPendingPingsHandlerTests
{
    private static readonly DateTime FixedNow =
        new(2026, 05, 17, 12, 00, 00, DateTimeKind.Utc);

    private static GetPendingPingsHandler Build(HandlerTestFixture fx, FixedClock clock)
        => new(fx.Db, clock);

    private static Drug NewDrug(Guid userId, string name)
        => Drug.Create(
            userId: userId,
            name: name,
            drugType: DrugType.Analgesic,
            doseStrength: "500mg",
            effectDurationMinHours: 4,
            effectDurationMaxHours: 6,
            maxDailyDose: 4,
            stockCount: 10);

    [Fact]
    public async Task Returns_only_due_pending_pings()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);

        var duePast = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-5));
        var dueExact = FollowUpPing.Schedule(episode.Id, FixedNow);
        var futurePing = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(5));
        var answeredPing = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-30));
        answeredPing.MarkAsked();
        answeredPing.RecordResponse(PingResponse.Same);
        var missedPing = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-60));
        missedPing.MarkMissed();

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.FollowUpPings.AddRange(duePast, dueExact, futurePing, answeredPing, missedPing);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetPendingPingsQuery(), CancellationToken.None);

        result.Select(r => r.PingId).Should().BeEquivalentTo(new[] { duePast.Id, dueExact.Id });
    }

    [Fact]
    public async Task Orders_by_scheduled_at_ascending()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);

        var older = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-30));
        var newer = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-5));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        // Insertion order is newer-first so we know the handler does the sort.
        fx.Db.FollowUpPings.AddRange(newer, older);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetPendingPingsQuery(), CancellationToken.None);

        result.Select(r => r.PingId).Should().ContainInOrder(older.Id, newer.Id);
    }

    [Fact]
    public async Task Respects_limit_parameter()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);

        for (var i = 0; i < 5; i++)
        {
            fx.Db.FollowUpPings.Add(
                FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-10 + i)));
        }

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetPendingPingsQuery(Limit: 3), CancellationToken.None);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Joins_episode_user_symptom_and_last_intake()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ไมเกรน");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 7);
        var drug = NewDrug(fx.User.Id, "Paracetamol");

        // Two intakes — handler should select the most-recent.
        var olderIntake = Intake.Create(
            userId: fx.User.Id, drugId: drug.Id, doseAmount: 1,
            symptomEpisodeId: episode.Id, takenAt: FixedNow.AddMinutes(-90));
        var newerIntake = Intake.Create(
            userId: fx.User.Id, drugId: drug.Id, doseAmount: 1,
            symptomEpisodeId: episode.Id, takenAt: FixedNow.AddMinutes(-25));

        var ping = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-1));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.Add(drug);
        fx.Db.Intakes.AddRange(olderIntake, newerIntake);
        fx.Db.FollowUpPings.Add(ping);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetPendingPingsQuery(), CancellationToken.None);

        var dto = result.Single();
        dto.PingId.Should().Be(ping.Id);
        dto.EpisodeId.Should().Be(episode.Id);
        dto.UserId.Should().Be(fx.User.Id);
        dto.SymptomId.Should().Be(symptom.Id);
        dto.SymptomName.Should().Be("ไมเกรน");
        dto.Severity.Should().Be(7);
        dto.ScheduledAt.Should().Be(ping.ScheduledAt);
        dto.LastIntakeAt.Should().Be(newerIntake.TakenAt);
        dto.LastDrugName.Should().Be("Paracetamol");
        dto.MinutesSinceLastIntake.Should().Be(25);
    }

    [Fact]
    public async Task Returns_zero_minutes_when_no_intake_logged()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดท้อง");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 4);
        var ping = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-1));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.FollowUpPings.Add(ping);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetPendingPingsQuery(), CancellationToken.None);

        var dto = result.Single();
        dto.LastIntakeAt.Should().BeNull();
        dto.LastDrugName.Should().BeNull();
        dto.MinutesSinceLastIntake.Should().Be(0);
    }

    [Fact]
    public async Task Returns_pings_across_multiple_users()
    {
        // Dispatcher runs system-context — must include every user's
        // due pings, not just the current one.
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");

        var ownEpisode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        var otherEpisode = SymptomEpisode.Start(Guid.NewGuid(), symptom.Id, severity: 5);

        var ownPing = FollowUpPing.Schedule(ownEpisode.Id, FixedNow.AddMinutes(-1));
        var otherPing = FollowUpPing.Schedule(otherEpisode.Id, FixedNow.AddMinutes(-2));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.AddRange(ownEpisode, otherEpisode);
        fx.Db.FollowUpPings.AddRange(ownPing, otherPing);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetPendingPingsQuery(), CancellationToken.None);

        result.Select(r => r.PingId).Should().BeEquivalentTo(new[] { ownPing.Id, otherPing.Id });
    }

    [Fact]
    public async Task Returns_empty_when_no_due_pings()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var result = await Build(fx, clock).Handle(
            new GetPendingPingsQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
