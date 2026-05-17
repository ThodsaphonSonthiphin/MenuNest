using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Reports.GetDoctorReport;

/// <summary>
/// Computes and returns the full doctor-report payload for a given share
/// token. Run ONCE per share open — clarity over micro-optimisation. The
/// shape mirrors <c>docs/mocks/doctor-report-mock.html</c> so the SPA can
/// render the report from JSON without further server hops.
/// </summary>
public sealed class GetDoctorReportHandler : IQueryHandler<GetDoctorReportQuery, DoctorReportDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IShareTokenService _shareTokens;
    private readonly IClock _clock;

    // Clinical thresholds used by BuildClinicalFlags. Per ICHD-3 / MOH
    // guidelines: > 10 acute med days / 30 days = MOH risk; >= 8 attacks /
    // 30 days approaches the chronic-migraine boundary (15+); >= 4 fully
    // disabled days indicates a meaningful MIDAS-style burden.
    private const int MohRiskAcuteMedDaysPer30Days = 10;
    private const int FrequencyNearChronicAttacksPer30Days = 8;
    private const int FunctionalDisabilityDaysThreshold = 4;

    // Treatment-efficacy heuristic: an intake is considered to have
    // delivered "relief" if the same episode resolved within this window
    // of the intake OR a follow-up ping on that episode within the
    // window reported Resolved/Improved.
    private static readonly TimeSpan ReliefWindow = TimeSpan.FromMinutes(60);

    public GetDoctorReportHandler(
        IApplicationDbContext db,
        IShareTokenService shareTokens,
        IClock clock)
    {
        _db = db;
        _shareTokens = shareTokens;
        _clock = clock;
    }

    public async ValueTask<DoctorReportDto> Handle(GetDoctorReportQuery query, CancellationToken ct)
    {
        // 1. Verify token signature, expiry, issuer/audience.
        var claims = _shareTokens.Verify(query.Token);
        var hash = _shareTokens.Hash(query.Token);
        var now = _clock.UtcNow;

        // 2. Look up the persisted link and re-check revocation/expiry
        //    against the DB. The token's own expiry is already checked by
        //    Verify but a separate DB check is what enforces user-driven
        //    revocation.
        var link = await _db.ShareLinks
            .FirstOrDefaultAsync(l => l.TokenHash == hash, ct)
            ?? throw new DomainException("Share link revoked or expired.");

        if (!link.IsValidAt(now))
            throw new DomainException("Share link revoked or expired.");

        // 3. Record the access for the user-visible "X opens" indicator.
        link.RecordAccess();

        // 4. Load the user.
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == claims.UserId, ct)
            ?? throw new DomainException("User on share link no longer exists.");

        // 5. Episodes in range: [DateFrom 00:00 UTC, DateTo + 1d 00:00 UTC)
        var rangeStart = claims.DateFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeEnd = claims.DateTo.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var episodes = await _db.SymptomEpisodes
            .Where(e => e.UserId == user.Id
                && e.StartedAt >= rangeStart
                && e.StartedAt < rangeEnd)
            .OrderBy(e => e.StartedAt)
            .ToListAsync(ct);

        var episodeIds = episodes.Select(e => e.Id).ToHashSet();

        // 6. Intakes — load once, project per-episode in memory.
        var intakes = episodeIds.Count == 0
            ? new List<Intake>()
            : await _db.Intakes
                .Where(i => i.SymptomEpisodeId != null
                    && episodeIds.Contains(i.SymptomEpisodeId!.Value))
                .OrderBy(i => i.TakenAt)
                .ToListAsync(ct);

        // 7. Follow-up pings.
        var pings = episodeIds.Count == 0
            ? new List<FollowUpPing>()
            : await _db.FollowUpPings
                .Where(p => episodeIds.Contains(p.SymptomEpisodeId))
                .OrderBy(p => p.ScheduledAt)
                .ToListAsync(ct);

        // 8. Name lookups for Symptoms/Triggers/Drugs.
        var symptomIds = episodes.Select(e => e.SymptomId).Distinct().ToHashSet();
        var triggerIds = episodes.SelectMany(e => e.TriggerIds).Distinct().ToHashSet();
        var drugIds = intakes.Select(i => i.DrugId).Distinct().ToHashSet();

        var symptoms = symptomIds.Count == 0
            ? new List<Symptom>()
            : await _db.Symptoms
                .Where(s => symptomIds.Contains(s.Id))
                .ToListAsync(ct);

        var triggers = triggerIds.Count == 0
            ? new List<Trigger>()
            : await _db.Triggers
                .Where(t => triggerIds.Contains(t.Id))
                .ToListAsync(ct);

        var drugs = drugIds.Count == 0
            ? new List<Drug>()
            : await _db.Drugs
                .Where(d => drugIds.Contains(d.Id))
                .ToListAsync(ct);

        var symptomNames = symptoms.ToDictionary(s => s.Id, s => s.Name);
        var triggerNames = triggers.ToDictionary(t => t.Id, t => t.Name);
        var drugById = drugs.ToDictionary(d => d.Id);

        // 9. Persist the recorded access (and any concurrent EF changes).
        //    Saved AFTER queries because EF would otherwise track the
        //    ShareLink update during the larger reads.
        await _db.SaveChangesAsync(ct);

        // 10. Build all report sections.
        var durationDays = claims.DateTo.DayNumber - claims.DateFrom.DayNumber + 1;

        var intakesByEpisode = intakes
            .Where(i => i.SymptomEpisodeId.HasValue)
            .GroupBy(i => i.SymptomEpisodeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var pingsByEpisode = pings
            .GroupBy(p => p.SymptomEpisodeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var summary = BuildSummary(episodes, intakes);
        var clinicalFlags = BuildClinicalFlags(summary, durationDays);
        var triggerCorrelations = BuildTriggerCorrelations(episodes, triggerNames);
        var treatmentEfficacy = BuildTreatmentEfficacy(intakes, episodes, pings, drugById);
        var patterns = BuildPatterns(episodes);
        var noDrugEvents = BuildNoDrugEvents(episodes, symptomNames);
        var days = BuildDays(episodes, intakesByEpisode, pingsByEpisode, symptomNames, drugById);

        return new DoctorReportDto(
            PatientName: user.DisplayName,
            DateFrom: claims.DateFrom,
            DateTo: claims.DateTo,
            DurationDays: durationDays,
            GeneratedAtUtc: now,
            Summary: summary,
            ClinicalFlags: clinicalFlags,
            TriggerCorrelations: triggerCorrelations,
            TreatmentEfficacy: treatmentEfficacy,
            Patterns: patterns,
            NoDrugEvents: noDrugEvents,
            Days: days);
    }

    // ------------------------------------------------------------------
    // Section builders
    // ------------------------------------------------------------------

    private static DoctorReportSummary BuildSummary(
        IReadOnlyList<SymptomEpisode> episodes,
        IReadOnlyList<Intake> intakes)
    {
        var total = episodes.Count;
        if (total == 0)
        {
            return new DoctorReportSummary(
                TotalAttacks: 0, DaysAffected: 0, AcuteMedDays: 0,
                AverageDurationHours: 0, AveragePeakSeverity: 0,
                SevereAttacksCount: 0, DaysFullyDisabled: 0,
                AttacksWithAura: 0, AuraPercentage: 0);
        }

        var daysAffected = episodes.Select(e => DateOnly.FromDateTime(e.StartedAt)).Distinct().Count();
        var acuteMedDays = intakes.Select(i => DateOnly.FromDateTime(i.TakenAt)).Distinct().Count();

        var resolved = episodes.Where(e => e.EndedAt.HasValue).ToList();
        var avgDuration = resolved.Count == 0
            ? 0.0
            : resolved.Average(e => (e.EndedAt!.Value - e.StartedAt).TotalHours);

        var avgSeverity = episodes.Average(e => (double)e.Severity);
        var severe = episodes.Count(e => e.Severity >= 8);

        var fullyDisabled = episodes
            .Where(e => e.FunctionalImpact == FunctionalImpact.SevereBedrest)
            .Select(e => DateOnly.FromDateTime(e.StartedAt))
            .Distinct()
            .Count();

        var withAura = episodes.Count(e => e.HasAura == true);
        var auraPct = total == 0 ? 0.0 : Math.Round((double)withAura / total * 100, 1);

        return new DoctorReportSummary(
            TotalAttacks: total,
            DaysAffected: daysAffected,
            AcuteMedDays: acuteMedDays,
            AverageDurationHours: Math.Round(avgDuration, 1),
            AveragePeakSeverity: Math.Round(avgSeverity, 1),
            SevereAttacksCount: severe,
            DaysFullyDisabled: fullyDisabled,
            AttacksWithAura: withAura,
            AuraPercentage: auraPct);
    }

    /// <summary>
    /// Builds clinical-flag banners. Counts are normalised to a 30-day
    /// window when the report covers a different range so the thresholds
    /// (MOH = 10/30, near-chronic = 8/30) are comparable.
    /// </summary>
    private static IReadOnlyList<DoctorReportFlag> BuildClinicalFlags(
        DoctorReportSummary summary, int durationDays)
    {
        var flags = new List<DoctorReportFlag>();

        var scale = durationDays > 0 ? 30.0 / durationDays : 1.0;
        var normalisedMedDays = summary.AcuteMedDays * scale;
        var normalisedAttacks = summary.TotalAttacks * scale;

        if (normalisedMedDays > MohRiskAcuteMedDaysPer30Days)
        {
            flags.Add(new DoctorReportFlag(
                Code: "MOH_RISK",
                Severity: "danger",
                Title: "Medication Overuse Headache (MOH) risk",
                Detail: $"Acute med usage: {summary.AcuteMedDays}/{durationDays} days " +
                        $"({Math.Round(normalisedMedDays, 1)}/30 normalised) — exceeds the 10-days-per-month threshold."));
        }

        if (normalisedAttacks >= FrequencyNearChronicAttacksPer30Days)
        {
            flags.Add(new DoctorReportFlag(
                Code: "FREQUENCY_NEAR_CHRONIC",
                Severity: "warning",
                Title: "Attack frequency approaching chronic boundary",
                Detail: $"{summary.TotalAttacks} attacks in {durationDays} days " +
                        $"({Math.Round(normalisedAttacks, 1)}/30 normalised). Chronic migraine threshold is 15+/month over 3 months."));
        }

        if (summary.DaysFullyDisabled >= FunctionalDisabilityDaysThreshold)
        {
            flags.Add(new DoctorReportFlag(
                Code: "FUNCTIONAL_DISABILITY",
                Severity: "warning",
                Title: "Functional disability",
                Detail: $"{summary.DaysFullyDisabled} days with severe bedrest impact — meaningful MIDAS-style burden."));
        }

        return flags;
    }

    private static IReadOnlyList<TriggerCorrelationDto> BuildTriggerCorrelations(
        IReadOnlyList<SymptomEpisode> episodes,
        IReadOnlyDictionary<Guid, string> triggerNames)
    {
        var total = episodes.Count;
        if (total == 0) return Array.Empty<TriggerCorrelationDto>();

        var counts = new Dictionary<Guid, int>();
        foreach (var ep in episodes)
        {
            foreach (var tid in ep.TriggerIds)
            {
                counts[tid] = counts.GetValueOrDefault(tid) + 1;
            }
        }

        return counts
            .Select(kv => new TriggerCorrelationDto(
                TriggerId: kv.Key,
                TriggerName: triggerNames.GetValueOrDefault(kv.Key, "(unknown trigger)"),
                AttackCount: kv.Value,
                Percentage: Math.Round((double)kv.Value / total * 100, 1)))
            .OrderByDescending(t => t.AttackCount)
            .ThenBy(t => t.TriggerName)
            .ToList();
    }

    /// <summary>
    /// Per-drug efficacy. Definition of "relief": for each Intake row,
    /// the drug provided relief if EITHER:
    /// <list type="bullet">
    ///   <item>The intake's episode resolved within <see cref="ReliefWindow"/>
    ///   minutes of the intake; OR</item>
    ///   <item>A follow-up ping on the same episode within
    ///   <see cref="ReliefWindow"/> minutes after the intake reported
    ///   Resolved or Improved.</item>
    /// </list>
    /// "Average onset" averages the minutes-to-relief across the intakes
    /// that delivered relief. Ignored intakes with no episode link are
    /// excluded from the calculation.
    /// </summary>
    private static IReadOnlyList<TreatmentEfficacyDto> BuildTreatmentEfficacy(
        IReadOnlyList<Intake> intakes,
        IReadOnlyList<SymptomEpisode> episodes,
        IReadOnlyList<FollowUpPing> pings,
        IReadOnlyDictionary<Guid, Drug> drugById)
    {
        var episodeById = episodes.ToDictionary(e => e.Id);
        var pingsByEpisode = pings.GroupBy(p => p.SymptomEpisodeId).ToDictionary(g => g.Key, g => g.ToList());

        var byDrug = intakes
            .Where(i => i.SymptomEpisodeId.HasValue && drugById.ContainsKey(i.DrugId))
            .GroupBy(i => i.DrugId);

        var rows = new List<TreatmentEfficacyDto>();
        foreach (var group in byDrug)
        {
            var drug = drugById[group.Key];
            var doseCount = 0;
            var reliefCount = 0;
            var onsetMinutesTotals = new List<double>();

            foreach (var intake in group)
            {
                doseCount++;
                var (relief, onsetMinutes) = EvaluateRelief(intake, episodeById, pingsByEpisode);
                if (relief)
                {
                    reliefCount++;
                    if (onsetMinutes.HasValue)
                        onsetMinutesTotals.Add(onsetMinutes.Value);
                }
            }

            var reliefPct = doseCount == 0 ? 0.0 : Math.Round((double)reliefCount / doseCount * 100, 1);
            var avgOnset = onsetMinutesTotals.Count == 0 ? 0.0 : Math.Round(onsetMinutesTotals.Average(), 1);

            rows.Add(new TreatmentEfficacyDto(
                DrugId: drug.Id,
                DrugName: drug.Name,
                DrugType: drug.DrugType,
                DoseCount: doseCount,
                ReliefCount: reliefCount,
                ReliefPercentage: reliefPct,
                AverageOnsetMinutes: avgOnset));
        }

        return rows
            .OrderByDescending(r => r.ReliefPercentage)
            .ThenByDescending(r => r.DoseCount)
            .ToList();
    }

    private static (bool DidRelieve, double? OnsetMinutes) EvaluateRelief(
        Intake intake,
        IReadOnlyDictionary<Guid, SymptomEpisode> episodeById,
        IReadOnlyDictionary<Guid, List<FollowUpPing>> pingsByEpisode)
    {
        if (!intake.SymptomEpisodeId.HasValue) return (false, null);
        if (!episodeById.TryGetValue(intake.SymptomEpisodeId.Value, out var episode))
            return (false, null);

        // Direct episode resolution within the window after this intake.
        if (episode.EndedAt.HasValue)
        {
            var gap = episode.EndedAt.Value - intake.TakenAt;
            if (gap >= TimeSpan.Zero && gap <= ReliefWindow)
                return (true, gap.TotalMinutes);
        }

        // Or: a follow-up ping after this intake reported Resolved/Improved.
        if (pingsByEpisode.TryGetValue(episode.Id, out var episodePings))
        {
            foreach (var ping in episodePings.OrderBy(p => p.RespondedAt ?? p.ScheduledAt))
            {
                if (ping.RespondedAt is null) continue;
                if (ping.Response is not (PingResponse.Resolved or PingResponse.Improved)) continue;

                var gap = ping.RespondedAt.Value - intake.TakenAt;
                if (gap >= TimeSpan.Zero && gap <= ReliefWindow)
                    return (true, gap.TotalMinutes);
            }
        }

        return (false, null);
    }

    private static DoctorReportPatterns BuildPatterns(IReadOnlyList<SymptomEpisode> episodes)
    {
        var buckets = new Dictionary<string, int>
        {
            ["morning"] = 0,
            ["afternoon"] = 0,
            ["evening"] = 0,
            ["night"] = 0,
        };

        var dow = new Dictionary<DayOfWeek, int>();
        foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>())
            dow[d] = 0;

        foreach (var ep in episodes)
        {
            buckets[BucketFor(ep.StartedAt)]++;
            dow[ep.StartedAt.DayOfWeek]++;
        }

        var duringPeriod = episodes.Count(e => e.IsOnPeriod);
        var outsidePeriod = episodes.Count - duringPeriod;

        var periodDays = episodes
            .Where(e => e.IsOnPeriod)
            .Select(e => DateOnly.FromDateTime(e.StartedAt))
            .Distinct()
            .Count();
        var nonPeriodDays = episodes
            .Where(e => !e.IsOnPeriod)
            .Select(e => DateOnly.FromDateTime(e.StartedAt))
            .Distinct()
            .Count();

        var rateDuringPeriod = periodDays == 0 ? 0.0 : Math.Round((double)duringPeriod / periodDays, 2);
        var rateOutsidePeriod = nonPeriodDays == 0 ? 0.0 : Math.Round((double)outsidePeriod / nonPeriodDays, 2);

        return new DoctorReportPatterns(
            OnsetTimeBuckets: buckets,
            DayOfWeekCounts: dow,
            AttacksDuringPeriod: duringPeriod,
            AttacksOutsidePeriod: outsidePeriod,
            AttackRateDuringPeriod: rateDuringPeriod,
            AttackRateOutsidePeriod: rateOutsidePeriod);
    }

    private static string BucketFor(DateTime utc)
    {
        // Bucketing is done in UTC. Frontend may re-bucket in viewer-local
        // time if a clinician asks — exposed as raw counts so we can.
        var hour = utc.Hour;
        return hour switch
        {
            >= 6 and < 12 => "morning",
            >= 12 and < 18 => "afternoon",
            >= 18 and < 24 => "evening",
            _ => "night",
        };
    }

    private static IReadOnlyList<NoDrugEventDto> BuildNoDrugEvents(
        IReadOnlyList<SymptomEpisode> episodes,
        IReadOnlyDictionary<Guid, string> symptomNames)
    {
        return episodes
            .Where(e => e.NoDrugTaken)
            .OrderByDescending(e => e.StartedAt)
            .Select(e => new NoDrugEventDto(
                EpisodeId: e.Id,
                StartedAt: e.StartedAt,
                SymptomName: symptomNames.GetValueOrDefault(e.SymptomId, "(unknown symptom)"),
                Severity: e.Severity,
                Reason: e.NoDrugReasonCode))
            .ToList();
    }

    private static IReadOnlyList<DoctorReportDay> BuildDays(
        IReadOnlyList<SymptomEpisode> episodes,
        IReadOnlyDictionary<Guid, List<Intake>> intakesByEpisode,
        IReadOnlyDictionary<Guid, List<FollowUpPing>> pingsByEpisode,
        IReadOnlyDictionary<Guid, string> symptomNames,
        IReadOnlyDictionary<Guid, Drug> drugById)
    {
        var grouped = episodes
            .GroupBy(e => DateOnly.FromDateTime(e.StartedAt))
            .OrderByDescending(g => g.Key);

        var days = new List<DoctorReportDay>();
        foreach (var dayGroup in grouped)
        {
            var dayEpisodes = dayGroup.OrderBy(e => e.StartedAt).ToList();

            var doseCount = 0;
            var noDrugEvents = 0;
            var peakSeverity = 0;
            var isPeriodDay = false;

            var episodeDtos = new List<DoctorReportEpisode>();
            foreach (var ep in dayEpisodes)
            {
                var ints = intakesByEpisode.GetValueOrDefault(ep.Id) ?? new List<Intake>();
                var pgs = pingsByEpisode.GetValueOrDefault(ep.Id) ?? new List<FollowUpPing>();

                doseCount += ints.Count;
                if (ep.NoDrugTaken) noDrugEvents++;
                if (ep.Severity > peakSeverity) peakSeverity = ep.Severity;
                if (ep.IsOnPeriod) isPeriodDay = true;

                episodeDtos.Add(new DoctorReportEpisode(
                    Id: ep.Id,
                    SymptomId: ep.SymptomId,
                    SymptomName: symptomNames.GetValueOrDefault(ep.SymptomId, "(unknown symptom)"),
                    StartedAt: ep.StartedAt,
                    EndedAt: ep.EndedAt,
                    Severity: ep.Severity,
                    SeverityAfter: ep.SeverityAfter,
                    HasAura: ep.HasAura,
                    Location: ep.Location,
                    Quality: ep.Quality,
                    AssociatedSymptoms: ep.AssociatedSymptoms,
                    FunctionalImpact: ep.FunctionalImpact,
                    IsOnPeriod: ep.IsOnPeriod,
                    NoDrugTaken: ep.NoDrugTaken,
                    NoDrugReasonCode: ep.NoDrugReasonCode,
                    Intakes: ints
                        .OrderBy(i => i.TakenAt)
                        .Select(i => new DoctorReportEpisodeIntake(
                            TakenAt: i.TakenAt,
                            DrugName: drugById.TryGetValue(i.DrugId, out var d) ? d.Name : "(unknown drug)",
                            DoseAmount: i.DoseAmount))
                        .ToList(),
                    FollowUps: pgs
                        .OrderBy(p => p.ScheduledAt)
                        .Select(p => new DoctorReportEpisodeFollowUp(
                            ScheduledAt: p.ScheduledAt,
                            RespondedAt: p.RespondedAt,
                            Response: p.Response,
                            SeverityAtCheck: p.SeverityAtCheck))
                        .ToList(),
                    TriggerIds: ep.TriggerIds));
            }

            days.Add(new DoctorReportDay(
                Date: dayGroup.Key,
                IsPeriodDay: isPeriodDay,
                AttackCount: dayEpisodes.Count,
                PeakSeverity: peakSeverity,
                DoseCount: doseCount,
                NoDrugEvents: noDrugEvents,
                Episodes: episodeDtos));
        }

        return days;
    }
}
