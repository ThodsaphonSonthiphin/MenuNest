using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Episodes.ListEpisodes;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Health.Episodes;

public class ListEpisodesHandlerTests
{
    [Fact]
    public async Task Returns_episodes_ordered_started_at_desc_scoped_to_current_user()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var older = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 4,
            startedAt: DateTime.UtcNow.AddDays(-5));
        var newer = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6,
            startedAt: DateTime.UtcNow.AddDays(-1));
        var otherUser = SymptomEpisode.Start(Guid.NewGuid(), symptom.Id, severity: 5);

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.AddRange(older, newer, otherUser);
        await fx.Db.SaveChangesAsync();

        var sut = new ListEpisodesHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListEpisodesQuery(), CancellationToken.None);

        result.Select(e => e.Id).Should().Equal(newer.Id, older.Id);
    }

    [Fact]
    public async Task Filters_by_inclusive_date_range()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");

        // Use UTC dates explicitly so the From/To boundaries align with the
        // handler's interpretation (UTC days, inclusive end-of-day).
        var today = DateTime.UtcNow.Date;
        var fiveAgo = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 4,
            startedAt: today.AddDays(-5).AddHours(8));
        var twoAgo = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6,
            startedAt: today.AddDays(-2).AddHours(8));
        var sevenAgo = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5,
            startedAt: today.AddDays(-7).AddHours(8));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.AddRange(fiveAgo, twoAgo, sevenAgo);
        await fx.Db.SaveChangesAsync();

        var from = DateOnly.FromDateTime(today.AddDays(-5));
        var to = DateOnly.FromDateTime(today.AddDays(-2));
        var sut = new ListEpisodesHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new ListEpisodesQuery(From: from, To: to),
            CancellationToken.None);

        result.Select(e => e.Id).Should().BeEquivalentTo(new[] { fiveAgo.Id, twoAgo.Id });
    }

    [Fact]
    public async Task Filters_by_symptom_id()
    {
        using var fx = new HandlerTestFixture();
        var headache = Symptom.CreateSeed("ปวดหัว");
        var fever = Symptom.CreateSeed("ไข้");
        var ep1 = SymptomEpisode.Start(fx.User.Id, headache.Id, severity: 5);
        var ep2 = SymptomEpisode.Start(fx.User.Id, headache.Id, severity: 6);
        var ep3 = SymptomEpisode.Start(fx.User.Id, fever.Id, severity: 4);

        fx.Db.Symptoms.AddRange(headache, fever);
        fx.Db.SymptomEpisodes.AddRange(ep1, ep2, ep3);
        await fx.Db.SaveChangesAsync();

        var sut = new ListEpisodesHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new ListEpisodesQuery(SymptomId: headache.Id),
            CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.SymptomId == headache.Id);
    }

    [Fact]
    public async Task OnlyResolved_returns_only_resolved_non_failed_episodes()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var resolved = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6);
        resolved.Resolve(severityAfter: 0);
        var active = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        var failed = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 7);
        failed.MarkNoDrug(NoDrugReason.OutOfStock);

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.AddRange(resolved, active, failed);
        await fx.Db.SaveChangesAsync();

        var sut = new ListEpisodesHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new ListEpisodesQuery(OnlyResolved: true),
            CancellationToken.None);

        result.Should().ContainSingle();
        result.Single().Id.Should().Be(resolved.Id);
    }

    [Fact]
    public async Task OnlyFailed_returns_only_no_drug_episodes()
    {
        using var fx = new HandlerTestFixture();
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var failed = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6);
        failed.MarkNoDrug(NoDrugReason.AllDrugsActive);
        var active = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.AddRange(failed, active);
        await fx.Db.SaveChangesAsync();

        var sut = new ListEpisodesHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new ListEpisodesQuery(OnlyFailed: true),
            CancellationToken.None);

        result.Should().ContainSingle();
        result.Single().Id.Should().Be(failed.Id);
    }

    [Fact]
    public async Task Empty_filters_returns_all_user_episodes()
    {
        using var fx = new HandlerTestFixture();
        var sut = new ListEpisodesHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(new ListEpisodesQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
