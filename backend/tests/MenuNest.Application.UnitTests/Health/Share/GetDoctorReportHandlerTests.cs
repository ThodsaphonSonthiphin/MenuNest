using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Reports.GetDoctorReport;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Share;

public class GetDoctorReportHandlerTests
{
    private static readonly DateTime FixedNow =
        new(2026, 05, 17, 14, 30, 00, DateTimeKind.Utc);

    private static readonly DateOnly RangeFrom = new(2026, 04, 17);
    private static readonly DateOnly RangeTo = new(2026, 05, 17);

    /// <summary>
    /// Deterministic test stub. Always returns the seeded claims when
    /// <c>token</c> is non-empty; throws for empty tokens or when the test
    /// explicitly opts into "verify fails" mode.
    /// </summary>
    private sealed class StubShareTokenService : IShareTokenService
    {
        public bool FailVerify { get; set; }
        public ShareTokenClaims? FixedClaims { get; set; }

        public ShareTokenIssuance Issue(Guid userId, DateOnly dateFrom, DateOnly dateTo, DateTime expiresAtUtc)
            => new($"raw-{Guid.NewGuid():N}", Hash($"raw-{Guid.NewGuid():N}"));

        public ShareTokenClaims Verify(string rawToken)
        {
            if (FailVerify) throw new InvalidOperationException("token invalid");
            return FixedClaims ?? throw new InvalidOperationException("no fixed claims");
        }

        public string Hash(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    private sealed class TestContext
    {
        public HandlerTestFixture Fx { get; init; } = null!;
        public StubShareTokenService Tokens { get; init; } = null!;
        public FixedClock Clock { get; init; } = null!;
        public string RawToken { get; init; } = null!;
        public ShareLink Link { get; init; } = null!;
        public Symptom Symptom { get; init; } = null!;
        public Trigger StressTrigger { get; init; } = null!;
        public Trigger SleepTrigger { get; init; } = null!;
        public Drug Paracetamol { get; init; } = null!;
        public Drug Ibuprofen { get; init; } = null!;
        public List<SymptomEpisode> Episodes { get; init; } = null!;
    }

    private static async Task<TestContext> Seed(
        HandlerTestFixture fx, FixedClock clock,
        DateOnly? rangeFrom = null, DateOnly? rangeTo = null)
    {
        var from = rangeFrom ?? RangeFrom;
        var to = rangeTo ?? RangeTo;

        var tokens = new StubShareTokenService
        {
            FixedClaims = new ShareTokenClaims(
                UserId: fx.User.Id,
                DateFrom: from,
                DateTo: to,
                ExpiresAtUtc: clock.UtcNow.AddDays(30))
        };

        var rawToken = $"raw-{Guid.NewGuid():N}";
        var hash = tokens.Hash(rawToken);

        var link = ShareLink.Create(
            userId: fx.User.Id,
            tokenHash: hash,
            dateFrom: from,
            dateTo: to,
            expiresAt: clock.UtcNow.AddDays(30),
            nowUtc: clock.UtcNow);
        fx.Db.ShareLinks.Add(link);

        var symptom = Symptom.CreateSeed("ไมเกรน");
        var stress = Trigger.CreateSeed("เครียด");
        var sleep = Trigger.CreateSeed("นอนน้อย");
        fx.Db.Symptoms.Add(symptom);
        fx.Db.Triggers.Add(stress);
        fx.Db.Triggers.Add(sleep);

        var para = Drug.Create(fx.User.Id, "Paracetamol", DrugType.Analgesic,
            "500mg", 4, 6, 8);
        var ibu = Drug.Create(fx.User.Id, "Ibuprofen", DrugType.Nsaid,
            "400mg", 4, 6, 4);
        fx.Db.Drugs.Add(para);
        fx.Db.Drugs.Add(ibu);

        await fx.Db.SaveChangesAsync();

        return new TestContext
        {
            Fx = fx,
            Tokens = tokens,
            Clock = clock,
            RawToken = rawToken,
            Link = link,
            Symptom = symptom,
            StressTrigger = stress,
            SleepTrigger = sleep,
            Paracetamol = para,
            Ibuprofen = ibu,
            Episodes = new List<SymptomEpisode>(),
        };
    }

    private static SymptomEpisode AddEpisode(
        TestContext ctx,
        DateTime startedAt,
        int severity,
        bool isOnPeriod = false,
        bool? hasAura = null,
        FunctionalImpact? impact = null,
        Guid[]? triggerIds = null,
        DateTime? endedAt = null,
        bool noDrugTaken = false,
        NoDrugReason? noDrugReason = null)
    {
        var ep = SymptomEpisode.Start(
            userId: ctx.Fx.User.Id,
            symptomId: ctx.Symptom.Id,
            severity: severity,
            isOnPeriod: isOnPeriod,
            startedAt: startedAt,
            triggerIds: triggerIds);

        if (hasAura.HasValue || impact.HasValue)
        {
            ep.SetMigraineAttributes(
                hasAura: hasAura,
                auraTypes: null,
                auraDurationMin: null,
                location: null,
                quality: null,
                associatedSymptoms: null,
                worsenedByActivity: null,
                functionalImpact: impact);
        }

        if (endedAt.HasValue)
            ep.Resolve(endedAt.Value, severityAfter: 0);

        if (noDrugTaken && noDrugReason.HasValue)
            ep.MarkNoDrug(noDrugReason.Value);

        ctx.Fx.Db.SymptomEpisodes.Add(ep);
        ctx.Episodes.Add(ep);
        return ep;
    }

    private static GetDoctorReportHandler Build(TestContext ctx)
        => new(ctx.Fx.Db, ctx.Tokens, ctx.Clock);

    // ------------------------------------------------------------------
    // Tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task Returns_summary_with_expected_counts_for_seeded_episodes()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        // 3 episodes inside the range, 1 outside (should be excluded).
        AddEpisode(ctx, FixedNow.AddDays(-5), severity: 7, hasAura: true,
            endedAt: FixedNow.AddDays(-5).AddHours(2));
        AddEpisode(ctx, FixedNow.AddDays(-3), severity: 9,
            impact: FunctionalImpact.SevereBedrest,
            endedAt: FixedNow.AddDays(-3).AddHours(4));
        AddEpisode(ctx, FixedNow.AddDays(-1), severity: 5);
        // out of range — older than range start
        AddEpisode(ctx, RangeFrom.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            severity: 6);
        await fx.Db.SaveChangesAsync();

        var result = await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        result.Summary.TotalAttacks.Should().Be(3, "the older episode is outside the range");
        result.Summary.SevereAttacksCount.Should().Be(1, "only severity 9 hits the >=8 cutoff");
        result.Summary.DaysFullyDisabled.Should().Be(1);
        result.Summary.AttacksWithAura.Should().Be(1);
        result.Summary.AuraPercentage.Should().BeApproximately(33.3, 0.1);
        result.Summary.DaysAffected.Should().Be(3, "each episode is on a distinct day");
    }

