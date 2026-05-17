using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.FollowUps.GetPendingPings;

/// <summary>
/// Loads due pings + their context (symptom, severity, most-recent
/// intake) for the dispatcher. Performs a small fan-out of queries
/// rather than one big join so the InMemory provider used in tests
/// can execute it.
/// </summary>
public sealed class GetPendingPingsHandler
    : IQueryHandler<GetPendingPingsQuery, IReadOnlyList<PendingPingDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public GetPendingPingsHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async ValueTask<IReadOnlyList<PendingPingDto>> Handle(
        GetPendingPingsQuery query, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var limit = query.Limit <= 0 ? 50 : query.Limit;

        // Step A — due pings.
        var pings = await _db.FollowUpPings
            .Where(p => p.Status == PingStatus.Pending && p.ScheduledAt <= now)
            .OrderBy(p => p.ScheduledAt)
            .Take(limit)
            .ToListAsync(ct);

        if (pings.Count == 0)
            return Array.Empty<PendingPingDto>();

        var episodeIds = pings.Select(p => p.SymptomEpisodeId).Distinct().ToList();

        var episodes = await _db.SymptomEpisodes
            .Where(e => episodeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var symptomIds = episodes.Values.Select(e => e.SymptomId).Distinct().ToList();
        var symptomNames = await _db.Symptoms
            .Where(s => symptomIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        // Step B — most-recent intake per episode (for "X minutes since")
        // and the drug name it referenced.
        var intakes = await _db.Intakes
            .Where(i => i.SymptomEpisodeId != null
                && episodeIds.Contains(i.SymptomEpisodeId!.Value))
            .ToListAsync(ct);

        var lastIntakeByEpisode = intakes
            .GroupBy(i => i.SymptomEpisodeId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(i => i.TakenAt).First());

        var drugIds = lastIntakeByEpisode.Values.Select(i => i.DrugId).Distinct().ToList();
        var drugNames = await _db.Drugs
            .Where(d => drugIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        // Step C — project.
        var result = new List<PendingPingDto>(pings.Count);
        foreach (var ping in pings)
        {
            if (!episodes.TryGetValue(ping.SymptomEpisodeId, out var episode))
                continue; // dangling ping — skip rather than fault dispatcher.

            var symptomName = symptomNames.GetValueOrDefault(episode.SymptomId, string.Empty);

            DateTime? lastIntakeAt = null;
            string? lastDrugName = null;
            int minutesSinceLast = 0;
            if (lastIntakeByEpisode.TryGetValue(episode.Id, out var lastIntake))
            {
                lastIntakeAt = lastIntake.TakenAt;
                lastDrugName = drugNames.GetValueOrDefault(lastIntake.DrugId);
                minutesSinceLast = (int)(now - lastIntake.TakenAt).TotalMinutes;
                if (minutesSinceLast < 0)
                    minutesSinceLast = 0;
            }

            result.Add(new PendingPingDto(
                PingId: ping.Id,
                EpisodeId: episode.Id,
                UserId: episode.UserId,
                SymptomId: episode.SymptomId,
                SymptomName: symptomName,
                ScheduledAt: ping.ScheduledAt,
                Severity: episode.Severity,
                LastIntakeAt: lastIntakeAt,
                LastDrugName: lastDrugName,
                MinutesSinceLastIntake: minutesSinceLast));
        }

        return result;
    }
}
