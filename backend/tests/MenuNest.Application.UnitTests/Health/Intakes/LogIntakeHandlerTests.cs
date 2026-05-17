using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Intakes.LogIntake;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Intakes;

public class LogIntakeHandlerTests
{
    private static readonly DateTime FixedNow =
        new(2026, 05, 17, 12, 00, 00, DateTimeKind.Utc);

    private static Drug NewDrug(Guid userId, string name = "Paracetamol")
        => Drug.Create(
            userId: userId,
            name: name,
            drugType: DrugType.Analgesic,
            doseStrength: "500mg",
            effectDurationMinHours: 4,
            effectDurationMaxHours: 6,
            maxDailyDose: 4,
            stockCount: 10);

    private static LogIntakeHandler Build(HandlerTestFixture fx, FixedClock clock)
        => new(fx.Db, fx.UserProvisioner.Object, new LogIntakeValidator(), clock);

    [Fact]
    public async Task Creates_intake_without_episode_link_when_episode_id_omitted()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var drug = NewDrug(fx.User.Id);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new LogIntakeCommand(DrugId: drug.Id, DoseAmount: 1),
            CancellationToken.None);

        result.DrugId.Should().Be(drug.Id);
        result.DrugName.Should().Be("Paracetamol");
        result.SymptomEpisodeId.Should().BeNull();
        result.DoseAmount.Should().Be(1);

        var intake = fx.Db.Intakes.Single();
        intake.UserId.Should().Be(fx.User.Id);
        intake.SymptomEpisodeId.Should().BeNull();
        fx.Db.FollowUpPings.Should().BeEmpty(
            "no follow-up should be scheduled when intake is not linked to an episode");
    }

    [Fact]
    public async Task Links_intake_to_episode_and_schedules_follow_up_at_plus_30_minutes()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6);
        var drug = NewDrug(fx.User.Id);

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new LogIntakeCommand(
                DrugId: drug.Id, DoseAmount: 1, SymptomEpisodeId: episode.Id),
            CancellationToken.None);

        result.SymptomEpisodeId.Should().Be(episode.Id);

        var ping = fx.Db.FollowUpPings.Single();
        ping.SymptomEpisodeId.Should().Be(episode.Id);
        ping.ScheduledAt.Should().Be(FixedNow.AddMinutes(30));
        ping.Status.Should().Be(PingStatus.Pending);
    }

    [Fact]
    public async Task Marks_prior_pending_pings_for_same_episode_as_missed()
    {
        // Per spec: "only most recent ping per episode should be active".
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        var drug = NewDrug(fx.User.Id);
        var olderPing = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-10));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.Add(drug);
        fx.Db.FollowUpPings.Add(olderPing);
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new LogIntakeCommand(
                DrugId: drug.Id, DoseAmount: 1, SymptomEpisodeId: episode.Id),
            CancellationToken.None);

        var pings = fx.Db.FollowUpPings.ToList();
        pings.Should().HaveCount(2);
        pings.Single(p => p.Id == olderPing.Id).Status.Should().Be(PingStatus.Missed);
        pings.Single(p => p.Id != olderPing.Id).Status.Should().Be(PingStatus.Pending);
    }

    [Fact]
    public async Task Does_not_touch_pings_from_other_episodes()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episodeA = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 4);
        var episodeB = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        var drug = NewDrug(fx.User.Id);
        var otherPing = FollowUpPing.Schedule(episodeB.Id, FixedNow.AddMinutes(20));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.AddRange(episodeA, episodeB);
        fx.Db.Drugs.Add(drug);
        fx.Db.FollowUpPings.Add(otherPing);
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new LogIntakeCommand(
                DrugId: drug.Id, DoseAmount: 1, SymptomEpisodeId: episodeA.Id),
            CancellationToken.None);

        // Other episode's ping is untouched.
        fx.Db.FollowUpPings.Single(p => p.Id == otherPing.Id).Status
            .Should().Be(PingStatus.Pending);
    }

    [Fact]
    public async Task Validator_rejects_non_positive_dose_amount()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var drug = NewDrug(fx.User.Id);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx, clock).Handle(
            new LogIntakeCommand(DrugId: drug.Id, DoseAmount: 0),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Validator_rejects_empty_drug_id()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var act = async () => await Build(fx, clock).Handle(
            new LogIntakeCommand(DrugId: Guid.Empty, DoseAmount: 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Throws_when_drug_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var otherUserId = Guid.NewGuid();
        var drug = NewDrug(otherUserId);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx, clock).Handle(
            new LogIntakeCommand(DrugId: drug.Id, DoseAmount: 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Drug not found.");
    }

    [Fact]
    public async Task Throws_when_drug_soft_deleted()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var drug = NewDrug(fx.User.Id);
        drug.SoftDelete();
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx, clock).Handle(
            new LogIntakeCommand(DrugId: drug.Id, DoseAmount: 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Drug not found.");
    }

    [Fact]
    public async Task Throws_when_episode_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var otherEpisode = SymptomEpisode.Start(Guid.NewGuid(), symptom.Id, severity: 4);
        var drug = NewDrug(fx.User.Id);

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(otherEpisode);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx, clock).Handle(
            new LogIntakeCommand(
                DrugId: drug.Id, DoseAmount: 1, SymptomEpisodeId: otherEpisode.Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }

    [Fact]
    public async Task Honours_explicit_taken_at_when_supplied()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var drug = NewDrug(fx.User.Id);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var backdated = FixedNow.AddHours(-2);
        var result = await Build(fx, clock).Handle(
            new LogIntakeCommand(DrugId: drug.Id, DoseAmount: 1, TakenAt: backdated),
            CancellationToken.None);

        result.TakenAt.Should().Be(backdated);
    }
}