    [Fact]
    public async Task Surfaces_MOH_RISK_when_acute_med_days_exceed_10_per_30_days()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        // 12 distinct days with intakes → MOH_RISK at 12 > 10.
        for (var i = 0; i < 12; i++)
        {
            var when = FixedNow.AddDays(-i);
            var ep = AddEpisode(ctx, when, severity: 5,
                endedAt: when.AddMinutes(45));
            await fx.Db.SaveChangesAsync();
            var intake = Intake.Create(fx.User.Id, ctx.Paracetamol.Id, doseAmount: 1,
                symptomEpisodeId: ep.Id, takenAt: when.AddMinutes(5));
            fx.Db.Intakes.Add(intake);
        }
        await fx.Db.SaveChangesAsync();

        var result = await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        result.Summary.AcuteMedDays.Should().Be(12);
        result.ClinicalFlags.Should().Contain(f => f.Code == "MOH_RISK")
            .Which.Severity.Should().Be("danger");
    }

    [Fact]
    public async Task Surfaces_FREQUENCY_NEAR_CHRONIC_when_attacks_reach_threshold()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        // Use a 30-day range so 8 attacks normalises to exactly 8/30 — at
        // the FREQUENCY_NEAR_CHRONIC threshold.
        var to = new DateOnly(2026, 05, 17);
        var from = to.AddDays(-29);
        var ctx = await Seed(fx, clock, rangeFrom: from, rangeTo: to);

        for (var i = 0; i < 8; i++)
        {
            AddEpisode(ctx, FixedNow.AddDays(-i), severity: 6);
        }
        await fx.Db.SaveChangesAsync();

