using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Episodes.GetActiveEpisodes;

public sealed class GetActiveEpisodesHandler
    : IQueryHandler<GetActiveEpisodesQuery, IReadOnlyList<EpisodeDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetActiveEpisodesHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<EpisodeDto>> Handle(
        GetActiveEpisodesQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var episodes = await _db.SymptomEpisodes
            .Where(e => e.UserId == user.Id && e.EndedAt == null)
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync(ct);

        if (episodes.Count == 0)
            return Array.Empty<EpisodeDto>();

        var episodeIds = episodes.Select(e => e.Id).ToList();
        var symptomIds = episodes.Select(e => e.SymptomId).Distinct().ToList();

        var symptomNames = await _db.Symptoms
            .Where(s => symptomIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var intakesByEpisode = await _db.Intakes
            .Where(i => i.SymptomEpisodeId != null && episodeIds.Contains(i.SymptomEpisodeId!.Value))
            .OrderBy(i => i.TakenAt)
            .ToListAsync(ct);

        var firstDrugIds = intakesByEpisode
            .GroupBy(i => i.SymptomEpisodeId!.Value)
            .ToDictionary(g => g.Key, g => g.First().DrugId);
        var drugIdsToLoad = firstDrugIds.Values.Distinct().ToList();
        var drugNames = await _db.Drugs
            .Where(d => drugIdsToLoad.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        return episodes
            .Select(e =>
            {
                var intakes = intakesByEpisode.Where(i => i.SymptomEpisodeId == e.Id).ToList();
                string? firstDrugName = null;
                if (firstDrugIds.TryGetValue(e.Id, out var firstDrugId)
                    && drugNames.TryGetValue(firstDrugId, out var name))
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
                    IntakeCount: intakes.Count,
                    FirstDrugName: firstDrugName);
            })
            .ToList();
    }
}
