using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health;
using MenuNest.Application.UseCases.Health.Intakes.GetTakeMedicationContext;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Intakes;

public class GetTakeMedicationContextHandlerTests
{
    // Stable "now" for deterministic windowing math. Sits squarely in the
    // middle of a UTC day so today-start / tomorrow-start calculations
    // are obvious in test assertions.
    private static readonly DateTime FixedNow =
        new(2026, 05, 17, 12, 00, 00, DateTimeKind.Utc);

    private static Drug NewDrug(
        Guid userId,
        string name,
        IEnumerable<Guid>? treats = null,
        int stock = 10,
        int maxDaily = 4,
        int effectMin = 4,
        int effectMax = 6)
        => Drug.Create(
            userId: userId,
            name: name,
            drugType: DrugType.Analgesic,
            doseStrength: "500mg",
            effectDurationMinHours: effectMin,
            effectDurationMaxHours: effectMax,
            maxDailyDose: maxDaily,
            stockCount: stock,
            treatsSymptomIds: treats);

    private static GetTakeMedicationContextHandler Build(HandlerTestFixture fx, FixedClock clock)
        => new(fx.Db, fx.UserProvisioner.Object, clock);

    [Fact]
    public async Task All_takeable_when_no_intakes_yet_and_drugs_all_treat_symptom()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 6);
        var a = NewDrug(fx.User.Id, "Paracetamol", treats: new[] { symptom.Id });
        var b = NewDrug(fx.User.Id, "Ibuprofen",   treats: new[] { symptom.Id });
        var c = NewDrug(fx.User.Id, "Cafergot",    treats: new[] { symptom.Id });

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.AddRange(a, b, c);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        result.TakeableDrugs.Should().HaveCount(3);
        result.ActiveDrugs.Should().BeEmpty();
        result.BlockedDrugs.Should().BeEmpty();
        result.TakeableDrugs.Select(t => t.DrugName).Should()
            .ContainInOrder("Cafergot", "Ibuprofen", "Paracetamol");
    }

    [Fact]
    public async Task Returns_episode_metadata_with_symptom_name_and_severity()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var symptom = Symptom.CreateSeed("ไมเกรน");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 8);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        result.SymptomEpisodeId.Should().Be(episode.Id);
        result.SymptomId.Should().Be(symptom.Id);
        result.SymptomName.Should().Be("ไมเกรน");
        result.CurrentSeverity.Should().Be(8);
    }

    [Fact]
    public async Task Surfaces_active_drug_with_progress_and_remaining_minutes()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        // Effect window = 6h. Taken 2h ago → 4h remaining, 33.3% progress.
        var drug = NewDrug(fx.User.Id, "Paracetamol", treats: new[] { symptom.Id },
            effectMin: 4, effectMax: 6);
        var intake = Intake.Create(
            userId: fx.User.Id, drugId: drug.Id, doseAmount: 1,
            symptomEpisodeId: episode.Id, takenAt: clock.UtcNow.AddHours(-2));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.Add(drug);
        fx.Db.Intakes.Add(intake);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        result.ActiveDrugs.Should().HaveCount(1);
        var active = result.ActiveDrugs[0];
        active.DrugId.Should().Be(drug.Id);
        active.LastTakenAt.Should().Be(intake.TakenAt);
        active.EffectEndsAt.Should().Be(intake.TakenAt.AddHours(6));
        active.RemainingMinutes.Should().Be(4 * 60);
        active.ProgressPct.Should().BeApproximately(33.3, 0.5);
    }

    [Fact]
    public async Task Blocks_active_drug_with_still_active_reason_and_effect_ends_at()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        var drug = NewDrug(fx.User.Id, "Paracetamol", treats: new[] { symptom.Id },
            effectMin: 4, effectMax: 6);
        var intake = Intake.Create(
            userId: fx.User.Id, drugId: drug.Id, doseAmount: 1,
            symptomEpisodeId: episode.Id, takenAt: clock.UtcNow.AddHours(-1));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.Add(drug);
        fx.Db.Intakes.Add(intake);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        result.BlockedDrugs.Should().HaveCount(1);
        var blocked = result.BlockedDrugs[0];
        blocked.DrugId.Should().Be(drug.Id);
        blocked.Reason.Should().Be(BlockedReason.StillActive);
        blocked.AvailableAt.Should().Be(intake.TakenAt.AddHours(6));
        result.TakeableDrugs.Should().BeEmpty();
    }

    [Fact]
    public async Task Blocks_drug_with_max_dose_reached_and_available_at_tomorrow_midnight()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);
        // maxDaily = 2, effect window = 2h so first dose has already worn off
        // by FixedNow. Both intakes are today (UTC) so the sum hits the cap.
        var drug = NewDrug(fx.User.Id, "Paracetamol", treats: new[] { symptom.Id },
            maxDaily: 2, effectMin: 1, effectMax: 2);
        var early  = Intake.Create(fx.User.Id, drug.Id, doseAmount: 1,
            takenAt: clock.UtcNow.AddHours(-10));
        var later  = Intake.Create(fx.User.Id, drug.Id, doseAmount: 1,
            takenAt: clock.UtcNow.AddHours(-9));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.Add(drug);
        fx.Db.Intakes.AddRange(early, later);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        result.BlockedDrugs.Should().HaveCount(1);
        var blocked = result.BlockedDrugs[0];
        blocked.Reason.Should().Be(BlockedReason.MaxDoseReached);
        // tomorrow 00:00 UTC = 2026-05-18 00:00:00Z (FixedNow is 2026-05-17 12:00Z).
        blocked.AvailableAt.Should().Be(new DateTime(2026, 05, 18, 0, 0, 0, DateTimeKind.Utc));
        result.TakeableDrugs.Should().BeEmpty();
        result.ActiveDrugs.Should().BeEmpty();
    }

    [Fact]
    public async Task Blocks_out_of_stock_drug_with_null_available_at()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 4);
        var drug = NewDrug(fx.User.Id, "Cafergot", treats: new[] { symptom.Id }, stock: 0);

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        result.BlockedDrugs.Should().HaveCount(1);
        var blocked = result.BlockedDrugs[0];
        blocked.Reason.Should().Be(BlockedReason.OutOfStock);
        blocked.AvailableAt.Should().BeNull();
    }

    [Fact]
    public async Task All_blocked_yields_empty_takeable_and_full_blocked_list()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 7);

        // Drug A: still-active. Drug B: max-dose reached. Drug C: out of stock.
        var a = NewDrug(fx.User.Id, "A_Active",    treats: new[] { symptom.Id },
            effectMin: 4, effectMax: 6);
        var b = NewDrug(fx.User.Id, "B_MaxDose",   treats: new[] { symptom.Id },
            maxDaily: 1, effectMin: 1, effectMax: 2);
        var c = NewDrug(fx.User.Id, "C_NoStock",   treats: new[] { symptom.Id },
            stock: 0);

        var aIntake = Intake.Create(fx.User.Id, a.Id, doseAmount: 1,
            takenAt: clock.UtcNow.AddHours(-1));
        var bIntake = Intake.Create(fx.User.Id, b.Id, doseAmount: 1,
            takenAt: clock.UtcNow.AddHours(-10));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.AddRange(a, b, c);
        fx.Db.Intakes.AddRange(aIntake, bIntake);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        result.TakeableDrugs.Should().BeEmpty();
        result.BlockedDrugs.Should().HaveCount(3);
        // Sorted by name ascending.
        result.BlockedDrugs.Select(b => b.DrugName).Should()
            .ContainInOrder("A_Active", "B_MaxDose", "C_NoStock");
        result.BlockedDrugs.Single(x => x.DrugName == "A_Active").Reason
            .Should().Be(BlockedReason.StillActive);
        result.BlockedDrugs.Single(x => x.DrugName == "B_MaxDose").Reason
            .Should().Be(BlockedReason.MaxDoseReached);
        result.BlockedDrugs.Single(x => x.DrugName == "C_NoStock").Reason
            .Should().Be(BlockedReason.OutOfStock);
    }

    [Fact]
    public async Task Drugs_not_treating_symptom_are_dropped_from_all_lists()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var migraine = Symptom.CreateSeed("ไมเกรน");
        var stomach  = Symptom.CreateSeed("ปวดท้อง");
        var episode = SymptomEpisode.Start(fx.User.Id, migraine.Id, severity: 5);

        var treatsMigraine = NewDrug(fx.User.Id, "Cafergot", treats: new[] { migraine.Id });
        // This drug only treats stomach pain — should NOT show up for a migraine episode.
        var treatsStomach  = NewDrug(fx.User.Id, "Antacid", treats: new[] { stomach.Id });

        fx.Db.Symptoms.AddRange(migraine, stomach);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.AddRange(treatsMigraine, treatsStomach);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        result.TakeableDrugs.Should().ContainSingle(d => d.DrugId == treatsMigraine.Id);
        result.TakeableDrugs.Should().NotContain(d => d.DrugId == treatsStomach.Id);
        result.BlockedDrugs.Should().NotContain(b => b.DrugId == treatsStomach.Id);
        result.ActiveDrugs.Should().NotContain(a => a.DrugId == treatsStomach.Id);
    }

    [Fact]
    public async Task Active_drugs_sorted_by_effect_ends_at_ascending()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(fx.User.Id, symptom.Id, severity: 5);

        // soonEnd ends first (taken 5h ago, 6h window → +1h).
        // laterEnd ends later (taken 1h ago, 6h window → +5h).
        var soonEnd = NewDrug(fx.User.Id, "ZetaName-EndsSoon",
            treats: new[] { symptom.Id }, effectMin: 4, effectMax: 6);
        var laterEnd = NewDrug(fx.User.Id, "AlphaName-EndsLater",
            treats: new[] { symptom.Id }, effectMin: 4, effectMax: 6);

        var soonIntake  = Intake.Create(fx.User.Id, soonEnd.Id, doseAmount: 1,
            takenAt: clock.UtcNow.AddHours(-5));
        var laterIntake = Intake.Create(fx.User.Id, laterEnd.Id, doseAmount: 1,
            takenAt: clock.UtcNow.AddHours(-1));

        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        fx.Db.Drugs.AddRange(soonEnd, laterEnd);
        fx.Db.Intakes.AddRange(soonIntake, laterIntake);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        result.ActiveDrugs.Should().HaveCount(2);
        result.ActiveDrugs[0].DrugId.Should().Be(soonEnd.Id);
        result.ActiveDrugs[1].DrugId.Should().Be(laterEnd.Id);
    }

    [Fact]
    public async Task Throws_when_episode_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var symptom = Symptom.CreateSeed("ปวดหัว");
        var otherUser = Guid.NewGuid();
        var episode = SymptomEpisode.Start(otherUser, symptom.Id, severity: 4);
        fx.Db.Symptoms.Add(symptom);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(episode.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }

    [Fact]
    public async Task Throws_when_episode_id_does_not_exist()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var act = async () => await Build(fx, clock).Handle(
            new GetTakeMedicationContextQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }
}
