using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Intakes.GetTakeMedicationContext;

public sealed class GetTakeMedicationContextHandler
    : IQueryHandler<GetTakeMedicationContextQuery, TakeMedicationContextDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IClock _clock;

    public GetTakeMedicationContextHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _clock = clock;
    }

    public async ValueTask<TakeMedicationContextDto> Handle(
        GetTakeMedicationContextQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        // Step A — load and authorise the episode.
        var episode = await _db.SymptomEpisodes
            .FirstOrDefaultAsync(e => e.Id == query.EpisodeId && e.UserId == user.Id, ct)
            ?? throw new DomainException("Episode not found.");

        // Step B — all of this user's (non-deleted) drugs.
        var drugs = await _db.Drugs
            .Where(d => d.UserId == user.Id && d.DeletedAt == null)
            .ToListAsync(ct);

        var now = _clock.UtcNow;
        var todayStartUtc = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var tomorrowStartUtc = todayStartUtc.AddDays(1);
        var since24h = now.AddHours(-24);

        // Step C — most-recent intake per drug within the last 24h (limits scan).
        var recentIntakes = await _db.Intakes
            .Where(i => i.UserId == user.Id && i.TakenAt > since24h)
            .ToListAsync(ct);

        var lastIntakeByDrug = recentIntakes
            .GroupBy(i => i.DrugId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.TakenAt).First());

        // Step D — today's dose totals per drug (UTC day boundary).
        var todayIntakes = await _db.Intakes
            .Where(i => i.UserId == user.Id && i.TakenAt >= todayStartUtc)
            .ToListAsync(ct);

        var todayDoseSumByDrug = todayIntakes
            .GroupBy(i => i.DrugId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.DoseAmount));

        // Step E — partition by category.
        var activeDrugs = new List<ActiveDrugDto>();
        var takeable = new List<TakeableDrugDto>();
        var blocked = new List<BlockedDrugDto>();
        var activeDrugIds = new HashSet<Guid>();

        foreach (var drug in drugs)
        {
            // Compute "active" state first — it's used by both partitions.
            DateTime? effectEndsAt = null;
            if (lastIntakeByDrug.TryGetValue(drug.Id, out var lastIntake))
            {
                var endsAt = lastIntake.TakenAt.AddHours(drug.EffectDurationMaxHours);
                if (endsAt > now)
                {
                    effectEndsAt = endsAt;
                    activeDrugIds.Add(drug.Id);
                    var totalMinutes = (endsAt - lastIntake.TakenAt).TotalMinutes;
                    var elapsedMinutes = (now - lastIntake.TakenAt).TotalMinutes;
                    var progressPct = totalMinutes > 0
                        ? Math.Round(elapsedMinutes / totalMinutes * 100.0, 1)
                        : 0.0;
                    var remainingMinutes = (int)(endsAt - now).TotalMinutes;

                    activeDrugs.Add(new ActiveDrugDto(
                        DrugId: drug.Id,
                        DrugName: drug.Name,
                        DoseStrength: drug.DoseStrength,
                        LastTakenAt: lastIntake.TakenAt,
                        EffectEndsAt: endsAt,
                        RemainingMinutes: remainingMinutes,
                        ProgressPct: progressPct));
                }
            }
        }

        // Second pass: classify drugs that treat the episode's symptom as
        // takeable or blocked. Drugs that don't treat the symptom are dropped
        // entirely (silently — per spec they're "not included in any list").
        foreach (var drug in drugs)
        {
            var treatsSymptom = drug.TreatsSymptomIds.Contains(episode.SymptomId);
            if (!treatsSymptom)
                continue;

            var todaySum = todayDoseSumByDrug.TryGetValue(drug.Id, out var s) ? s : 0;

            // StillActive — drug is in active list and treats this symptom.
            if (activeDrugIds.Contains(drug.Id))
            {
                var endsAt = lastIntakeByDrug[drug.Id].TakenAt
                    .AddHours(drug.EffectDurationMaxHours);
                blocked.Add(new BlockedDrugDto(
                    DrugId: drug.Id,
                    DrugName: drug.Name,
                    DoseStrength: drug.DoseStrength,
                    Reason: BlockedReason.StillActive,
                    AvailableAt: endsAt));
                continue;
            }

            // OutOfStock takes precedence over MaxDoseReached when both apply.
            if (drug.StockCount == 0)
            {
                blocked.Add(new BlockedDrugDto(
                    DrugId: drug.Id,
                    DrugName: drug.Name,
                    DoseStrength: drug.DoseStrength,
                    Reason: BlockedReason.OutOfStock,
                    AvailableAt: null));
                continue;
            }

            if (todaySum >= drug.MaxDailyDose)
            {
                blocked.Add(new BlockedDrugDto(
                    DrugId: drug.Id,
                    DrugName: drug.Name,
                    DoseStrength: drug.DoseStrength,
                    Reason: BlockedReason.MaxDoseReached,
                    AvailableAt: tomorrowStartUtc));
                continue;
            }

            // Spec: takeable if next dose (1) would not exceed cap.
            if (todaySum + 1 <= drug.MaxDailyDose)
            {
                takeable.Add(new TakeableDrugDto(
                    DrugId: drug.Id,
                    DrugName: drug.Name,
                    DoseStrength: drug.DoseStrength,
                    DrugType: drug.DrugType,
                    StockCount: drug.StockCount,
                    EffectDurationMinHours: drug.EffectDurationMinHours,
                    EffectDurationMaxHours: drug.EffectDurationMaxHours));
            }
        }

        // Step F — sort each list.
        activeDrugs = activeDrugs.OrderBy(a => a.EffectEndsAt).ToList();
        takeable = takeable.OrderBy(t => t.DrugName, StringComparer.Ordinal).ToList();
        blocked = blocked.OrderBy(b => b.DrugName, StringComparer.Ordinal).ToList();

        // Step G — symptom name + response.
        var symptomName = await _db.Symptoms
            .Where(s => s.Id == episode.SymptomId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);

        return new TakeMedicationContextDto(
            SymptomEpisodeId: episode.Id,
            SymptomId: episode.SymptomId,
            SymptomName: symptomName,
            CurrentSeverity: episode.Severity,
            ActiveDrugs: activeDrugs,
            TakeableDrugs: takeable,
            BlockedDrugs: blocked);
    }
}