        var result = await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        result.Summary.TotalAttacks.Should().Be(8);
        result.ClinicalFlags.Should().Contain(f => f.Code == "FREQUENCY_NEAR_CHRONIC");
    }

    [Fact]
    public async Task Surfaces_FUNCTIONAL_DISABILITY_when_bedrest_days_reach_threshold()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        for (var i = 0; i < 4; i++)
        {
            AddEpisode(ctx, FixedNow.AddDays(-i), severity: 9,
                impact: FunctionalImpact.SevereBedrest,
                endedAt: FixedNow.AddDays(-i).AddHours(5));
        }
        await fx.Db.SaveChangesAsync();

        var result = await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        result.Summary.DaysFullyDisabled.Should().Be(4);
        result.ClinicalFlags.Should().Contain(f => f.Code == "FUNCTIONAL_DISABILITY");
    }

    [Fact]
    public async Task TriggerCorrelations_count_episodes_per_trigger_and_compute_percentage()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        AddEpisode(ctx, FixedNow.AddDays(-1), severity: 5,
            triggerIds: new[] { ctx.StressTrigger.Id });
        AddEpisode(ctx, FixedNow.AddDays(-2), severity: 5,
            triggerIds: new[] { ctx.StressTrigger.Id, ctx.SleepTrigger.Id });
        AddEpisode(ctx, FixedNow.AddDays(-3), severity: 5,
            triggerIds: new[] { ctx.SleepTrigger.Id });
        AddEpisode(ctx, FixedNow.AddDays(-4), severity: 5);
        await fx.Db.SaveChangesAsync();

        var result = await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        result.TriggerCorrelations.Should().HaveCount(2);
        var stress = result.TriggerCorrelations.Single(t => t.TriggerId == ctx.StressTrigger.Id);
        stress.AttackCount.Should().Be(2);
        stress.Percentage.Should().Be(50.0);

        var sleep = result.TriggerCorrelations.Single(t => t.TriggerId == ctx.SleepTrigger.Id);
        sleep.AttackCount.Should().Be(2);
    }

    [Fact]
    public async Task TreatmentEfficacy_only_includes_drugs_with_intakes_and_credits_relief_within_window()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        // Episode resolved 30 min after Paracetamol → relief credited.
        var ep1Start = FixedNow.AddDays(-2);
        var ep1 = AddEpisode(ctx, ep1Start, severity: 7,
            endedAt: ep1Start.AddMinutes(30));
        await fx.Db.SaveChangesAsync();
        fx.Db.Intakes.Add(Intake.Create(fx.User.Id, ctx.Paracetamol.Id, 1,
            symptomEpisodeId: ep1.Id, takenAt: ep1Start));

        // Episode that did NOT resolve within window → no relief.
        var ep2Start = FixedNow.AddDays(-1);
        var ep2 = AddEpisode(ctx, ep2Start, severity: 6);
        await fx.Db.SaveChangesAsync();
        fx.Db.Intakes.Add(Intake.Create(fx.User.Id, ctx.Paracetamol.Id, 1,
            symptomEpisodeId: ep2.Id, takenAt: ep2Start));
        await fx.Db.SaveChangesAsync();

        var result = await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        result.TreatmentEfficacy.Should().HaveCount(1,
            "Ibuprofen had no intakes so it shouldn't appear");
        var row = result.TreatmentEfficacy[0];
        row.DrugId.Should().Be(ctx.Paracetamol.Id);
        row.DoseCount.Should().Be(2);
        row.ReliefCount.Should().Be(1);
        row.ReliefPercentage.Should().Be(50.0);
    }

    [Fact]
    public async Task NoDrugEvents_lists_episodes_with_no_drug_flag_newest_first()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        AddEpisode(ctx, FixedNow.AddDays(-5), severity: 7,
            noDrugTaken: true, noDrugReason: NoDrugReason.OutOfStock);
        AddEpisode(ctx, FixedNow.AddDays(-1), severity: 8,
            noDrugTaken: true, noDrugReason: NoDrugReason.MaxDoseReached);
        AddEpisode(ctx, FixedNow.AddDays(-2), severity: 5); // not a no-drug event
        await fx.Db.SaveChangesAsync();

        var result = await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        result.NoDrugEvents.Should().HaveCount(2);
        result.NoDrugEvents[0].Reason.Should().Be(NoDrugReason.MaxDoseReached,
            "the newest no-drug event comes first");
    }

    [Fact]
    public async Task Patterns_compute_onset_time_buckets_and_period_rates()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        // 1 morning, 1 evening, 1 night episode (UTC times)
        var morning = new DateTime(2026, 05, 10, 08, 00, 00, DateTimeKind.Utc);
        var evening = new DateTime(2026, 05, 11, 19, 00, 00, DateTimeKind.Utc);
        var night = new DateTime(2026, 05, 12, 03, 00, 00, DateTimeKind.Utc);

        AddEpisode(ctx, morning, severity: 5, isOnPeriod: true);
        AddEpisode(ctx, evening, severity: 5, isOnPeriod: true);
        AddEpisode(ctx, night, severity: 5, isOnPeriod: false);
        await fx.Db.SaveChangesAsync();

        var result = await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        result.Patterns.OnsetTimeBuckets["morning"].Should().Be(1);
        result.Patterns.OnsetTimeBuckets["evening"].Should().Be(1);
        result.Patterns.OnsetTimeBuckets["night"].Should().Be(1);
        result.Patterns.OnsetTimeBuckets["afternoon"].Should().Be(0);

        result.Patterns.AttacksDuringPeriod.Should().Be(2);
        result.Patterns.AttacksOutsidePeriod.Should().Be(1);
    }

    [Fact]
    public async Task Records_share_link_access_count_and_last_accessed()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);
        AddEpisode(ctx, FixedNow.AddDays(-1), severity: 5);
        await fx.Db.SaveChangesAsync();

        await Build(ctx).Handle(new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        var persisted = fx.Db.ShareLinks.Single(l => l.Id == ctx.Link.Id);
        persisted.AccessCount.Should().Be(1);
        persisted.LastAccessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Throws_when_token_verification_fails()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);
        ctx.Tokens.FailVerify = true;

        var act = async () => await Build(ctx).Handle(
            new GetDoctorReportQuery("bad-token"), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Throws_DomainException_when_share_link_revoked()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        ctx.Link.Revoke();
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Throws_DomainException_when_share_link_expired()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        // Advance clock past the link expiry.
        clock.UtcNow = clock.UtcNow.AddDays(31);

        var act = async () => await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Builds_day_buckets_with_episodes_intakes_and_followups()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var ctx = await Seed(fx, clock);

        var dayA = new DateTime(2026, 05, 16, 10, 00, 00, DateTimeKind.Utc);
        var dayB = new DateTime(2026, 05, 17, 09, 30, 00, DateTimeKind.Utc);

        var ep1 = AddEpisode(ctx, dayA, severity: 7,
            endedAt: dayA.AddHours(2),
            isOnPeriod: true);
        var ep2 = AddEpisode(ctx, dayB, severity: 8);
        await fx.Db.SaveChangesAsync();

        fx.Db.Intakes.Add(Intake.Create(fx.User.Id, ctx.Paracetamol.Id, 1,
            symptomEpisodeId: ep1.Id, takenAt: dayA.AddMinutes(10)));
        fx.Db.FollowUpPings.Add(FollowUpPing.Schedule(ep1.Id, dayA.AddMinutes(40)));
        await fx.Db.SaveChangesAsync();

        var result = await Build(ctx).Handle(
            new GetDoctorReportQuery(ctx.RawToken), CancellationToken.None);

        result.Days.Should().HaveCount(2);
        result.Days[0].Date.Should().Be(new DateOnly(2026, 05, 17), "newest day first");

        var dayBuckets = result.Days.ToDictionary(d => d.Date);
        dayBuckets[new DateOnly(2026, 05, 16)].IsPeriodDay.Should().BeTrue();
        dayBuckets[new DateOnly(2026, 05, 16)].DoseCount.Should().Be(1);
        dayBuckets[new DateOnly(2026, 05, 16)].PeakSeverity.Should().Be(7);
        dayBuckets[new DateOnly(2026, 05, 16)].Episodes.Single().Intakes.Should().HaveCount(1);
        dayBuckets[new DateOnly(2026, 05, 16)].Episodes.Single().FollowUps.Should().HaveCount(1);
    }
}
