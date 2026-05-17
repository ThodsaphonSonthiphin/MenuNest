using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Intakes.LogNoDrug;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Intakes;

public class LogNoDrugHandlerTests
{
    private static readonly DateTime FixedNow =
        new(2026, 05, 17, 12, 00, 00, DateTimeKind.Utc);

    private static LogNoDrugHandler Build(HandlerTestFixture fx, FixedClock clock)
        => new(fx.Db, fx.UserProvisioner.Object, clock);

    [Fact]
    public async Task Marks_episode_as_no_drug_taken_with_reason()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new LogNoDrugCommand(episode.Id, NoDrugReason.AllDrugsActive),
            CancellationToken.None);

        var persisted = fx.Db.SymptomEpisodes.Single(e => e.Id == episode.Id);
        persisted.NoDrugTaken.Should().BeTrue();
        persisted.NoDrugReasonCode.Should().Be(NoDrugReason.AllDrugsActive);
    }

    [Fact]
    public async Task Schedules_self_resolving_follow_up_at_plus_60_minutes()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 4);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        await Build(fx, clock).Handle(
            new LogNoDrugCommand(episode.Id, NoDrugReason.UserSkip),
            CancellationToken.None);

        var ping = fx.Db.FollowUpPings.Single();
        ping.SymptomEpisodeId.Should().Be(episode.Id);
        ping.ScheduledAt.Should().Be(FixedNow.AddMinutes(60));
        ping.Status.Should().Be(PingStatus.Pending);
    }

    [Fact]
    public async Task Throws_when_episode_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var foreignEpisode = SymptomEpisode.Start(Guid.NewGuid(), symptom.Id, severity: 4);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(foreignEpisode);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx, clock).Handle(
            new LogNoDrugCommand(foreignEpisode.Id, NoDrugReason.OutOfStock),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }

    [Fact]
    public async Task Throws_when_episode_does_not_exist()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var act = async () => await Build(fx, clock).Handle(
            new LogNoDrugCommand(Guid.NewGuid(), NoDrugReason.NoDrugTreatsThis),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }
}
