using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Episodes.ListEpisodes;

public sealed class ListEpisodesHandler
    : IQueryHandler<ListEpisodesQuery, IReadOnlyList<EpisodeDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListEpisodesHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<EpisodeDto>> Handle(
        ListEpisodesQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var q = _db.SymptomEpisodes.Where(e => e.UserId == user.Id);

        if (query.From.HasValue)
        {
            var fromUtc = query.From.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(e => e.StartedAt >= fromUtc);
        }
        if (query.To.HasValue)
        {
            // Inclusive upper bound — through end of day.
            var toUtc = query.To.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            q = q.Where(e => e.StartedAt <= toUtc);
        }
        if (query.SymptomId.HasValue)
        {
            q = q.Where(e => e.SymptomId == query.SymptomId.Value);
        }
        if (query.OnlyResolved == true)
        {
            q = q.Where(e => e.EndedAt != null && !e.NoDrugTaken);
        }
        if (query.OnlyFailed == true)
        {
            q = q.Where(e => e.NoDrugTaken);
        }

        var episodes = await q
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync(ct);

        if (episodes.Count == 0)
            return Array.Empty<EpisodeDto>();

        var episodeIds = episodes.Select(e => e.Id).ToList();
        var symptomIds = episodes.Select(e => e.SymptomId).Distinct().ToList();

        var symptomNames = await _db.Symptoms
            .Where(s => symptomIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var intakes = await _db.Intakes
            .Where(i => i.SymptomEpisodeId != null && episodeIds.Contains(i.SymptomEpisodeId!.Value))
            .OrderBy(i => i.TakenAt)
            .ToListAsync(ct);

        var firstDrugIds = intakes
            .GroupBy(i => i.SymptomEpisodeId!.Value)
            .ToDictionary(g => g.Key, g => g.First().DrugId);
        var drugIdsToLoad = firstDrugIds.Values.Distinct().ToList();
        var drugNames = await _db.Drugs
            .Where(d => drugIdsToLoad.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        return episodes
            .Select(e =>
            {
                var count = intakes.Count(i => i.SymptomEpisodeId == e.Id);
                string? firstDrugName = null;
                if (firstDrugIds.TryGetValue(e.Id, out var drugId)
                    && drugNames.TryGetValue(drugId, out var name))
                {
                    firstDrugName = name;
                }

                return new EpisodeDto(
                    e.Id,
                    e.SymptomId,
                    symptomNames.GetValueOrDefault(e.SymptomId, string.Empty),
                    e.StartedAt,
                    e.EndedAt,
                    e.Severity,
                    e.SeverityAfter,
                    e.IsOnPeriod,
                    e.NoDrugTaken,
                    e.NoDrugReasonCode,
                    e.RetroClosed,
                    IntakeCount: count,
                    FirstDrugName: firstDrugName);
            })
            .ToList();
    }
}
